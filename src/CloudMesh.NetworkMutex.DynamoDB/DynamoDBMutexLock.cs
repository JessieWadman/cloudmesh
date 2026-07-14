using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CloudMesh.NetworkMutex.Abstractions;

namespace CloudMesh.NetworkMutex.DynamoDB;

/// <summary>
/// A held DynamoDB lease. Disposing releases the lease immediately by resetting <c>LeaseUntil</c> to <c>0</c>,
/// under a condition that the lease is still owned by this instance — so a lease that was already taken over by
/// another holder (after expiry) is never clobbered.
/// </summary>
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

    /// <inheritdoc />
    public string Id => instanceId;

    /// <summary>
    /// Releases the lease by conditionally zeroing <c>LeaseUntil</c>. If the lease is no longer owned by this
    /// instance the conditional check fails and disposal is a no-op.
    /// </summary>
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