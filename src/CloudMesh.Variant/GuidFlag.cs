using System.Runtime.CompilerServices;

namespace CloudMesh.Variant;

public readonly partial struct Value
{
    internal sealed class GuidFlag : TypeFlag<Guid>
    {
        public static GuidFlag Instance { get; } = new();

        public override Guid To(in Value value)
        {
            return Unsafe.As<Union, Guid>(ref Unsafe.AsRef(in value._union));
        }
    }
}