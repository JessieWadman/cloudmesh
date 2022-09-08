using CloudMesh.Routing;
using CloudMesh.Utils;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Channels;

namespace CloudMesh.Actors.Hosting
{
    public interface IActorLifetimeNotifications
    {
        void ActorDeactivated(string actorName, string id);
    }

    public interface IHostedActor
    {
        string ActorName { get; }
        string Id { get; }
        TimeSpan IdleTimeout { get; }
        DateTime LastInvocationUtc { get; }

        Channel<MethodInvocationEnvelope> GetInbox();

        void Watch(IActorLifetimeNotifications watcher);
        void Unwatch(IActorLifetimeNotifications watcher);

        ValueTask StopAsync();
    }

    public static class HostedActorExtensions
    {
        private static readonly ConcurrentDictionary<string, Counter<int>> actorInvocations = new();

        public static async Task<object?> InvokeAsync(
            this IHostedActor actor,
            string methodName,
            object[]? args,
            ResourceIdentifier sender,
            bool waitForCompletion,
            CancellationToken cancellationToken)
        {
            var envelope = new MethodInvocationEnvelope(methodName, args, sender);
            var inbox = actor.GetInbox();
            if (!inbox.Writer.TryWrite(envelope))
            {
                BackpressureMonitor.BackpressureDetected(actor.ActorName, actor.Id);
                await inbox.Writer.WriteAsync(envelope, cancellationToken);
            }

            actorInvocations.GetOrAdd(actor.ActorName, n => Metrics.Meter.CreateCounter<int>($"actorInvocations.{n}"))
                .Add(1);

            if (!waitForCompletion)
                return NoReturnType.Instance;

            var task = envelope.Completion.Task;
            await task;
            var response = envelope.Result;
            return response;
        }
    }
}