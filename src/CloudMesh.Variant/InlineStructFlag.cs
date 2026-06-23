using System.Runtime.CompilerServices;

namespace CloudMesh.Variant;

public readonly partial struct Value
{
    private Value(TypeFlag flag, in Union union)
    {
        _object = flag;
        _union = union;
    }
    
    internal sealed class InlineStructFlag<T> : TypeFlag<T>
    {
        public static InlineStructFlag<T> Instance { get; } = new();

        public override T To(in Value value)
        {
            return Unsafe.As<Union, T>(ref Unsafe.AsRef(in value._union));
        }
    }
}