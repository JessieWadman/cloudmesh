namespace CloudMesh.Actors.Hosting
{
    public static class ActorTypes
    {
        private static readonly Dictionary<string, (Type, Type)> actorNamesToActorInterfaceTypes = new();

        public static void Register<TActor, TImplementation>()
            where TActor : class, IActor
            where TImplementation : Actor, TActor
        {
            actorNamesToActorInterfaceTypes[typeof(TActor).Name] = (typeof(TActor), typeof(TImplementation));
        }

        public static bool TryGetActorTypeFor(string actorName, out Type? actorType, out Type? implementationType)
        {
            var result = actorNamesToActorInterfaceTypes.TryGetValue(actorName, out var types);
            actorType = types.Item1;
            implementationType = types.Item2;
            return result;
        }
    }
}
