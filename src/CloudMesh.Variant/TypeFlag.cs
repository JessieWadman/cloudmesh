using System.Runtime.CompilerServices;

namespace CloudMesh.Variant;

public readonly partial struct Value
{
    internal abstract class TypeFlag
    {
        public abstract Type Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }
        
        internal virtual bool IsStraightCastFlag => false;

        public abstract object ToObject(in Value value);

        /// <summary>
        /// Reconstructs the stored value as a nullable <typeparamref name="TTarget"/> (i.e.
        /// Nullable&lt;stored-type&gt;) without boxing. Only meaningful for inline structs; other
        /// flags don't reach this path.
        /// </summary>
        internal virtual TTarget ToNullable<TTarget>(in Value value)
            => throw new InvalidOperationException($"{Type} cannot be read as a nullable inline struct.");
    }

    internal abstract class TypeFlag<T> : TypeFlag
    {
        public sealed override Type Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => typeof(T);
        }

        public override object ToObject(in Value value) => To(value)!;
        public abstract T To(in Value value);
    }
}