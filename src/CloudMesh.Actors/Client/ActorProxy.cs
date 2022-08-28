namespace CloudMesh.Actors.Client
{
    public static class ActorProxy
    {
        internal static IProxyActivator? Instance = null;

        public static TActorInterface Create<TActorInterface>(string id) where TActorInterface : IActor
        {
            if (Instance is null)
                throw new InvalidOperationException("Actor client has not been initialized!");
            return Instance.Create<TActorInterface>(id);
        }
    }
}