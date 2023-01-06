namespace CloudMesh.Routing
{
    public class SchemeProviderRegistry<T>
    {
        private Dictionary<string, Func<T>> factories = new();

        public void Register(string scheme, Func<T> factory)
        {
            factories[scheme] = factory;
        }

        public bool TryGet(string scheme, out T? provider)
        {
            if (!factories.TryGetValue(scheme, out var factory) || factory is null)
            {
                provider = default;
                return false;
            }
            provider = factory();
            return true;
        }
    }
}
