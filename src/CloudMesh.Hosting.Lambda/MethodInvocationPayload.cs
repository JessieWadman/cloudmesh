using System.Text.Json.Serialization;

namespace CloudMesh.Hosting.Lambda
{
    /*
    {
        "$service": "ICartService",
        "$method": "PlaceOrderAsync",
        "produtId": "GHE131",
        "customerId" "ALFK",
        ...
    }
    */

    public class MethodInvocationPayload
    {
        [JsonPropertyName("$service")]
        public string Service { get; set; }

        [JsonPropertyName("$method")]
        public string Method { get; set; }
    }
}