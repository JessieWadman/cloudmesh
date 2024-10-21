namespace CloudMesh.Internal;

internal static class UuidConstants
{
    public const byte Variant10xxMask = 0xC0;
    public const byte Variant10xxValue = 0x80;

    public const ushort VersionMask = 0xF000;
    // public const ushort Version4Value = 0x4000;
    public const ushort Version7Value = 0x7000;
}