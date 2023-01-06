namespace CloudMesh.Serialization
{
    public class DynamicReturnValue
    {
        public dynamic Value { get; set; } = default;
        public ExceptionContext? Exception { get; set; }
    }
}
