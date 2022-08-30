using System.Linq.Expressions;

namespace CloudMesh.DataBlocks
{
    public interface IDataBlockContainer
    {
        IDataBlock ChildOf<T>(Expression<Func<T>> newExpression, string? name = null)
            where T : IDataBlock;
        void RemoveChild(IDataBlock child);
    }
}
