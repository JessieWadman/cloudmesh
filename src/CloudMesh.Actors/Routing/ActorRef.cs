using CloudMesh.Actors.Hosting;

namespace CloudMesh.Actors.Routing
{
    public abstract class ActorRef
    {
        public string ActorName { get; init; }
        public string Id { get; init; }
    }

    public interface ICanInvoke
    {
        Task<object?> InvokeAsync(string methodName, object?[]? args);
    }

    public abstract class ActorRef<T> where T : IActor
    {
    }

    public class LocalActorRef<T> : ActorRef<T> where T : IActor
    {
        private readonly IHostedActor actor;

        
    }

    public class RemoteActorRef<T> : ActorRef<T> where T : IActor
    {
        public string Address { get; init; }
    }
}
