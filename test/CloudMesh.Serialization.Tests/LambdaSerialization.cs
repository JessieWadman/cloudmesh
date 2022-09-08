using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudMesh.Serialization.Tests
{
    public class LambdaSerialization
    {
        public class LambdaInvocation
        {
            [JsonPropertyName("$service")]
            public string Service { get; set; }

            [JsonPropertyName("$method")]
            public string Method { get; set; }

            [JsonPropertyName("foo")]
            public string Foo { get; set; }
        }

        [Fact]
        public void RequestsWithSpecialMonikersShouldWork()
        {
            var json = "{ \"$service\": \"test\", \"$method\": \"method\", \"foo\": \"bar\" }";
            var deserialized = JsonSerializer.Deserialize(json, typeof(LambdaInvocation), new JsonSerializerOptions());
            Assert.NotNull(deserialized);
            Assert.IsType<LambdaInvocation>(deserialized);

            var invocation = (LambdaInvocation)deserialized!;

            Assert.Equal("test", invocation.Service);
            Assert.Equal("method", invocation.Method);
            Assert.Equal("bar", invocation.Foo);
        }
    }
}