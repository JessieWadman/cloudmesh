using Amazon.SQS;
using Amazon.SQS.Model;
using CloudMesh.Observability;
using CloudMesh.Queues.Internal;
using CloudMesh.Routing;
using System.Text.Json;

namespace CloudMesh.Aws.Queues
{
    public class SqsPublisher : IQueue
    {
        public static readonly SqsPublisher Instance = new();
        public async ValueTask SendOne<T>(ResourceInstance instance, int delayInSeconds, Func<T, string?>? deduplicationId, T value)
        {
            var body = JsonSerializer.Serialize(value);
            using var client = new AmazonSQSClient();
            await client.SendMessageAsync(new()
            {
                QueueUrl = instance.Address.Resource,
                MessageBody = body,
                MessageDeduplicationId = deduplicationId?.Invoke(value),
                DelaySeconds = delayInSeconds,
                MessageAttributes = new()
                {
                    ["OperationContext"] = new() { StringValue = OperationContext.Current.ToString() }
                }
            });
        }

        public ValueTask SendAsync<T>(ResourceInstance instance, TimeSpan? delay, Func<T, string?>? deduplicationId, T[] values, CancellationToken cancellationToken)
        {
            if (values.Length == 0)
                return ValueTask.CompletedTask;

            var delayInSeconds = 0;
            if (delay.HasValue)
                delayInSeconds = Convert.ToInt32(Math.Ceiling(delay.Value.TotalSeconds));

            if (values.Length == 1)
                return SendOne(instance, delayInSeconds, deduplicationId, values[0]);

            return new(Batch());

            async Task Batch()
            {
                using var client = new AmazonSQSClient();

                var entries = values.Select(v => new SendMessageBatchRequestEntry
                {
                    DelaySeconds = delayInSeconds,
                    MessageDeduplicationId = deduplicationId?.Invoke(v),
                    MessageBody = JsonSerializer.Serialize(v),
                    MessageAttributes = new()
                    {
                        ["OperationContext"] = new() { StringValue = OperationContext.Current.ToString() }
                    }
                });

                foreach (var batch in entries.Chunk(25))
                {
                    await client.SendMessageBatchAsync(new()
                    {
                        Entries = batch.ToList(),
                        QueueUrl = instance.Address.Resource
                    });
                }
            }
        }
    }
}
