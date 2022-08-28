namespace CloudMesh.Actors
{
    public record ActorAddress(string IpAddress)
    {
        public static readonly ActorAddress Local = new("127.0.0.1");
    }
}
