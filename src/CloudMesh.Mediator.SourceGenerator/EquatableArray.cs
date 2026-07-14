using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CloudMesh.Mediator.SourceGenerator;

/// <summary>
/// A value-equatable wrapper around an immutable array. Incremental-generator pipeline models must be
/// value-equatable so the driver can cache them; a bare array uses reference equality and defeats caching.
/// Equality here is structural (element-by-element).
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public int Count => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault && other._array.IsDefault)
            return true;
        if (_array.IsDefault || other._array.IsDefault)
            return false;
        if (_array.Length != other._array.Length)
            return false;
        for (var i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefault)
            return 0;
        unchecked
        {
            var hash = 17;
            foreach (var item in _array)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() =>
        (_array.IsDefault ? ImmutableArray<T>.Empty : _array).GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();
}
