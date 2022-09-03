namespace CloudMesh.Routing
{
    // This instead of Uri, because Uri doesn't preserve casing for resource
    // e.g. Uri class will mangle like this: s3://MyBucket/MyPath -> s3://mybucket/MyPath
    public class ResourceIdentifier
    {
        public ResourceIdentifier(string scheme, string resource)
        {
            Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        }

        public string Scheme { get; set; }
        public string Resource { get; set; }

        public override string ToString() => $"{Scheme}://{Resource}";

        public static bool TryParse(string source, out ResourceIdentifier? address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(source))
                return false;
            var parts = source.Split("://");
            if (parts.Length != 2)
                return false;
            if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return false;
            address = new(parts[0], parts[1]);
            return true;
        }

        public static ResourceIdentifier Parse(string source)
        {
            if (!TryParse(source, out var address) || address is null)
                throw new InvalidOperationException($"Cannot parse Address {source}");
            return address;
        }

        public bool IsLocal() => string.Equals(Resource, LocalIpAddressResolver.Instance.Resolve());
    }
}