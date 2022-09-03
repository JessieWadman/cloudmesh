namespace CloudMesh.Routing
{
    public static class Router
    {
        public static IRouteResolver RouteResolver { get; set; } = new MockRouteResolver();
    }
}
