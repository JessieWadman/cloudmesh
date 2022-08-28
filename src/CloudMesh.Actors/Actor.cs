using CloudMesh.Actors.Client;
using CloudMesh.Actors.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace CloudMesh.Actors
{
    public abstract class Actor : IHostedActor, IActor
    {
        public string Id { get; private set; }
        public string ActorName { get; private set; }
        protected IServiceProvider Services { get; private set; }
        protected IConfiguration Configuration { get; private set; }
        protected ILogger Logger { get; private set; }

        private Channel<Envelope> inbox;
        private readonly Task completion;
        private bool completed;
        private readonly HashSet<IActorLifetimeNotifications> watchers = new();
        private TimeSpan idleTimeout = TimeSpan.FromHours(1);
        protected DateTime LastInvocationUtc { get; private set; }

        protected ActorAddress Sender { get; private set; }
        protected long MessageId { get; private set; }

        protected Actor()
        {
            inbox = Channel.CreateBounded<Envelope>(1);
            completion = Task.Run(() => RunAsync());
        }

        protected virtual ValueTask OnActivation() => ValueTask.CompletedTask;
        protected virtual ValueTask OnBeforeDeactivation() => ValueTask.CompletedTask;
        protected virtual ValueTask OnAfterDeactivation() => ValueTask.CompletedTask;

        protected void SetIdleTimeout(TimeSpan idleTimeout) => this.idleTimeout = idleTimeout;
        

        async ValueTask IHostedActor.StopAsync()
        {
            if (completed)
                return;

            completed = true;

            // Stop inbox from receiving more messages
            inbox.Writer.Complete();

            // Wait for all messages in inbox to drain
            await inbox.Reader.Completion;

            // Wait for message loop to exit
            await completion;
        }
        Channel<Envelope> IHostedActor.GetInbox() => inbox;
        TimeSpan IHostedActor.IdleTimeout => this.idleTimeout;
        DateTime IHostedActor.LastInvocationUtc => this.LastInvocationUtc;

        private async Task RunAsync()
        {
            await OnActivation();

            var dispatcher = Dispatcher.Create(GetType(), this);

            await foreach (var item in inbox.Reader.ReadAllAsync())
            {
                Sender = item.Sender;
                MessageId = item.MessageId;

                var call = (Task)dispatcher.Invoke(item.MethodName, item.Args ?? Array.Empty<object>(), out var taskType)!;

                await call;

                this.LastInvocationUtc = DateTime.UtcNow;

                if (taskType == typeof(Task))
                    item.Complete(NoReturnType.Instance);
                else
                {
                    var result = ((dynamic)call).Result;
                    item.Complete(result);
                }
            }

            await OnBeforeDeactivation();

            lock (watchers)
            {
                foreach (var watcher in watchers)
                {
                    watcher.ActorDeactivated(ActorName, Id);
                }
            }

            await OnAfterDeactivation();
        }

        void IHostedActor.Watch(IActorLifetimeNotifications watcher)
        {
            lock (watchers)
            {
                if (!watchers.Contains(watcher))
                    watchers.Add(watcher);
            }
        }

        void IHostedActor.Unwatch(IActorLifetimeNotifications watcher)
        {
            lock (watchers)
            {
                if (watchers.Contains(watcher))
                    watchers.Remove(watcher);
            }
        }
    }
}
