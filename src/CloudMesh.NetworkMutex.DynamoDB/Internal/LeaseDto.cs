using Amazon.DynamoDBv2.DataModel;

namespace CloudMesh.NetworkMutex.DynamoDB.Internal;

internal class LeaseDto
{
    [DynamoDBHashKey]
    public string? MutexName { get; set; }
    public string? InstanceId { get; init; }
    public long LeaseUntil { get; init; }
}