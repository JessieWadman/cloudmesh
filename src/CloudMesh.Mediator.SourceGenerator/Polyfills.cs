// Polyfills required to use records / init-only setters when targeting netstandard2.0.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>Enables <c>init</c>-only setters and positional records on netstandard2.0.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
