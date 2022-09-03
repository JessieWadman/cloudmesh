namespace CloudMesh.Actors.Hosting
{
    public static class ActorTypes
    {
        private static readonly Dictionary<string, Type> actorNamesToActorInterfaceTypes = new();

        public static void Register<T>(string actorName) where T : Actor
        {
            actorNamesToActorInterfaceTypes[actorName] = typeof(T);
        }

        public static bool TryGetActorTypeFor(string actorName, out Type? actorType)
            => actorNamesToActorInterfaceTypes.TryGetValue(actorName, out actorType);
    }
}
