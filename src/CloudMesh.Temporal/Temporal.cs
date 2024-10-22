using System.Buffers;
using System.Collections.Concurrent;
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
/// employee.Set(default, e => e.Name, "Bob"); // Will by default return Bob for name
/// employee.Set(new DateOnly(2024, 12, 01), e => e.Name, "Bobby"); // At 2024-12-01 and after the name returned will be Bobby
/// employee.Getat(default); // Returns an Employee object with Id = 14 and Name = "Bob"
/// employee.GetAt(new DateOnly(2024, 12, 01)); // Returns an Employee object with Id = 14 and Name = "Bobby"
/// </code>
/// </example>
public class Temporal<T>
{
    private readonly SortedDictionary<DateOnly, Dictionary<string, object?>> pendingChanges = new();
    
    public IEnumerable<DateOnly> GetPointInTimes() => pendingChanges.Keys;
    
    /// <summary>
    /// Set default values, such as primary key values, that should be applied to the instance before returning it.
    /// </summary>
    /// <param name="snapshot">The snapshot being returned</param>
    /// <param name="pointInTime">The point in time for the snapshot</param>
    /// <example>
    /// <code>
    ///     snapshot.Id = this.Id;
    /// </code>
    /// </example>
    protected virtual void BeforeReturnSnapshot(T snapshot, DateOnly pointInTime)
    { }

    /// <summary>
    /// Serialize the pending changes part of this object to a JSON writer.
    /// </summary>
    /// <param name="writer">The writer to use</param>
    public void SerializePendingChanges(ref Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        foreach (var entry in pendingChanges)
        {
            // Write the date as the key
            writer.WritePropertyName(entry.Key.ToString("yyyy-MM-dd"));

            // Start the array for property changes
            writer.WriteStartObject();

            foreach (var change in entry.Value)
            {
                writer.WriteString(change.Key, change.Value?.ToString());
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
    
    /// <summary>
    /// Deserializes the pending changes part of this object from a JSON reader.
    /// </summary>
    /// <param name="reader">The reader to use</param>
    /// <remarks>Will consume the remainder of the reader. If you're parsing part of the object, get the
    /// ValueSpan from your current reader that captures the pending changes, and instantiate a new reader on top of
    /// that buffer only.</remarks>
    public void DeserializePendingChanges(ref Utf8JsonReader reader)
    {
        pendingChanges.Clear();

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) 
                continue;
            
            // Read the point in time (DateOnly)
            var pointInTime = DateOnly.Parse(reader.GetString()!);
             
            // Prepare the inner dictionary to store property changes
            var changes = new Dictionary<string, object?>();

            // Start reading the changes for this date
            reader.Read(); // Move to StartObject

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var propertyPath = reader.GetString()!;
                reader.Read();
                var value = reader.GetString();  // Read the value for the property

                changes[propertyPath] = value;
            }

            pendingChanges[pointInTime] = changes;
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

        if (!pendingChanges.TryGetValue(pointInTime, out var pointInTimeChanges))
            pendingChanges[pointInTime] = pointInTimeChanges = new Dictionary<string, object?>();

        pointInTimeChanges[propertyName] = value;

        if (!clearFutureChanges)
            return;

        var clonedList = pendingChanges
            .OrderBy(kp => kp.Key)
            .Where(kp => kp.Key > pointInTime && kp.Value.ContainsKey(propertyName))
            .ToArray();

        foreach (var changes in clonedList)
        {
            changes.Value.Remove(propertyName);
            if (changes.Value.Count == 0)
                pendingChanges.Remove(changes.Key);
        }
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

        // Use binary search by leveraging SortedDictionary
        foreach (var entry in pendingChanges.Reverse())
        {
            if (entry.Key <= pointInTime && entry.Value.TryGetValue(propertyName, out var value))
                return (R?)value;
        }

        return default;
    }
    
    /// <summary>
    /// Gets a snapshot of the entire object at a given point in time.
    /// </summary>
    /// <param name="pointInTime">The point in time for which to snapshot the object</param>
    /// <returns>A snapshot representing the object at a given point in time.</returns>
    public T GetAt(DateOnly pointInTime)
    {
        // Create a new instance of type T
        var instance = Activator.CreateInstance<T>();

        // Dictionary to track the latest value for each property
        var latestPropertyValues = new Dictionary<string, object?>();

        // Get all the changes that occurred up to the given point in time
        var changesToApply = pendingChanges
            .Where(entry => entry.Key <= pointInTime)
            .OrderByDescending(entry => entry.Key)  // Sort by descending to get the latest changes first
            .SelectMany(entry => entry.Value)
            .ToList();  // Flatten the dictionary of changes

        // Apply only the latest change for each property
        foreach (var change in changesToApply)
        {
            if (!latestPropertyValues.ContainsKey(change.Key))
            {
                latestPropertyValues[change.Key] = change.Value;
            }
        }

        // Apply each unique property change to the instance
        foreach (var change in latestPropertyValues)
        {
            DotNotation.SetValue(instance!, change.Key, change.Value);  // Use SetValue from previous implementation
        }

        // Hook for any logic to be applied before returning the instance
        BeforeReturnSnapshot(instance, pointInTime);

        return instance;
    }

    /// <summary>
    /// Removes all redundant history of changes before a given point in time.
    /// </summary>
    /// <param name="pointInTime"></param>
    public void ReduceTo(DateOnly pointInTime)
    {
        // Dictionary to track the latest value for each property
        var latestPropertyValues = new Dictionary<string, object?>();

        // Get all the changes that occurred up to the given point in time
        var changesToApply = pendingChanges
            .Where(entry => entry.Key <= pointInTime)
            .OrderByDescending(entry => entry.Key)  // Sort by descending to get the latest changes first
            .SelectMany(entry => entry.Value)
            .ToList();  // Flatten the dictionary of changes

        // Apply only the latest change for each property
        foreach (var change in changesToApply)
        {
            if (!latestPropertyValues.ContainsKey(change.Key))
            {
                latestPropertyValues[change.Key] = change.Value;
            }
        }

        foreach (var key in pendingChanges.Keys.Where(k => k <= pointInTime && k != default).ToArray())
        {
            pendingChanges.Remove(key);
        }
        
        if (!pendingChanges.ContainsKey(default))
            pendingChanges.Add(default, new());
        
        foreach (var change in latestPropertyValues)
        {
            if (DefaultValueComparer.IsDefaultValue(change.Value, true))
                continue;
            
            pendingChanges[default][change.Key] = change.Value;
        }
    }

    public override string ToString()
    {
        var temp = JsonSerializer.Serialize(this)[..^1];
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer);
        SerializePendingChanges(ref writer);
        writer.Flush();
        writer.Dispose();
        if (temp != "{")
            temp += ",";
        temp += $"\"pendingChanges\":{{{Encoding.UTF8.GetString(buffer.WrittenSpan[1..].ToArray())}}}";
        return temp;
    }
}