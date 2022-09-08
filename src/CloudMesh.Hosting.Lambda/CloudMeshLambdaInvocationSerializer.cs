using Amazon.Lambda.Core;
using System.Text.Json;

namespace CloudMesh.Hosting.Lambda
{
    internal class CloudMeshLambdaInvocationSerializer : ILambdaSerializer
    {
        private static readonly JsonDocumentOptions jDocOpts = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 32
        };

        public CloudMeshLambdaInvocationSerializer()
        {
        }

        public T Deserialize<T>(Stream requestStream)
        {
            var json = JsonDocument.Parse(requestStream, jDocOpts);
            return (T)(object)json;            
        }


        public void Serialize<T>(T response, Stream responseStream)
        {
            JsonSerializer.Serialize(responseStream, response, typeof(T), new JsonSerializerOptions()
            {
                WriteIndented = true
            });
        }

    }
}
