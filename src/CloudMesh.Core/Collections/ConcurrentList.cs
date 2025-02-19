using System.Collections;

namespace CloudMesh.Collections;

public class ConcurrentList<T> : IList<T>, IDisposable
{
    private readonly List<T> _list;
    private readonly ReaderWriterLockSlim _lock;

    public ConcurrentList()
    {
        this._lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        this._list = new List<T>();
    }

    public ConcurrentList(int capacity)
    {
        this._lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        this._list = new List<T>(capacity);
    }

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

public class ConcurrentEnumerator<T> : IEnumerator<T>
{
    #region Fields

    private readonly IEnumerator<T> _inner;
    private readonly ReaderWriterLockSlim _lock;

    #endregion

    #region Constructor

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