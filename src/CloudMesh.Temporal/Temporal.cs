using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;

namespace CloudMesh.Temporal;

/// <summary>
/// An object wrapper that can contain future changes to the object being wrapped, and allows for getting a
/// point-in-time snapshot of the object.
/// </summary>
/// <typeparam name="T">The object to wrap</typeparam>
/// <example>
/// <code>
/// var employee = new Employee();
/// employee.Id = 14;
/// employee.Set(default, e => e.Name, "Bob");
/// employee.Set(new DateOnly(2024, 12, 01), e => e.Name, "Bobby");
/// employee.GetAt(default);
/// employee.GetAt(new DateOnly(2024, 12, 01));
/// </code>
/// </example>
public class Temporal<T>
{
    protected readonly SortedDictionary<DateOnly, Dictionary<string, object?>> PendingChanges = new();

    public IEnumerable<DateOnly> GetPointInTimes() => PendingChanges.Keys;

    /// <summary>
    /// Set default values, such as primary key values, that should be applied to the instance before returning it.
    /// </summary>
    /// <param name="snapshot">The snapshot being returned</param>
    /// <param name="pointInTime">The point in time for the snapshot</param>
    protected virtual void BeforeReturnSnapshot(T snapshot, DateOnly pointInTime)
    { }

    /// <summary>
    /// Serialize the pending changes part of this object to a JSON writer.
    /// </summary>
    /// <param name="writer">The writer to use</param>
    public void SerializePendingChanges(ref Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        foreach (var entry in PendingChanges)
        {
            writer.WritePropertyName(entry.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteStartObject();

            foreach (var change in entry.Value)
                writer.WriteString(change.Key, change.Value?.ToString());

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Deserializes the pending changes part of this object from a JSON reader.
    /// </summary>
    /// <param name="reader">The reader to use</param>
    /// <remarks>
    /// Will consume the remainder of the reader. If you're parsing part of the object, get the
    /// ValueSpan from your current reader that captures the pending changes, and instantiate a new reader on top of
    /// that buffer only.
    /// </remarks>
    public void DeserializePendingChanges(ref Utf8JsonReader reader)
    {
        PendingChanges.Clear();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var pointInTime = DateOnly.ParseExact(
                reader.GetString()!,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );

            reader.Read();

            var changes = new Dictionary<string, object?>(StringComparer.Ordinal);

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var propertyPath = reader.GetString()!;
                reader.Read();

                changes[propertyPath] = reader.TokenType == JsonTokenType.Null
                    ? null
                    : reader.GetString();
            }

            PendingChanges[pointInTime] = changes;
        }
    }

    /// <summary>
    /// Sets a property value for a given point in time.
    /// </summary>
    /// <param name="pointInTime">The date the value should take effect</param>
    /// <param name="property">The property that should change. This can be a nested property.</param>
    /// <param name="value">The value that should take effect at the date</param>
    /// <param name="clearFutureChanges">If this change replaces all pending changes to the same property at later dates</param>
    /// <typeparam name="R">Type value type</typeparam>
    public void Set<R>(DateOnly pointInTime, Expression<Func<T, R>> property, R value, bool clearFutureChanges = true)
    {
        var (propertyName, _) = DotNotation.ToDotNotation(property);

        if (!PendingChanges.TryGetValue(pointInTime, out var pointInTimeChanges))
        {
            pointInTimeChanges = new Dictionary<string, object?>(StringComparer.Ordinal);
            PendingChanges.Add(pointInTime, pointInTimeChanges);
        }

        pointInTimeChanges[propertyName] = value;

        if (!clearFutureChanges)
            return;

        List<DateOnly>? emptyDates = null;

        foreach (var entry in PendingChanges)
        {
            if (entry.Key <= pointInTime)
                continue;

            if (!entry.Value.Remove(propertyName))
                continue;

            if (entry.Value.Count == 0)
                (emptyDates ??= new List<DateOnly>()).Add(entry.Key);
        }

        if (emptyDates is null)
            return;

        foreach (var date in emptyDates)
            PendingChanges.Remove(date);
    }

    /// <summary>
    /// Gets a single property value at a given point in time.
    /// </summary>
    /// <param name="pointInTime">The effective date to get the value at</param>
    /// <param name="property">The property to read, can be a nested property.</param>
    /// <typeparam name="R">The property type</typeparam>
    /// <returns>The value as it's represented at that point if time, if set at all.</returns>
    public R? Get<R>(DateOnly pointInTime, Expression<Func<T, R>> property)
    {
        var (propertyName, _) = DotNotation.ToDotNotation(property);

        object? latest = null;
        var found = false;

        foreach (var entry in PendingChanges)
        {
            if (entry.Key > pointInTime)
                break;

            if (entry.Value.TryGetValue(propertyName, out latest))
                found = true;
        }

        return found ? (R?)latest : default;
    }

    /// <summary>
    /// Gets a snapshot of the entire object at a given point in time.
    /// </summary>
    /// <param name="pointInTime">The point in time for which to snapshot the object</param>
    /// <returns>A snapshot representing the object at a given point in time.</returns>
    public T GetAt(DateOnly pointInTime)
    {
        var instance = Activator.CreateInstance<T>();
        var latestPropertyValues = CollectLatestChanges(pointInTime);

        foreach (var change in latestPropertyValues)
        {
            try
            {
                DotNotation.Compile(change.Key).SetValue(instance!, change.Value);
            }
            catch (Exception error)
            {
                throw new InvalidCastException(
                    $"Failed to set property {change.Key} to {change.Value} on {instance}",
                    error
                );
            }
        }

        BeforeReturnSnapshot(instance, pointInTime);
        return instance;
    }

    /// <summary>
    /// Removes all redundant history of changes before a given point in time.
    /// </summary>
    /// <param name="pointInTime"></param>
    public void ReduceTo(DateOnly pointInTime)
    {
        var latestPropertyValues = CollectLatestChanges(pointInTime);

        List<DateOnly>? datesToRemove = null;

        foreach (var date in PendingChanges.Keys)
        {
            if (date > pointInTime)
                break;

            (datesToRemove ??= new List<DateOnly>()).Add(date);
        }

        if (datesToRemove is not null)
        {
            foreach (var date in datesToRemove)
                PendingChanges.Remove(date);
        }

        var reduced = new Dictionary<string, object?>(latestPropertyValues.Count, StringComparer.Ordinal);

        foreach (var change in latestPropertyValues)
        {
            if (!DefaultValueComparer.IsDefaultValue(change.Value, emptyStringsAsDefault: true))
                reduced[change.Key] = change.Value;
        }

        PendingChanges[default] = reduced;
    }

    private Dictionary<string, object?> CollectLatestChanges(DateOnly pointInTime)
    {
        var latestPropertyValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var entry in PendingChanges)
        {
            if (entry.Key > pointInTime)
                break;

            foreach (var change in entry.Value)
                latestPropertyValues[change.Key] = change.Value;
        }

        return latestPropertyValues;
    }

    public override string ToString()
    {
        var json = JsonSerializer.Serialize(this);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer);

        try
        {
            SerializePendingChanges(ref writer);
            writer.Flush();
        }
        finally
        {
            writer.Dispose();
        }

        var pendingChangesJson = Encoding.UTF8.GetString(buffer.WrittenSpan);

        if (json == "{}")
            return string.Concat("{\"pendingChanges\":", pendingChangesJson, "}");

        return string.Concat(
            json.AsSpan(0, json.Length - 1),
            ",\"pendingChanges\":",
            pendingChangesJson,
            "}"
        );
    }
}