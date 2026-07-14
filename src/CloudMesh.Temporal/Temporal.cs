using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;

namespace CloudMesh.Temporal;

/// <summary>
/// An effective-dated wrapper around an object of type <typeparamref name="T"/> that records property values
/// keyed by the <see cref="DateOnly"/> on which they take effect, and can reconstruct the object as it stood
/// (or will stand) at any point in time.
/// </summary>
/// <typeparam name="T">
/// The wrapped record type. It must have a public parameterless constructor and writable properties, since
/// snapshots are materialized via <see cref="Activator.CreateInstance{T}()"/> and populated through
/// <see cref="DotNotation"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Changes are stored as a timeline of <c>(effective date → property → value)</c> entries rather than as a
/// single mutable object. A <see cref="GetAt(DateOnly)"/> snapshot for a date <c>d</c> is built by replaying,
/// in chronological order, every change whose effective date is <b>on or before</b> <c>d</c> — so later
/// values win over earlier ones for the same property. Entries dated after <c>d</c> are ignored, which is how
/// both historical values and pending future values live side by side in the same instance.
/// </para>
/// <para>
/// Use <see cref="DateOnly">default(DateOnly)</see> as the effective date for baseline / "since the beginning
/// of time" values (for example primary keys or defaults).
/// </para>
/// <para>
/// <b>Important semantic:</b> by default, <see cref="Set{R}(DateOnly, Expression{Func{T, R}}, R, bool)"/>
/// clears any <i>later</i> pending changes to the same property, because setting a value at a date is treated
/// as "this is the value from here onward". Pass <c>clearFutureChanges: false</c> to keep already-scheduled
/// future changes intact.
/// </para>
/// <para>
/// Property selectors may be nested (e.g. <c>e =&gt; e.Address.City</c>); the paths are translated with
/// <see cref="DotNotation.ToDotNotation{TSource,TProp}"/>. Derive from this class and override
/// <see cref="BeforeReturnSnapshot"/> to stamp invariant values (such as identifiers) onto every snapshot.
/// </para>
/// <example>
/// <code>
/// var employee = new Temporal&lt;Employee&gt;();
/// employee.Set(default, e =&gt; e.Name, "Bob");                        // baseline value
/// employee.Set(new DateOnly(2024, 12, 01), e =&gt; e.Name, "Bobby");   // scheduled future change
///
/// employee.GetAt(default).Name;                       // "Bob"
/// employee.GetAt(new DateOnly(2024, 11, 30)).Name;    // "Bob"  (change not yet effective)
/// employee.GetAt(new DateOnly(2024, 12, 01)).Name;    // "Bobby"
/// </code>
/// </example>
/// </remarks>
public class Temporal<T>
{
    /// <summary>
    /// The timeline of changes, keyed by effective date and then by dot-notation property path. Kept sorted
    /// by date so replay happens in chronological order.
    /// </summary>
    protected readonly SortedDictionary<DateOnly, Dictionary<string, object?>> PendingChanges = new();

    /// <summary>
    /// Gets every effective date at which at least one change is recorded, in ascending chronological order.
    /// </summary>
    /// <returns>The distinct effective dates on this timeline.</returns>
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
    /// Records that <paramref name="property"/> takes the value <paramref name="value"/> effective on
    /// <paramref name="pointInTime"/>.
    /// </summary>
    /// <typeparam name="R">The property's value type.</typeparam>
    /// <param name="pointInTime">
    /// The effective date from which the value applies. Use <c>default</c> for a baseline value.
    /// </param>
    /// <param name="property">The property to change; may be a nested property such as <c>e =&gt; e.Address.City</c>.</param>
    /// <param name="value">The value that becomes effective on <paramref name="pointInTime"/>.</param>
    /// <param name="clearFutureChanges">
    /// When <see langword="true"/> (the default), any already-scheduled changes to the <i>same</i> property at
    /// <i>later</i> dates are removed — the new value is treated as authoritative from this date onward. Set
    /// to <see langword="false"/> to leave later pending changes to that property untouched.
    /// </param>
    /// <remarks>
    /// Setting a value at an existing effective date overwrites only that property's entry for that date;
    /// other properties recorded at the same date are preserved. If clearing future changes empties an
    /// effective date of all properties, that date is dropped from the timeline.
    /// </remarks>
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
    /// Reads a single property's effective value as of <paramref name="pointInTime"/>.
    /// </summary>
    /// <typeparam name="R">The property's value type.</typeparam>
    /// <param name="pointInTime">The effective date to evaluate the property at.</param>
    /// <param name="property">The property to read; may be a nested property.</param>
    /// <returns>
    /// The most recent value recorded on or before <paramref name="pointInTime"/>, or <c>default(R)</c> if no
    /// change to the property applies by that date.
    /// </returns>
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
    /// Materializes a full snapshot of <typeparamref name="T"/> as it stood on <paramref name="pointInTime"/>,
    /// by replaying every change effective on or before that date.
    /// </summary>
    /// <param name="pointInTime">The date at which to reconstruct the object.</param>
    /// <returns>
    /// A new <typeparamref name="T"/> instance populated with the effective values. Properties never set by
    /// that date keep their type default.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// Thrown if a recorded value cannot be assigned to its target property.
    /// </exception>
    /// <remarks><see cref="BeforeReturnSnapshot"/> is invoked on the snapshot before it is returned.</remarks>
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
    /// Compacts the timeline by collapsing all history on or before <paramref name="pointInTime"/> into a
    /// single baseline entry, discarding intermediate historical dates.
    /// </summary>
    /// <param name="pointInTime">The date up to and including which history is folded into the baseline.</param>
    /// <remarks>
    /// The effective state as of <paramref name="pointInTime"/> is computed, values that equal their type
    /// default (empty strings included) are dropped, and the result is stored under the
    /// <c>default</c> effective date. Changes scheduled <i>after</i> <paramref name="pointInTime"/> are left
    /// untouched. Use this to bound how much history an instance carries once older detail is no longer needed.
    /// </remarks>
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