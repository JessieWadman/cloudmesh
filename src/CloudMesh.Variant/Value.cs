using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("CloudMesh.Variant.Tests")]

namespace CloudMesh.Variant;

/// <summary>
/// A boxing-free discriminated union that can carry any common value type (or any reference) in a single,
/// fixed-size 24-byte struct (an object reference plus a 16-byte inline union).
/// </summary>
/// <remarks>
/// <para>
/// When you need to gather arbitrary values into an array or pass them around as a common type, the usual
/// approach is an <c>object?[]</c> — but storing a value type as <see cref="object"/> <b>boxes</b> it
/// (a heap allocation per value). In hot paths this can mean millions of allocations and heavy GC pressure:
/// </para>
/// <code>
/// object?[] values = new object?[3];
/// values[0] = 4;     // int    → boxed (heap allocation)
/// values[1] = null;  // null   → no boxing
/// values[2] = true;  // bool   → boxed (heap allocation)
/// </code>
/// <para>
/// <see cref="Value"/> stores primitives, enums, <see cref="Guid"/>, <see cref="DateTime"/>,
/// <see cref="DateTimeOffset"/>, <see cref="ArraySegment{T}"/> of <see cref="byte"/>/<see cref="char"/>, and
/// small unmanaged <see langword="struct"/>s <b>inline</b> in an overlapped field, so none of them box:
/// </para>
/// <code>
/// Value[] values = new Value[3];
/// values[0] = 4;     // int   → no boxing
/// values[1] = null;  // null  → no boxing
/// values[2] = true;  // bool  → no boxing
///
/// int i = values[0].As&lt;int&gt;();      // round-trips back out without boxing
/// bool b = (bool)values[2];         // explicit conversion operators also work
/// </code>
/// <para>
/// Read a value back with <see cref="As{T}"/> (throws on a type mismatch) or <see cref="TryGetValue{T}"/>
/// (returns <see langword="false"/>). Both honour <see cref="Nullable{T}"/>: a stored <c>int</c> can be read
/// as <c>int?</c>, and a stored <see langword="null"/> reads back as any nullable or reference type. Reference
/// types and large/managed structs are still held as a plain object reference (no extra allocation beyond the
/// object itself). Use <see cref="Type"/> and <see cref="IsNull"/> to inspect what is stored.
/// </para>
/// <para>
/// The layout is <see cref="LayoutKind.Explicit"/>: an <see cref="object"/> reference (either the payload
/// itself, or an internal type-flag marker identifying the inline value's type) overlaps with an inline
/// <c>Union</c> holding the bits of value types.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
public readonly partial struct Value
{
    [FieldOffset(0)]
    internal readonly object? _object;

    [FieldOffset(8)]
    internal readonly Union _union;

    /// <summary>
    /// Wraps a reference (or a boxed value) directly, storing it as-is. Prefer the type-specific constructors
    /// or <see cref="Create{T}"/> for value types, so they are stored inline rather than boxed.
    /// </summary>
    /// <param name="value">The object to store, or <see langword="null"/>.</param>
    public Value(object? value)
    {
        _object = value;
        _union = default;
    }

    /// <summary>
    /// Gets the runtime <see cref="System.Type"/> of the stored value, or <see langword="null"/> if the value
    /// is <see langword="null"/>. Inline value types report their real type (e.g. <c>typeof(int)</c>), not the
    /// internal storage type.
    /// </summary>
    public readonly Type? Type
    {
        [SkipLocalsInit]
        get
        {
            Type? type;
            if (_object is null)
            {
                type = null;
            }
            else if (_object is TypeFlag typeFlag)
            {
                type = typeFlag.Type;
            }
            else
            {
                type = _object.GetType();

                if (_union.UInt64 != 0 && type.IsArray)
                {
                    // We have an ArraySegment
                    if (type == typeof(byte[]))
                    {
                        type = typeof(ArraySegment<byte>);
                    }
                    else if (type == typeof(char[]))
                    {
                        type = typeof(ArraySegment<char>);
                    }
                    else
                    {
                        ThrowInvalidOperation();
                    }
                }
            }

            return type;
        }
    }
    
    /// <summary>Gets whether this <see cref="Value"/> stores <see langword="null"/>.</summary>
    public bool IsNull => _object is null;

    [DoesNotReturn]
    private static void ThrowInvalidCast() => throw new InvalidCastException();

    [DoesNotReturn]
    private static void ThrowArgumentNull(string paramName) => throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    private static void ThrowInvalidOperation() => throw new InvalidOperationException();

    #region Byte
    public Value(byte value)
    {
        this = default;
        _object = TypeFlags.Byte;
        _union.Byte = value;
    }

    public Value(byte? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Byte;
            _union.Byte = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(byte value) => new(value);
    public static explicit operator byte(in Value value) => value.As<byte>();
    public static implicit operator Value(byte? value) => new(value);
    public static explicit operator byte?(in Value value) => value.As<byte?>();
    #endregion

    #region SByte
    public Value(sbyte value)
    {
        this = default;
        _object = TypeFlags.SByte;
        _union.SByte = value;
    }

    public Value(sbyte? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.SByte;
            _union.SByte = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(sbyte value) => new(value);
    public static explicit operator sbyte(in Value value) => value.As<sbyte>();
    public static implicit operator Value(sbyte? value) => new(value);
    public static explicit operator sbyte?(in Value value) => value.As<sbyte?>();
    #endregion

    #region Boolean
    public Value(bool value)
    {
        this = default;
        _object = TypeFlags.Boolean;
        _union.Boolean = value;
    }

    public Value(bool? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Boolean;
            _union.Boolean = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(bool value) => new(value);
    public static explicit operator bool(in Value value) => value.As<bool>();
    public static implicit operator Value(bool? value) => new(value);
    public static explicit operator bool?(in Value value) => value.As<bool?>();
    #endregion

    #region Char
    public Value(char value)
    {
        this = default;
        _object = TypeFlags.Char;
        _union.Char = value;
    }

    public Value(char? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Char;
            _union.Char = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(char value) => new(value);
    public static explicit operator char(in Value value) => value.As<char>();
    public static implicit operator Value(char? value) => new(value);
    public static explicit operator char?(in Value value) => value.As<char?>();
    #endregion

    #region Int16
    public Value(short value)
    {
        this = default;
        _object = TypeFlags.Int16;
        _union.Int16 = value;
    }

    public Value(short? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Int16;
            _union.Int16 = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(short value) => new(value);
    public static explicit operator short(in Value value) => value.As<short>();
    public static implicit operator Value(short? value) => new(value);
    public static explicit operator short?(in Value value) => value.As<short?>();
    #endregion

    #region Int32
    public Value(int value)
    {
        this = default;
        _object = TypeFlags.Int32;
        _union.Int32 = value;
    }

    public Value(int? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Int32;
            _union.Int32 = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(int value) => new(value);
    public static explicit operator int(in Value value) => value.As<int>();
    public static implicit operator Value(int? value) => new(value);
    public static explicit operator int?(in Value value) => value.As<int?>();
    #endregion

    #region Int64
    public Value(long value)
    {
        this = default;
        _object = TypeFlags.Int64;
        _union.Int64 = value;
    }

    public Value(long? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Int64;
            _union.Int64 = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(long value) => new(value);
    public static explicit operator long(in Value value) => value.As<long>();
    public static implicit operator Value(long? value) => new(value);
    public static explicit operator long?(in Value value) => value.As<long?>();
    #endregion

    #region UInt16
    public Value(ushort value)
    {
        this = default;
        _object = TypeFlags.UInt16;
        _union.UInt16 = value;
    }

    public Value(ushort? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.UInt16;
            _union.UInt16 = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(ushort value) => new(value);
    public static explicit operator ushort(in Value value) => value.As<ushort>();
    public static implicit operator Value(ushort? value) => new(value);
    public static explicit operator ushort?(in Value value) => value.As<ushort?>();
    #endregion

    #region UInt32
    public Value(uint value)
    {
        this = default;
        _object = TypeFlags.UInt32;
        _union.UInt32 = value;
    }

    public Value(uint? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.UInt32;
            _union.UInt32 = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(uint value) => new(value);
    public static explicit operator uint(in Value value) => value.As<uint>();
    public static implicit operator Value(uint? value) => new(value);
    public static explicit operator uint?(in Value value) => value.As<uint?>();
    #endregion

    #region UInt64
    public Value(ulong value)
    {
        this = default;
        _object = TypeFlags.UInt64;
        _union.UInt64 = value;
    }

    public Value(ulong? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.UInt64;
            _union.UInt64 = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(ulong value) => new(value);
    public static explicit operator ulong(in Value value) => value.As<ulong>();
    public static implicit operator Value(ulong? value) => new(value);
    public static explicit operator ulong?(in Value value) => value.As<ulong?>();
    #endregion

    #region Single
    public Value(float value)
    {
        this = default;
        _object = TypeFlags.Single;
        _union.Single = value;
    }

    public Value(float? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Single;
            _union.Single = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(float value) => new(value);
    public static explicit operator float(in Value value) => value.As<float>();
    public static implicit operator Value(float? value) => new(value);
    public static explicit operator float?(in Value value) => value.As<float?>();
    #endregion

    #region Double
    public Value(double value)
    {
        this = default;
        _object = TypeFlags.Double;
        _union.Double = value;
    }

    public Value(double? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.Double;
            _union.Double = value.Value;
        }
        else
        {
            _object = null;
        }
    }

    public static implicit operator Value(double value) => new(value);
    public static explicit operator double(in Value value) => value.As<double>();
    public static implicit operator Value(double? value) => new(value);
    public static explicit operator double?(in Value value) => value.As<double?>();
    #endregion
    
    #region Guid

    public static implicit operator Value(Guid value) => Value.Create(value);
    public static implicit operator Value(Guid? value) => Value.Create(value);
    public static explicit operator Guid?(in Value value) => value.As<Guid?>();
    public static explicit operator Guid(in Value value) => value.As<Guid>();
    
    #endregion

    #region DateTimeOffset
    public Value(DateTimeOffset value)
    {
        this = default;
        var offset = value.Offset;
        if (offset.Ticks == 0)
        {
            // This is a UTC time
            _union.Ticks = value.Ticks;
            _object = TypeFlags.DateTimeOffset;
        }
        else if (PackedDateTimeOffset.TryCreate(value, offset, out var packed))
        {
            _union.PackedDateTimeOffset = packed;
            _object = TypeFlags.PackedDateTimeOffset;
        }
        else
        {
            _object = value;
        }
    }

    public Value(DateTimeOffset? value)
    {
        this = default;
        if (!value.HasValue)
        {
            _object = null;
        }
        else
        {
            this = new(value.Value);
        }
    }

    public static implicit operator Value(DateTimeOffset value) => new(value);
    public static explicit operator DateTimeOffset(in Value value) => value.As<DateTimeOffset>();
    public static implicit operator Value(DateTimeOffset? value) => new(value);
    public static explicit operator DateTimeOffset?(in Value value) => value.As<DateTimeOffset?>();
    #endregion

    #region DateTime
    public Value(DateTime value)
    {
        this = default;

        _union.DateTime = value;
        _object = TypeFlags.DateTime;
    }

    public Value(DateTime? value)
    {
        this = default;
        if (value.HasValue)
        {
            _object = TypeFlags.DateTime;
            _union.DateTime = value.Value;
        }
        else
        {
            _object = value;
        }
    }

    public static implicit operator Value(DateTime value) => new(value);
    public static explicit operator DateTime(in Value value) => value.As<DateTime>();
    public static implicit operator Value(DateTime? value) => new(value);
    public static explicit operator DateTime?(in Value value) => value.As<DateTime?>();
    #endregion

    #region ArraySegment
    public Value(ArraySegment<byte> segment)
    {
        this = default;
        var array = segment.Array;
        if (array is null)
        {
            ThrowArgumentNull(nameof(segment));
        }

        _object = array;
        if (segment is { Offset: 0, Count: 0 })
        {
            _union.UInt64 = ulong.MaxValue;
        }
        else
        {
            _union.Segment = (segment.Offset, segment.Count);
        }
    }

    public static implicit operator Value(ArraySegment<byte> value) => new(value);
    public static explicit operator ArraySegment<byte>(in Value value) => value.As<ArraySegment<byte>>();

    public Value(ArraySegment<char> segment)
    {
        this = default;
        var array = segment.Array;
        if (array is null)
        {
            ThrowArgumentNull(nameof(segment));
        }

        _object = array;
        if (segment is { Offset: 0, Count: 0 })
        {
            _union.UInt64 = ulong.MaxValue;
        }
        else
        {
            _union.Segment = (segment.Offset, segment.Count);
        }
    }

    public static implicit operator Value(ArraySegment<char> value) => new(value);
    public static explicit operator ArraySegment<char>(in Value value) => value.As<ArraySegment<char>>();
    #endregion
    
    #region Array 
    public static implicit operator Value(Array value) => new(value);
    public static explicit operator Array(in Value value) => value.As<Array>();
    #endregion
    
    #region Decimal
    public static implicit operator Value(decimal value) => Value.Create(value);
    public static explicit operator decimal(in Value value) => value.As<decimal>();
    public static implicit operator Value(decimal? value) => Value.Create(value);
    public static explicit operator decimal?(in Value value) => value.As<decimal?>();
    #endregion
    
    #region T
    /// <summary>
    /// Creates a <see cref="Value"/> from a value of an arbitrary type <typeparamref name="T"/>, choosing the
    /// boxing-free inline representation whenever possible.
    /// </summary>
    /// <typeparam name="T">The type of the value being stored. May be a primitive, a nullable primitive, an
    /// enum, a small unmanaged struct, or any reference type.</typeparam>
    /// <param name="value">The value to store.</param>
    /// <returns>A <see cref="Value"/> wrapping <paramref name="value"/>.</returns>
    /// <remarks>
    /// Primitives, their <see cref="Nullable{T}"/> forms, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="ArraySegment{T}"/> of <see cref="byte"/>/<see cref="char"/>, enums, and any unmanaged struct
    /// small enough to fit inline are stored <b>without boxing</b>. Everything else is stored as an object
    /// reference. This is the generic entry point used when <typeparamref name="T"/> is only known at
    /// compile time (e.g. from a generic method); the implicit conversion operators call into it.
    /// </remarks>
    public static Value Create<T>(T value)
    {
        var valueType = typeof(T);
        // Explicit cast for types we don't box
        if (valueType == typeof(bool)) return new(Unsafe.As<T, bool>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(byte)) return new(Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(sbyte)) return new(Unsafe.As<T, sbyte>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(char)) return new(Unsafe.As<T, char>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(short)) return new(Unsafe.As<T, short>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(int)) return new(Unsafe.As<T, int>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(long)) return new(Unsafe.As<T, long>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(ushort)) return new(Unsafe.As<T, ushort>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(uint)) return new(Unsafe.As<T, uint>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(ulong)) return new(Unsafe.As<T, ulong>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(float)) return new(Unsafe.As<T, float>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(double)) return new(Unsafe.As<T, double>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(DateTime)) return new(Unsafe.As<T, DateTime>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(DateTimeOffset)) return new(Unsafe.As<T, DateTimeOffset>(ref Unsafe.AsRef(in value)));

        if (valueType == typeof(bool?)) return new(Unsafe.As<T, bool?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(byte?)) return new(Unsafe.As<T, byte?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(sbyte?)) return new(Unsafe.As<T, sbyte?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(char?)) return new(Unsafe.As<T, char?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(short?)) return new(Unsafe.As<T, short?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(int?)) return new(Unsafe.As<T, int?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(long?)) return new(Unsafe.As<T, long?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(ushort?)) return new(Unsafe.As<T, ushort?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(uint?)) return new(Unsafe.As<T, uint?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(ulong?)) return new(Unsafe.As<T, ulong?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(float?)) return new(Unsafe.As<T, float?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(double?)) return new(Unsafe.As<T, double?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(DateTime?)) return new(Unsafe.As<T, DateTime?>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(DateTimeOffset?)) return new(Unsafe.As<T, DateTimeOffset?>(ref Unsafe.AsRef(in value)));

        if (valueType == typeof(ArraySegment<byte>)) return new(Unsafe.As<T, ArraySegment<byte>>(ref Unsafe.AsRef(in value)));
        if (valueType == typeof(ArraySegment<char>)) return new(Unsafe.As<T, ArraySegment<char>>(ref Unsafe.AsRef(in value)));
        
        if (typeof(T).IsEnum)
        {
            Debug.Assert(Unsafe.SizeOf<T>() <= sizeof(ulong));
            return new Value(StraightCastFlag<T>.Instance, Unsafe.As<T, ulong>(ref value));
        }
        
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            var size = Unsafe.SizeOf<T>();

            if (size <= Unsafe.SizeOf<Union>())
            {
                Union union = default;
                Unsafe.As<Union, T>(ref union) = value;
                return new Value(StraightCastFlag<T>.Instance, in union);
            }
        }

        return new Value(value);
    }

    [SkipLocalsInit]
    private Value(object o, ulong u)
    {
        Unsafe.SkipInit(out _union);
        _object = o;
        _union.UInt64 = u;
    }

    /// <summary>
    /// Attempts to read the stored value as <typeparamref name="T"/> without boxing.
    /// </summary>
    /// <typeparam name="T">The type to read the value as. Honours <see cref="Nullable{T}"/> — a stored
    /// primitive can be read as its nullable form, and a stored <see langword="null"/> succeeds for any
    /// nullable or reference type.</typeparam>
    /// <param name="value">When this returns <see langword="true"/>, the extracted value; otherwise the
    /// default of <typeparamref name="T"/>.</param>
    /// <returns><see langword="true"/> if the stored value is compatible with <typeparamref name="T"/>;
    /// otherwise <see langword="false"/>.</returns>
    /// <example>
    /// <code>
    /// Value v = 42;
    /// v.TryGetValue&lt;int&gt;(out var i);   // true, i == 42
    /// v.TryGetValue&lt;int?&gt;(out var n);  // true, n == 42
    /// v.TryGetValue&lt;long&gt;(out _);      // false — no implicit widening
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryGetValue<T>(out T value)
    {
        bool success;
        
        // Checking the type gets all the non-relevant compares elided by the JIT
        if (_object is not null && ((typeof(T) == typeof(bool) && _object == TypeFlags.Boolean)
            || (typeof(T) == typeof(byte) && _object == TypeFlags.Byte)
            || (typeof(T) == typeof(char) && _object == TypeFlags.Char)
            || (typeof(T) == typeof(double) && _object == TypeFlags.Double)
            || (typeof(T) == typeof(short) && _object == TypeFlags.Int16)
            || (typeof(T) == typeof(int) && _object == TypeFlags.Int32)
            || (typeof(T) == typeof(long) && _object == TypeFlags.Int64)
            || (typeof(T) == typeof(sbyte) && _object == TypeFlags.SByte)
            || (typeof(T) == typeof(float) && _object == TypeFlags.Single)
            || (typeof(T) == typeof(ushort) && _object == TypeFlags.UInt16)
            || (typeof(T) == typeof(uint) && _object == TypeFlags.UInt32)
            || (typeof(T) == typeof(ulong) && _object == TypeFlags.UInt64)))
        {
            value = CastTo<T>();
            success = true;
        }
        /*
        else if (typeof(T) == typeof(Guid))
        {
            value = Unsafe.As<Guid, T>(ref Unsafe.AsRef(in _union.Guid));
            success = true;
        }
        else if (typeof(T) == typeof(Guid?))
        {
            if (_object == null)
            {
                value = default!;
                success = true;
            }
            else
            {
                Guid? temp = _union.Guid;
                value = Unsafe.As<Guid?, T>(ref Unsafe.AsRef(in temp));
                success = true;
            }
        }*/
        else if (typeof(T) == typeof(DateTime) && _object == TypeFlags.DateTime)
        {
            value = Unsafe.As<DateTime, T>(ref Unsafe.AsRef(in _union.DateTime));
            success = true;
        }
        else if (typeof(T) == typeof(DateTimeOffset) && _object == TypeFlags.DateTimeOffset)
        {
            var dateTimeOffset = new DateTimeOffset(_union.Ticks, TimeSpan.Zero);
            value = Unsafe.As<DateTimeOffset, T>(ref Unsafe.AsRef(in dateTimeOffset));
            success = true;
        }
        else if (typeof(T) == typeof(DateTimeOffset) && _object == TypeFlags.PackedDateTimeOffset)
        {
            var dateTimeOffset = _union.PackedDateTimeOffset.Extract();
            value = Unsafe.As<DateTimeOffset, T>(ref Unsafe.AsRef(in dateTimeOffset));
            success = true;
        }
        else if (typeof(T).IsValueType)
        {
            success = TryGetValueSlow(out value);
        }
        else
        {
            success = TryGetObjectSlow(out value);
        }

        return success;
    }

    private readonly bool TryGetValueSlow<T>(out T value)
    {
        // Single return has a significant performance benefit.

        bool result = false;

        if (_object is null)
        {
            // A null is stored, it can only be assigned to a reference type or nullable.
            value = default!;
            result = Nullable.GetUnderlyingType(typeof(T)) is not null;
        }
        else if (typeof(T).IsEnum && _object is TypeFlag<T> typeFlag)
        {
            value = typeFlag.To(in this);
            result = true;
        }
        else if (_object is TypeFlag<T> typeFlag2)
        {
            value = typeFlag2.To(in this);
            result = true;
        }
        else if (_object is T t)
        {
            value = t;
            result = true;
        }
        else if (typeof(T) == typeof(ArraySegment<byte>))
        {
            var bits = _union.UInt64;
            if (bits != 0 && _object is byte[] byteArray)
            {
                ArraySegment<byte> segment = bits != ulong.MaxValue
                    ? new(byteArray, _union.Segment.Offset, _union.Segment.Count)
                    : new(byteArray, 0, 0);
                value = Unsafe.As<ArraySegment<byte>, T>(ref segment);
                result = true;
            }
            else
            {
                value = default!;
            }
        }
        else if (typeof(T) == typeof(ArraySegment<char>))
        {
            var bits = _union.UInt64;
            if (bits != 0 && _object is char[] charArray)
            {
                ArraySegment<char> segment = bits != ulong.MaxValue
                    ? new(charArray, _union.Segment.Offset, _union.Segment.Count)
                    : new(charArray, 0, 0);
                value = Unsafe.As<ArraySegment<char>, T>(ref segment);
                result = true;
            }
            else
            {
                value = default!;
            }
        }
#pragma warning disable CS9193 // Argument should be a variable because it is passed to a 'ref readonly' parameter            
        else if (typeof(T) == typeof(int?) && _object == TypeFlags.Int32)
        {

            value = Unsafe.As<int?, T>(ref Unsafe.AsRef((int?)_union.Int32));
            result = true;
        }
        else if (typeof(T) == typeof(long?) && _object == TypeFlags.Int64)
        {
            value = Unsafe.As<long?, T>(ref Unsafe.AsRef((long?)_union.Int64));
            result = true;
        }
        else if (typeof(T) == typeof(bool?) && _object == TypeFlags.Boolean)
        {
            value = Unsafe.As<bool?, T>(ref Unsafe.AsRef((bool?)_union.Boolean));
            result = true;
        }
        else if (typeof(T) == typeof(float?) && _object == TypeFlags.Single)
        {
            value = Unsafe.As<float?, T>(ref Unsafe.AsRef((float?)_union.Single));
            result = true;
        }
        else if (typeof(T) == typeof(double?) && _object == TypeFlags.Double)
        {
            value = Unsafe.As<double?, T>(ref Unsafe.AsRef((double?)_union.Double));
            result = true;
        }
        else if (typeof(T) == typeof(uint?) && _object == TypeFlags.UInt32)
        {
            value = Unsafe.As<uint?, T>(ref Unsafe.AsRef((uint?)_union.UInt32));
            result = true;
        }
        else if (typeof(T) == typeof(ulong?) && _object == TypeFlags.UInt64)
        {
            value = Unsafe.As<ulong?, T>(ref Unsafe.AsRef((ulong?)_union.UInt64));
            result = true;
        }
        else if (typeof(T) == typeof(char?) && _object == TypeFlags.Char)
        {
            value = Unsafe.As<char?, T>(ref Unsafe.AsRef((char?)_union.Char));
            result = true;
        }
        else if (typeof(T) == typeof(short?) && _object == TypeFlags.Int16)
        {
            value = Unsafe.As<short?, T>(ref Unsafe.AsRef((short?)_union.Int16));
            result = true;
        }
        else if (typeof(T) == typeof(ushort?) && _object == TypeFlags.UInt16)
        {
            value = Unsafe.As<ushort?, T>(ref Unsafe.AsRef((ushort?)_union.UInt16));
            result = true;
        }
        else if (typeof(T) == typeof(byte?) && _object == TypeFlags.Byte)
        {
            value = Unsafe.As<byte?, T>(ref Unsafe.AsRef((byte?)_union.Byte));
            result = true;
        }
        else if (typeof(T) == typeof(sbyte?) && _object == TypeFlags.SByte)
        {
            value = Unsafe.As<sbyte?, T>(ref Unsafe.AsRef((sbyte?)_union.SByte));
            result = true;
        }
        else if (typeof(T) == typeof(DateTime?) && _object == TypeFlags.DateTime)
        {
            value = Unsafe.As<DateTime?, T>(ref Unsafe.AsRef((DateTime?)_union.DateTime));
            result = true;
        }
        else if (typeof(T) == typeof(DateTimeOffset?) && _object == TypeFlags.DateTimeOffset)
        {
            value = Unsafe.As<DateTimeOffset?, T>(ref Unsafe.AsRef((DateTimeOffset?)new DateTimeOffset(_union.Ticks, TimeSpan.Zero)));
            result = true;
        }
        else if (typeof(T) == typeof(DateTimeOffset?) && _object == TypeFlags.PackedDateTimeOffset)
        {
            value = Unsafe.As<DateTimeOffset?, T>(ref Unsafe.AsRef((DateTimeOffset?)_union.PackedDateTimeOffset.Extract()));
            result = true;
        }
#pragma warning restore CS9193 // Argument should be a variable because it is passed to a 'ref readonly' parameter
        else if (Nullable.GetUnderlyingType(typeof(T)) is { } underlying
                 && _object is TypeFlag { IsStraightCastFlag: true } straightCastFlag
                 && straightCastFlag.Type == underlying)
        {
            // Asked for a nullable straight-cast value (enum or inline struct — e.g. MyEnum?, Guid?,
            // MyStruct?) held inline. Rebuild the nullable without boxing.
            value = straightCastFlag.ToNullable<T>(in this);
            result = true;
        }
        else
        {
            value = default!;
            result = false;
        }

        return result;
    }

    private readonly bool TryGetObjectSlow<T>(out T value)
    {
        // Single return has a significant performance benefit.

        bool result = false;

        if (_object is null)
        {
            value = default!;
        }
        else if (typeof(T) == typeof(char[]))
        {
            if (_union.UInt64 == 0 && _object is char[])
            {
                value = (T)_object;
                result = true;
            }
            else
            {
                // Don't allow "implicit" cast to array if we stored a segment.
                value = default!;
                result = false;
            }
        }
        else if (typeof(T) == typeof(byte[]))
        {
            if (_union.UInt64 == 0 && _object is byte[])
            {
                value = (T)_object;
                result = true;
            }
            else
            {
                // Don't allow "implicit" cast to array if we stored a segment.
                value = default!;
                result = false;
            }
        }
        else if (typeof(T) == typeof(object))
        {
            // This case must also come before the _object is T case to make sure we don't leak our flags.
            if (_object is TypeFlag flag)
            {
                value = (T)flag.ToObject(this);
                result = true;
            }
            else if (_union.UInt64 != 0 && _object is char[] chars)
            {
                value = _union.UInt64 != ulong.MaxValue
                    ? (T)(object)new ArraySegment<char>(chars, _union.Segment.Offset, _union.Segment.Count)
                    : (T)(object)new ArraySegment<char>(chars, 0, 0);
                result = true;
            }
            else if (_union.UInt64 != 0 && _object is byte[] bytes)
            {
                value = _union.UInt64 != ulong.MaxValue
                    ? (T)(object)new ArraySegment<byte>(bytes, _union.Segment.Offset, _union.Segment.Count)
                    : (T)(object)new ArraySegment<byte>(bytes, 0, 0);
                result = true;
            }
            else
            {
                value = (T)_object;
                result = true;
            }
        }
        else if (_object is T t)
        {
            value = t;
            result = true;
        }
        else
        {
            value = default!;
            result = false;
        }

        return result;
    }

    /// <summary>
    /// Reads the stored value as <typeparamref name="T"/> without boxing, throwing if the stored value is not
    /// compatible.
    /// </summary>
    /// <typeparam name="T">The type to read the value as. Honours <see cref="Nullable{T}"/> exactly as
    /// <see cref="TryGetValue{T}"/> does.</typeparam>
    /// <returns>The extracted value.</returns>
    /// <exception cref="InvalidCastException">The stored value is not compatible with
    /// <typeparamref name="T"/>.</exception>
    /// <seealso cref="TryGetValue{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly T As<T>()
    {
        if (!TryGetValue(out T value))
        {
            ThrowInvalidCast();
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly T CastTo<T>()
    {
        Debug.Assert(typeof(T).IsPrimitive);
        var value = Unsafe.As<Union, T>(ref Unsafe.AsRef(in _union));
        return value;
    }
    
    #endregion
}

