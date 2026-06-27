using System.Runtime.CompilerServices;

namespace CloudMesh.Variant;

public readonly partial struct Value
{
    private Value(TypeFlag flag, in Union union)
    {
        _object = flag;
        _union = union;
    }
    
    internal sealed class StraightCastFlag<T> : TypeFlag<T>
    {
        public static StraightCastFlag<T> Instance { get; } = new();

        internal override bool IsStraightCastFlag => true;

        public override T To(in Value value)
            => Unsafe.As<Union, T>(ref Unsafe.AsRef(in value._union));
        
        internal override TTarget ToNullable<TTarget>(in Value value)
        {
            // T is the stored struct; TTarget is Nullable<T>. Build the nullable via a layout-compatible
            // template and reinterpret — no boxing.
            var inner = Unsafe.As<Union, T>(ref Unsafe.AsRef(in value._union));
            var template = new NullableTemplate<T>(inner);
            return Unsafe.As<NullableTemplate<T>, TTarget>(ref template);
        }
    }
}
