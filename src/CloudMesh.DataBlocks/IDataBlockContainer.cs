using System.Linq.Expressions;

namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// The contract for a block that supervises children: create them from a <c>() =&gt; new T(...)</c> expression
    /// and detach them when they stop. Implemented by <see cref="DataBlock"/>.
    /// </summary>
    public interface IDataBlockContainer
    {
        /// <summary>Creates and supervises a child block from a <c>() =&gt; new T(...)</c> expression.</summary>
        /// <typeparam name="T">The child block type.</typeparam>
        /// <param name="newExpression">A <c>() =&gt; new T(...)</c> expression describing the child.</param>
        /// <param name="name">An explicit child name, or <see langword="null"/> to auto-generate one.</param>
        /// <returns>A reference to the new child block.</returns>
        IDataBlock ChildOf<T>(Expression<Func<T>> newExpression, string? name = null)
            where T : IDataBlock;

        /// <summary>Detaches a child from this container's supervision set.</summary>
        /// <param name="child">The child to remove.</param>
        void RemoveChild(IDataBlock child);
    }
}
