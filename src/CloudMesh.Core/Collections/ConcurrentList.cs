using System.Collections;

namespace CloudMesh.Collections;

/// <summary>
/// A thread-safe <see cref="IList{T}"/> backed by a <see cref="System.Threading.ReaderWriterLockSlim"/>: reads run
/// concurrently while writes take an exclusive lock. Enumerating holds a read lock for the lifetime of the
/// enumerator, so dispose enumerators promptly (a <c>foreach</c> does this automatically).
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// Because the lock uses <see cref="System.Threading.LockRecursionPolicy.NoRecursion"/>, do not mutate the list
/// from within an active enumeration, and dispose the list when done to release the lock.
/// </remarks>
public class ConcurrentList<T> : IList<T>, IDisposable
{
    private readonly List<T> _list;
    private readonly ReaderWriterLockSlim _lock;

    /// <summary>Creates an empty concurrent list.</summary>
    public ConcurrentList()
    {
        this._lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        this._list = new List<T>();
    }

    /// <summary>Creates an empty concurrent list with the given initial capacity.</summary>
    /// <param name="capacity">The initial capacity of the backing list.</param>
    public ConcurrentList(int capacity)
    {
        this._lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        this._list = new List<T>(capacity);
    }

    /// <summary>Creates a concurrent list pre-populated with the given items.</summary>
    /// <param name="items">The items to copy into the list.</param>
    public ConcurrentList(IEnumerable<T> items)
    {
        this._lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        this._list = new List<T>(items);
    }

    #region Methods

    public void Add(T item)
    {
        try
        {
            this._lock.EnterWriteLock();
            this._list.Add(item);
        }
        finally
        {
            this._lock.ExitWriteLock();
        }
    }

    public void Insert(int index, T item)
    {
        try
        {
            this._lock.EnterWriteLock();
            this._list.Insert(index, item);
        }
        finally
        {
            this._lock.ExitWriteLock();
        }
    }

    public bool Remove(T item)
    {
        try
        {
            this._lock.EnterWriteLock();
            return this._list.Remove(item);
        }
        finally
        {
            this._lock.ExitWriteLock();
        }
    }

    public void RemoveAt(int index)
    {
        try
        {
            this._lock.EnterWriteLock();
            this._list.RemoveAt(index);
        }
        finally
        {
            this._lock.ExitWriteLock();
        }
    }

    /// <summary>Removes the elements at the given indexes in a single write lock, highest index first.</summary>
    /// <param name="indexes">The indexes to remove. They are removed in descending order so earlier removals do not shift later ones.</param>
    public void RemoveAt(IEnumerable<int> indexes)
    {
        try
        {
            this._lock.EnterWriteLock();
            foreach (var i in indexes.OrderByDescending(i => i))
                this._list.RemoveAt(i);
        }
        finally
        {
            this._lock.ExitWriteLock();
        }
    }

    public int IndexOf(T item)
    {
        try
        {
            this._lock.EnterReadLock();
            return this._list.IndexOf(item);
        }
        finally
        {
            this._lock.ExitReadLock();
        }
    }

    public void Clear()
    {
        try
        {
            this._lock.EnterWriteLock();
            this._list.Clear();
        }
        finally
        {
            this._lock.ExitWriteLock();
        }
    }

    public bool Contains(T item)
    {
        try
        {
            this._lock.EnterReadLock();
            return this._list.Contains(item);
        }
        finally
        {
            this._lock.ExitReadLock();
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        try
        {
            this._lock.EnterReadLock();
            this._list.CopyTo(array, arrayIndex);
        }
        finally
        {
            this._lock.ExitReadLock();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return new ConcurrentEnumerator<T>(this._list, this._lock);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new ConcurrentEnumerator<T>(this._list, this._lock);
    }

    ~ConcurrentList()
    {
        this.Dispose(false);
    }

    public void Dispose()
    {
        this.Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
            GC.SuppressFinalize(this);

        this._lock.Dispose();
    }

    #endregion

    #region Properties

    public T this[int index]
    {
        get
        {
            try
            {
                this._lock.EnterReadLock();
                return this._list[index];
            }
            finally
            {
                this._lock.ExitReadLock();
            }
        }
        set
        {
            try
            {
                this._lock.EnterWriteLock();
                this._list[index] = value;
            }
            finally
            {
                this._lock.ExitWriteLock();
            }
        }
    }

    public int Count
    {
        get
        {
            try
            {
                this._lock.EnterReadLock();
                return this._list.Count;
            }
            finally
            {
                this._lock.ExitReadLock();
            }
        }
    }

    public bool IsReadOnly => false;

    #endregion
}

/// <summary>
/// Enumerator returned by <see cref="ConcurrentList{T}"/> that holds a read lock for its lifetime. Disposing it
/// (which a <c>foreach</c> does automatically) releases the read lock.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class ConcurrentEnumerator<T> : IEnumerator<T>
{
    #region Fields

    private readonly IEnumerator<T> _inner;
    private readonly ReaderWriterLockSlim _lock;

    #endregion

    #region Constructor

    /// <summary>Creates an enumerator over <paramref name="inner"/>, entering the supplied read lock immediately.</summary>
    /// <param name="inner">The sequence to enumerate.</param>
    /// <param name="lock">The reader-writer lock to hold (in read mode) while enumerating.</param>
    public ConcurrentEnumerator(IEnumerable<T> inner, ReaderWriterLockSlim @lock)
    {
        this._lock = @lock;
        this._lock.EnterReadLock();
        this._inner = inner.GetEnumerator();
    }

    #endregion

    #region Methods

    public bool MoveNext()
    {
        return _inner.MoveNext();
    }

    public void Reset()
    {
        _inner.Reset();
    }

    public void Dispose()
    {
        this._lock.ExitReadLock();
    }

    #endregion

    #region Properties

    public T Current
    {
        get { return _inner.Current; }
    }

    object? IEnumerator.Current => _inner.Current;

    #endregion
}