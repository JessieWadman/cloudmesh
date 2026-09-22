#if NET11_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CloudMesh.Variant;

/// <summary>
/// Opts <see cref="Value"/> into the C# 15 / .NET 11 <see langword="union"/> feature, so it can be pattern
/// matched and assigned like a compiler-declared union while keeping its boxing-free storage.
/// </summary>
/// <remarks>
/// <para>
/// A compiler-declared <see langword="union"/> always stores its contents as a single <see cref="object"/>
/// reference, which boxes every value-type case. <see cref="Value"/> instead implements the union contract by
/// hand, using the <i>union member provider</i> form: the members the compiler binds to are declared on the
/// nested <see cref="IUnionMembers"/> interface rather than discovered on the type. That form is required here
/// because the basic union pattern wants a public property named <c>Value</c>, which a type named
/// <see cref="Value"/> cannot declare, and because it lets the <c>Create</c> members route through the
/// type-specific constructors that store inline instead of boxing.
/// </para>
/// <para>
/// The scalar case types are declared in their <see cref="Nullable{T}"/> form — <c>int?</c> rather than
/// <c>int</c> — because a <see cref="Value"/> can always hold <see langword="null"/>. The union contract
/// matches a nullable case type with a <c>TryGetValue</c> whose <see langword="out"/> parameter is the
/// underlying type, so one case type per scalar covers both <c>int</c> and <c>int?</c> sources while the
/// pattern <c>v is int i</c> still binds to <see cref="TryGetValue(out int)"/> and never boxes.
/// </para>
/// <para>
/// Because <see cref="IUnionMembers"/> declares a <c>TryGetValue</c> overload per case type and a
/// <see cref="HasValue"/> property, the compiler emits the <i>non-boxing access pattern</i>: a type pattern
/// such as <c>v is int i</c> becomes a call to <see cref="TryGetValue(out int)"/> and a null pattern becomes a
/// call to <see cref="HasValue"/>. Neither reads the boxing <c>Value</c> property, so matching a union over an
/// inline value type allocates nothing.
/// </para>
/// <code>
/// Value v = 42;
///
/// var description = v switch
/// {
///     int i =&gt; $"int: {i}",        // TryGetValue(out int)     — no boxing
///     string s =&gt; $"string: {s}",  // TryGetValue(out string)
///     null =&gt; "null",              // HasValue
///     object o =&gt; o.ToString()!,   // TryGetValue(out object)  — boxes, as any object cast must
/// };
/// </code>
/// <para>
/// This is additive: the existing constructors, conversion operators, <see cref="Create{T}(T)"/>,
/// <see cref="TryGetValue{T}"/> and <see cref="As{T}"/> are unchanged, and a user-defined conversion operator
/// takes priority over the union conversion, so assignments keep storing values inline exactly as before. On
/// target frameworks older than net11.0 the whole file compiles away.
/// </para>
/// </remarks>
[Union]
public readonly partial struct Value : IUnion, Value.IUnionMembers
{
    /// <summary>
    /// Declares the union members the C# compiler binds to when it treats <see cref="Value"/> as a union:
    /// one <c>Create</c> method per case type, the boxing <c>Value</c> accessor, and the optional
    /// <c>HasValue</c>/<c>TryGetValue</c> members that enable non-boxing pattern matching.
    /// </summary>
    /// <remarks>
    /// The parameter type of each <c>Create</c> method defines a case type of the union. Only members
    /// declared here are used by the compiler. Call the members on <see cref="Value"/> itself; this interface
    /// exists for the compiler's benefit.
    /// </remarks>
    public interface IUnionMembers
    {
        // One Create per case type. These define the union's case types and back the implicit union
        // conversion from each of them. Each routes to the constructor (or Create<T>) that stores inline.
        //
        // The scalar case types are the *nullable* forms, because a Value can always be null and
        // Value(T?) is the constructor that models that. A nullable case type is matched by a
        // TryGetValue whose out parameter is the underlying type, so `v is int i` still binds to
        // TryGetValue(out int) and never boxes. One case type per scalar therefore covers both `int`
        // and `int?` sources.
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(bool? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(byte? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(sbyte? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(char? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(short? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(ushort? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(int? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(uint? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(long? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(ulong? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(float? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(double? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(DateTime? value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(DateTimeOffset? value) => new(value);
        // Fully qualified: inside this interface the simple name 'Value' binds to the Value property below.
        // decimal? and Guid? have no dedicated constructor, so they go through the Nullable<T> overload of
        // Create, which unwraps them and stores the 16-byte payload inline.
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(decimal? value) => CloudMesh.Variant.Value.Create(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(Guid? value) => CloudMesh.Variant.Value.Create(value);
        // ArraySegment has no nullable constructor, so its case types stay non-nullable; an absent segment is
        // simply a null Value.
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(ArraySegment<byte> value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(ArraySegment<char> value) => new(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(string? value) => new((object?)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static Value Create(object? value) => new(value);

        /// <summary>Gets the stored value as an <see cref="object"/>, boxing inline value types.</summary>
        object? Value { get; }

        /// <summary>Gets whether a non-<see langword="null"/> value is stored.</summary>
        bool HasValue { get; }

        // One TryGetValue per case type. Their presence is what makes type patterns non-boxing.
        bool TryGetValue(out bool value);
        bool TryGetValue(out byte value);
        bool TryGetValue(out sbyte value);
        bool TryGetValue(out char value);
        bool TryGetValue(out short value);
        bool TryGetValue(out ushort value);
        bool TryGetValue(out int value);
        bool TryGetValue(out uint value);
        bool TryGetValue(out long value);
        bool TryGetValue(out ulong value);
        bool TryGetValue(out float value);
        bool TryGetValue(out double value);
        bool TryGetValue(out decimal value);
        bool TryGetValue(out Guid value);
        bool TryGetValue(out DateTime value);
        bool TryGetValue(out DateTimeOffset value);
        bool TryGetValue(out ArraySegment<byte> value);
        bool TryGetValue(out ArraySegment<char> value);
        bool TryGetValue([MaybeNullWhen(false)] out string value);
        bool TryGetValue([MaybeNullWhen(false)] out object value);
    }

    /// <inheritdoc cref="IUnionMembers.Value"/>
    object? IUnion.Value => BoxedValue;

    /// <inheritdoc cref="IUnionMembers.Value"/>
    object? IUnionMembers.Value => BoxedValue;

    /// <summary>
    /// Gets the stored value as an <see cref="object"/>. Inline value types are boxed here — this is the
    /// fallback the compiler only reaches for patterns that have no <c>TryGetValue</c> overload.
    /// </summary>
    private object? BoxedValue => TryGetValue<object>(out var boxed) ? boxed : null;

    /// <summary>
    /// Gets whether this <see cref="Value"/> stores a non-<see langword="null"/> value. The inverse of
    /// <see cref="IsNull"/>; the compiler calls it for the <see langword="null"/> pattern.
    /// </summary>
    public bool HasValue => _object is not null;

    /// <summary>Attempts to read the stored value as a <see cref="bool"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out bool value) => TryGetValue<bool>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="byte"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out byte value) => TryGetValue<byte>(out value);

    /// <summary>Attempts to read the stored value as an <see cref="sbyte"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out sbyte value) => TryGetValue<sbyte>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="char"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out char value) => TryGetValue<char>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="short"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out short value) => TryGetValue<short>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="ushort"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out ushort value) => TryGetValue<ushort>(out value);

    /// <summary>Attempts to read the stored value as an <see cref="int"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out int value) => TryGetValue<int>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="uint"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out uint value) => TryGetValue<uint>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="long"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out long value) => TryGetValue<long>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="ulong"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out ulong value) => TryGetValue<ulong>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="float"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out float value) => TryGetValue<float>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="double"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out double value) => TryGetValue<double>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="decimal"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out decimal value) => TryGetValue<decimal>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="Guid"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out Guid value) => TryGetValue<Guid>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="DateTime"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out DateTime value) => TryGetValue<DateTime>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="DateTimeOffset"/> without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out DateTimeOffset value) => TryGetValue<DateTimeOffset>(out value);

    /// <summary>Attempts to read the stored value as an <see cref="ArraySegment{T}"/> of bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out ArraySegment<byte> value) => TryGetValue<ArraySegment<byte>>(out value);

    /// <summary>Attempts to read the stored value as an <see cref="ArraySegment{T}"/> of chars.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue(out ArraySegment<char> value) => TryGetValue<ArraySegment<char>>(out value);

    /// <summary>Attempts to read the stored value as a <see cref="string"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue([MaybeNullWhen(false)] out string value) => TryGetValue<string>(out value!);

    /// <summary>
    /// Attempts to read the stored value as an <see cref="object"/>. Unlike the other overloads this one
    /// boxes an inline value type, because the case type is <see cref="object"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    public readonly bool TryGetValue([MaybeNullWhen(false)] out object value) => TryGetValue<object>(out value!);
}
#endif
