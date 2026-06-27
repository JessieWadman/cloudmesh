using System.Runtime.InteropServices;

namespace CloudMesh.Variant;

public readonly partial struct Value
{
    // Mirrors the runtime layout of Nullable<T> ({ bool hasValue; T value; }, sequential) so a value
    // can be reinterpreted into a Nullable<T> without boxing. Unconstrained so it can also be used for
    // arbitrary inline structs (e.g. Guid), not just the unmanaged enum-backing types.
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NullableTemplate<T>
    {
        public readonly bool _hasValue;
        public readonly T _value;

        public NullableTemplate(T value)
        {
            _value = value;
            _hasValue = true;
        }
    }
}