using Amazon.DynamoDBv2.DataModel;

namespace CloudMesh.NetworkMutex.DynamoDB.Internal;

internal class LeaseDto
{
    [DynamoDBHashKey]
    public required string MutexName { get; set; }
    public required string InstanceId { get; set; }
    public long LeaseUntil { get; set; }
}