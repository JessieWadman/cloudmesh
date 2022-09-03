using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CloudMesh.Actors.Hosting
{
    public class ActorHost : IActorHost, IActorLifetimeNotifications, IAsyncDisposable, IDisposable
    {
        private bool disposed;
        private readonly IServiceProvider services;
        private readonly IConfiguration configuration;
        private readonly ILoggerFactory loggerFactory;
        private readonly HashSet<IHostedActor> allActors = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IHostedActor>> actorsByActorName = new();
        private readonly Task evictIdleActorsCompletion;
        private readonly CancellationTokenSource evictIdleActorStoppingTokenSource = new();

        public static IActorHost? Instance = null;

        public ActorHost(IServiceProvider services, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            // routingTable.RegisterNotifications(this);
            Instance = this;
            evictIdleActorsCompletion = EvictIdleActors(evictIdleActorStoppingTokenSource.Token);
        }

        private async Task EvictIdleActors(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken).ReturnOnCancellation(false))
            {
                IHostedActor[] snapshot;
                lock (allActors)
                {
                    snapshot = allActors.ToArray();
                }

                foreach (var actor in snapshot)
                {
                    if (DateTime.UtcNow - actor.LastInvocationUtc > actor.IdleTimeout)
                    {
                        try
                        {
                            await actor.StopAsync();
                        }
                        catch { }
                    }
                }
            }
        }

        ~ActorHost()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposeManagedResoures)
        {
            var disposeAsync = DisposeAsync();
            if (disposeAsync.IsCompleted)
                return;
            disposeAsync.AsTask().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ActorHost));
        }

        private ConcurrentDictionary<string, IHostedActor> GetActorMap(string actorType)
        {
            return actorsByActorName.GetOrAdd(actorType, _ => new ConcurrentDictionary<string, IHostedActor>());
        }

        public bool TryGetHostedActor(string actorName, string id, out IHostedActor? actor)
        {
            ThrowIfDisposed();

            actor = null;
            if (!ActorTypes.TryGetActorTypeFor(actorName, out var actorType) || actorType is null)
                return false;

            var actorTypesToInstances = GetActorMap(actorName);
            actor = actorTypesToInstances.GetOrAdd(id, _ => CreateActor(actorName, actorType, id));
            return actor is not null;
        }

        private IHostedActor CreateActor(string actorName, Type actorType, string id)
        {
            var actor = ActorFactory.Create(actorName, actorType, id, services, configuration, loggerFactory);

            lock (allActors)
            {
                allActors.Add(actor);
            }
            actor.Watch(this);

            return actor;
        }

        public void ActorDeactivated(string actorName, string id)
        {
            GetActorMap(actorName).TryRemove(id, out var actor);

            if (actor is not null)
            {
                lock (allActors)
                {
                    if (allActors.Contains(actor))
                        allActors.Remove(actor);
                }
            }
        }

        private IEnumerable<Task> StopActors(IEnumerable<IHostedActor> actors)
        {
            foreach (var actor in actors)
            {
                if (actor is not null)
                {
                    var completion = actor.StopAsync();
                    if (!completion.IsCompleted)
                        yield return completion.AsTask();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            ThrowIfDisposed();

            GC.SuppressFinalize(this);

            // Stop background task for evicting idle actors and wait
            // for it to complete.
            using (evictIdleActorStoppingTokenSource)
            {
                evictIdleActorStoppingTokenSource.Cancel();
                await evictIdleActorsCompletion;
            }

            // Snapshot all actors
            IHostedActor[] snapshot;
            lock (allActors)
            {
                snapshot = allActors.ToArray();
            }

            if (snapshot.Length == 0)
            {
                disposed = true;
                return;
            }

            var allActorsStopCalls = StopActors(snapshot).ToArray();
            if (allActorsStopCalls.Length == 0)
            {
                disposed = true;
                return;
            }

            await Task.WhenAll(allActorsStopCalls);
            disposed = true;
        }
    }
}
