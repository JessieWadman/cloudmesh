using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;

namespace CloudMesh.NetworkMutex.DynamoDB.Internal;

internal static class DynamoDbHelper
{
    private static LeaseDto FromAttributeMap(IAmazonDynamoDB client, Dictionary<string, AttributeValue> values)
    {
        var doc = Document.FromAttributeMap(values);
        using var ctx = new DynamoDBContext(client);
        return ctx.FromDocument<LeaseDto>(doc);
    }

    internal static async ValueTask<LeaseDto?> TrySetLeaseAsync(
        string tableName,
        string mutexName,
        string instanceId,
        Func<DateTimeOffset> utcNow,
        TimeSpan leaseDuration,
        CancellationToken stoppingToken)
    {
        using var client = new AmazonDynamoDBClient();
        var now = utcNow().ToUnixTimeMilliseconds();
        var leaseUntil = (utcNow() + leaseDuration).ToUnixTimeMilliseconds();

        var getItemResponse = await client.GetItemAsync(tableName, new Dictionary<string, AttributeValue>()
        {
            [nameof(LeaseDto.MutexName)] = new(mutexName)
        }, true, stoppingToken);


        // If no lease stored for the singleton
        if (!getItemResponse.IsItemSet)
        {
            var lease = new LeaseDto
            {
                InstanceId = instanceId,
                MutexName = mutexName,
                LeaseUntil = leaseUntil
            };

            using var ctx = new DynamoDBContext(client);
            var opConfig = new DynamoDBOperationConfig
            {
                OverrideTableName = tableName
            };
            await ctx.SaveAsync(lease, opConfig, stoppingToken);
            return lease;
        }

        // Entry exists for the singleton
        var existingLease = FromAttributeMap(client, getItemResponse.Item);

        // Lease by someone else, and lease still valid?
        if (existingLease.InstanceId != instanceId && existingLease.LeaseUntil >= now)
            return null;

        // Try to acquire the lease (this is atomic and will fail if someone else beats us to it)
        var request = new UpdateItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>()
            {
                ["SingletonName"] = new(mutexName)
            }
        };

        request.ExpressionAttributeValues[":now"] =
            new AttributeValue { N = now.ToString(CultureInfo.InvariantCulture) };
        request.ExpressionAttributeValues[":leaseUntil"] = new AttributeValue
            { N = leaseUntil.ToString(CultureInfo.InvariantCulture) };
        request.ExpressionAttributeValues[":instanceId"] = new AttributeValue { S = instanceId };

        request.ConditionExpression = "(InstanceId = :instanceId) OR (LeaseUntil < :now)";
        request.UpdateExpression = "SET LeaseUntil = :leaseUntil, InstanceId = :instanceId";

        request.ReturnValues = ReturnValue.ALL_NEW;

        try
        {
            var updateItemResponse = await client.UpdateItemAsync(request, stoppingToken);
            if ((int)updateItemResponse.HttpStatusCode < 200 || (int)updateItemResponse.HttpStatusCode >= 400)
                throw new InvalidOperationException($"Failed to patch item: {updateItemResponse.HttpStatusCode}");
            return FromAttributeMap(client, updateItemResponse.Attributes);
        }
        catch (ConditionalCheckFailedException)
        {
            return null;
        }
    }
}