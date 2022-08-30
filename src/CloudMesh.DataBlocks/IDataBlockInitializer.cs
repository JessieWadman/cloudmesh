namespace CloudMesh.DataBlocks
{
    public interface IDataBlockInitializer
    {
        IDataBlockRef Parent { set; }
        string Name { get; set; }
        string Path { get; }
    }
}
