using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CloudMesh.NetworkMutex.Abstractions;

namespace CloudMesh.NetworkMutex.DynamoDB;

public sealed class DynamoDbMutexLock : INetworkMutexLock
{
    private readonly string tableName;
    private readonly string mutexName;
    private readonly string instanceId;

    internal DynamoDbMutexLock(string tableName, string mutexName, string instanceId)
    {
        this.tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        this.mutexName = mutexName ?? throw new ArgumentNullException(nameof(mutexName));
        this.instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
    }

    public string Id => instanceId;

    public async ValueTask DisposeAsync()
    {
        var request = new UpdateItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>()
            {
                ["SingletonName"] = new(mutexName)
            }
        };

        request.ExpressionAttributeValues[":leaseUntil"] = new AttributeValue { N = "0" };
        request.ExpressionAttributeValues[":instanceId"] = new AttributeValue { S = instanceId };
        request.ConditionExpression = "(InstanceId = :instanceId)";
        request.UpdateExpression = "SET LeaseUntil = :leaseUntil";

        using var client = new AmazonDynamoDBClient();
        try
        {
            var updateItemResponse = await client.UpdateItemAsync(request, CancellationToken.None);
            if ((int)updateItemResponse.HttpStatusCode < 200 || (int)updateItemResponse.HttpStatusCode >= 400)
                throw new InvalidOperationException($"Failed to patch Item: {updateItemResponse.HttpStatusCode}");
        }
        catch (ConditionalCheckFailedException)
        {
        }
    }
}