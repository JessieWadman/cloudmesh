namespace CloudMesh.Actors.Hosting
{
    public interface IActorHost
    {
        bool TryGetHostedActor(string actorName, string id, out IHostedActor? actor);
    }
}
