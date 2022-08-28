using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using System.Globalization;

namespace CloudMesh.Actors.Singletons
{
    public class SingletonLease : ISingletonLease
    {
        private static readonly string DefaultInstanceId = Guid.NewGuid().ToString();
        public static string tableName = Environment.GetEnvironmentVariable("leaseTableName") ?? "SingletonLease";

        public static async ValueTask<ISingletonLease?> TryAcquire(
            string singletonName, 
            TimeSpan leaseDuration, 
            CancellationToken stoppingToken, 
            string? instanceId = null)
        {
            var leaseDto = await TrySetLeaseAsync(singletonName, instanceId ?? DefaultInstanceId, leaseDuration, stoppingToken);
            if (leaseDto is not null)
                return new SingletonLease(singletonName, leaseDto.UserData, leaseDto.InstanceId);
            return null;
        }

        private static LeaseDto FromAttributeMap(IAmazonDynamoDB client, Dictionary<string, AttributeValue> values)
        {
            var doc = Document.FromAttributeMap(values);
            using var ctx = new DynamoDBContext(client);
            return ctx.FromDocument<LeaseDto>(doc);
        }

        private static async ValueTask<LeaseDto?> TrySetLeaseAsync(
            string singletonName, 
            string instanceId,
            TimeSpan leaseDuration,
            CancellationToken stoppingToken)
        {
            using var client = new AmazonDynamoDBClient();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var leaseUntil = (DateTimeOffset.UtcNow + leaseDuration).ToUnixTimeMilliseconds();

            try
            {
                var getItemResponse = await client.GetItemAsync(tableName, new Dictionary<string, AttributeValue>()
                {
                    ["SingletonName"] = new AttributeValue(singletonName)
                }, true, stoppingToken);


                // If no lease stored for the singleton
                if (!getItemResponse.IsItemSet)
                {
                    var lease = new LeaseDto
                    {
                        InstanceId = instanceId,
                        SingletonName = singletonName,
                        LeaseUntil = leaseUntil,
                        UserData = "0"
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
            }
            catch (Exception error)
            {
                throw;
            }

            // Try to acquire the lease (this is atomic and will fail if someone else beats us to it)
            var request = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue>()
                {
                    ["SingletonName"] = new AttributeValue(singletonName)
                }
            };

            request.ExpressionAttributeValues[":now"] = new AttributeValue { N = now.ToString(CultureInfo.InvariantCulture) };
            request.ExpressionAttributeValues[":leaseUntil"] = new AttributeValue { N = leaseUntil.ToString(CultureInfo.InvariantCulture) };
            request.ExpressionAttributeValues[":instanceId"] = new AttributeValue { S = instanceId.ToString() };

            request.ConditionExpression = "(InstanceId = :instanceId) OR (LeaseUntil < :now)";
            request.UpdateExpression = "SET LeaseUntil = :leaseUntil, InstanceId = :instanceId";

            request.ReturnValues = ReturnValue.ALL_NEW;

            try
            {
                var updateItemResponse = await client.UpdateItemAsync(request, stoppingToken);
                if ((int)updateItemResponse.HttpStatusCode < 200 || (int)updateItemResponse.HttpStatusCode >= 400)
                    throw new InvalidOperationException($"Failed to patch Item: {updateItemResponse.HttpStatusCode}");
                return FromAttributeMap(client, updateItemResponse.Attributes);
            }
            catch (ConditionalCheckFailedException)
            {
                return null;
            }
        }

        private SingletonLease(string singletonName, string userData, string instanceId)
        {
            SingletonName = singletonName;
            UserData = userData;
            InstanceId = instanceId;
        }

        public string SingletonName { get; private set; }
        public string UserData { get; private set; }
        public string InstanceId { get; private set; }

        public async ValueTask ReleaseAsync()
        {
            var request = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue>()
                {
                    ["SingletonName"] = new AttributeValue(SingletonName)
                }
            };

            request.ExpressionAttributeValues[":leaseUntil"] = new AttributeValue { N = "0" };
            request.ExpressionAttributeValues[":instanceId"] = new AttributeValue { S = InstanceId.ToString() };
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

        public async ValueTask UpdateUserDataAsync(string userData, TimeSpan leaseDuration)
        {
            var request = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue>()
                {
                    ["SingletonName"] = new AttributeValue(SingletonName)
                }
            };

            var leaseUntil = (DateTimeOffset.UtcNow + leaseDuration).ToUnixTimeMilliseconds();
            request.ExpressionAttributeValues[":instanceId"] = new AttributeValue { S = InstanceId.ToString() };
            request.ExpressionAttributeValues[":userData"] = new AttributeValue { S = userData };
            request.ExpressionAttributeValues[":leaseUntil"] = new AttributeValue { N = leaseUntil.ToString(CultureInfo.InvariantCulture) };
            request.ConditionExpression = "(InstanceId = :instanceId)";
            request.UpdateExpression = "SET LeaseUntil = :leaseUntil, UserData = :userData";

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

        private class LeaseDto
        {
            [DynamoDBHashKey]
            public string SingletonName { get; set; }
            public string InstanceId { get; set; }
            public long LeaseUntil { get; set; }
            public string UserData { get; set; }
        }
    }
}
