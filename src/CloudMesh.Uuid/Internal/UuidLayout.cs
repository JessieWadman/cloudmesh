namespace CloudMesh.Internal;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct UuidLayout
{
    [FieldOffset(0)] 
    internal Guid Guid;
    
    [FieldOffset(0)] 
    internal fixed byte Bytes[16];

    [FieldOffset(0)]
    internal readonly DotNetGuid GuidFields;
    
    // Since we're not directly implementing the Guid struct, we can't access its private members.
    // Therefore, we just copy the layout of the Guid struct and use that to access the private members.
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct DotNetGuid
    {
        public readonly int _a; // Do not rename (binary serialization)
        public readonly short _b; // Do not rename (binary serialization)
        public readonly short _c; // Do not rename (binary serialization)
        public readonly byte _d; // Do not rename (binary serialization)
        public readonly byte _e; // Do not rename (binary serialization)
        public readonly byte _f; // Do not rename (binary serialization)
        public readonly byte _g; // Do not rename (binary serialization)
        public readonly byte _h; // Do not rename (binary serialization)
        public readonly byte _i; // Do not rename (binary serialization)
        public readonly byte _j; // Do not rename (binary serialization)
        public readonly byte _k; // Do not rename (binary serialization)
    }
}