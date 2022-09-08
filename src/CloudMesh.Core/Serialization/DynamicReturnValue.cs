namespace CloudMesh.Serialization
{
    public class DynamicReturnValue
    {
        public dynamic Value { get; set; }
        public ExceptionContext? Exception { get; set; }
    }
}
