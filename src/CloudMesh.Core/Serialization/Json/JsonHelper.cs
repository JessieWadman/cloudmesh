using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudMesh.Serialization.Json
{
    public static class JsonHelper
    {
        public static readonly JsonSerializerOptions DefaultJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = null,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 16,
            WriteIndented = false,
        };

        public static readonly JsonSerializerOptions CamelCase = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 16,
            WriteIndented = false
        };

        public static string Merge(string originalJson, string newContent)
        {
            var outputBuffer = new ArrayBufferWriter<byte>();

            using (var jDoc1 = JsonDocument.Parse(originalJson))
            using (var jDoc2 = JsonDocument.Parse(newContent))
            using (var jsonWriter = new Utf8JsonWriter(outputBuffer, new JsonWriterOptions { Indented = true }))
            {
                var root1 = jDoc1.RootElement;
                var root2 = jDoc2.RootElement;

                if (root1.ValueKind != JsonValueKind.Array && root1.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"The original JSON document to merge new content into must be a container type. Instead it is {root1.ValueKind}.");
                }

                if (root1.ValueKind != root2.ValueKind)
                {
                    return originalJson;
                }

                if (root1.ValueKind == JsonValueKind.Array)
                {
                    MergeArrays(jsonWriter, root1, root2);
                }
                else
                {
                    MergeObjects(jsonWriter, root1, root2);
                }
            }

            return Encoding.UTF8.GetString(outputBuffer.WrittenSpan);
        }

        private static void MergeObjects(Utf8JsonWriter jsonWriter, JsonElement root1, JsonElement root2)
        {
            Debug.Assert(root1.ValueKind == JsonValueKind.Object);
            Debug.Assert(root2.ValueKind == JsonValueKind.Object);

            jsonWriter.WriteStartObject();

            // Write all the properties of the first document.
            // If a property exists in both documents, either:
            // * Merge them, if the value kinds match (e.g. both are objects or arrays),
            // * Completely override the value of the first with the one from the second, if the value kind mismatches (e.g. one is object, while the other is an array or string),
            // * Or favor the value of the first (regardless of what it may be), if the second one is null (i.e. don't override the first).
            foreach (var property in root1.EnumerateObject())
            {
                var propertyName = property.Name;

                JsonValueKind newValueKind;

                if (root2.TryGetProperty(propertyName, out var newValue) && (newValueKind = newValue.ValueKind) != JsonValueKind.Null)
                {
                    jsonWriter.WritePropertyName(propertyName);

                    var originalValue = property.Value;
                    var originalValueKind = originalValue.ValueKind;

                    switch (newValueKind)
                    {
                        case JsonValueKind.Object when originalValueKind == JsonValueKind.Object:
                            MergeObjects(jsonWriter, originalValue, newValue); // Recursive call
                            break;
                        case JsonValueKind.Array when originalValueKind == JsonValueKind.Array:
                            MergeArrays(jsonWriter, originalValue, newValue);
                            break;
                        default:
                            newValue.WriteTo(jsonWriter);
                            break;
                    }
                }
                else
                {
                    property.WriteTo(jsonWriter);
                }
            }

            // Write all the properties of the second document that are unique to it.
            foreach (var property in root2.EnumerateObject())
            {
                if (!root1.TryGetProperty(property.Name, out _))
                {
                    property.WriteTo(jsonWriter);
                }
            }

            jsonWriter.WriteEndObject();
        }

        private static void MergeArrays(Utf8JsonWriter jsonWriter, JsonElement root1, JsonElement root2)
        {
            Debug.Assert(root1.ValueKind == JsonValueKind.Array);
            Debug.Assert(root2.ValueKind == JsonValueKind.Array);

            jsonWriter.WriteStartArray();

            // Write all the elements from both JSON arrays
            foreach (var element in root1.EnumerateArray())
            {
                element.WriteTo(jsonWriter);
            }
            foreach (var element in root2.EnumerateArray())
            {
                element.WriteTo(jsonWriter);
            }

            jsonWriter.WriteEndArray();
        }
    }
}
