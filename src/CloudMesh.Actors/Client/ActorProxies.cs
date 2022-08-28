namespace CloudMesh.Actors.Client
{
    internal record ProxyRegistration(string ServiceName, string ActorName);

    internal static class ActorProxies
    {
        private static readonly Dictionary<Type, ProxyRegistration> actorInterfaceTypesToActorNames = new();

        public static void Register<T>(string serviceName, string name) where T : IActor
        {
            actorInterfaceTypesToActorNames[typeof(T)] = new(serviceName, name);
        }

        public static bool TryGetActorNameFor<TActorInterface>(out ProxyRegistration? nameAndService)
            => actorInterfaceTypesToActorNames.TryGetValue(typeof(TActorInterface), out nameAndService);
    }
}
