using Amazon.ECS;
using System.Text.Json;

namespace CartServices.Helpers
{
    public static class EcsUtils
    {
        public static async Task<(string? ClusterArn, string? TaskArn)> GetClusterAndTaskArnsAsync()
        {
            string? taskArn;
            string? clusterArn;

            using var httpClient = new HttpClient();
            var endpoint = Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4");
            var metadata = await httpClient.GetStringAsync($"{endpoint}/task");
            Console.WriteLine(metadata);
            using var jdoc = JsonDocument.Parse(metadata);
            clusterArn = jdoc.RootElement.GetProperty("Cluster").GetString();
            taskArn = jdoc.RootElement.GetProperty("TaskARN").GetString();

            Console.WriteLine($"Cluster arn: {clusterArn}, Task arn: {taskArn}");

            using var ecsClient = new AmazonECSClient();
            var members = await Proto.Cluster.AmazonECS.EcsUtils.GetMembers(ecsClient, clusterArn);

            return (clusterArn, taskArn);
        }
    }
}
