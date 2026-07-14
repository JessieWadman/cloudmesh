using System.Diagnostics.Contracts;

namespace CloudMesh.Collections;

/// <summary>
/// A binary-heap priority queue. Objects <see cref="Push"/>ed in arbitrary order are read back in priority order
/// (least to greatest as defined by the supplied <see cref="IComparer{T}"/>) via <see cref="Top"/> and
/// <see cref="Pop"/>.
/// </summary>
/// <typeparam name="T">The type of item stored in the queue.</typeparam>
/// <remarks>
/// <see cref="Push"/> and <see cref="Pop"/> are each O(log N); pushing N objects and popping them all is a heap
/// sort at O(N log N). The item with the minimum value (per the comparer) is always at <see cref="Top"/>.
/// Originally by Niklas Borson (2005); adapted from the .NET reference source.
/// </remarks>
/// <example>
/// <code>
/// var pq = new PriorityQueue&lt;int&gt;(capacity: 16, Comparer&lt;int&gt;.Default);
/// pq.Push(5); pq.Push(1); pq.Push(3);
/// while (pq.Count &gt; 0) { Console.WriteLine(pq.Top); pq.Pop(); }  // 1, 3, 5
/// </code>
/// </example>
public class PriorityQueue<T>
{
    // The _heap array represents a binary tree with the "shape" property.
    // If we number the nodes of a binary tree from left-to-right and top-
    // to-bottom as shown,
    //
    //             0
    //           /   \
    //          /     \
    //         1       2
    //       /  \     / \
    //      3    4   5   6
    //     /\    /
    //    7  8  9
    //
    // The shape property means that there are no gaps in the sequence of
    // numbered nodes, i.e., for all N > 0, if node N exists then node N-1
    // also exists. For example, the next node added to the above tree would
    // be node 10, the right child of node 4.
    //
    // Because of this constraint, we can easily represent the "tree" as an
    // array, where node number == array index, and parent/child relationships
    // can be calculated instead of maintained explicitly. For example, for
    // any node N > 0, the parent of N is at array index (N - 1) / 2.
    //
    // In addition to the above, the first _count members of the _heap array
    // compose a "heap", meaning each child node is greater than or equal to
    // its parent node; thus, the root node is always the minimum (i.e., the
    // best match for the specified style, weight, and stretch) of the nodes
    // in the heap.
    //
    // Initially _count < 0, which means we have not yet constructed the heap.
    // On the first call to MoveNext, we construct the heap by "pushing" all
    // the nodes into it. Each successive call "pops" a node off the heap
    // until the heap is empty (_count == 0), at which time we've reached the
    // end of the sequence.

    /// <summary>
    /// Initializes a new instance of the <see cref="PriorityQueue{T}"/> class.
    /// </summary>
    /// <param name="capacity">The initial capacity of the backing heap. Values ≤ 0 fall back to a small default.</param>
    /// <param name="comparer">The comparer that defines priority order; the smallest item (per this comparer) is popped first.</param>
    /// <remarks>
    /// This code copied and adapted from https://referencesource.microsoft.com/#PresentationCore/Shared/MS/Internal/PriorityQueue.cs
    /// Includes fix for Pop() from https://stackoverflow.com/questions/44221454/bug-in-microsofts-internal-priorityqueuet
    /// </remarks>
    public PriorityQueue(int capacity, IComparer<T> comparer)
    {
        _heap = new T[capacity > 0 ? capacity : DefaultCapacity];
        _count = 0;
        _comparer = comparer;
    }

    /// <summary>
    /// Gets the number of items in the priority queue.
    /// </summary>
    public int Count => _count;
    
    /// <summary>
    /// Gets the first or topmost object in the priority queue, which is the
    /// object with the minimum value.
    /// </summary>
    public T Top
    {
        get
        {
            Contract.Assert(_count > 0);
            return _heap[0];
        }
    }

    /// <summary>
    /// Adds an object to the priority queue. O(log N).
    /// </summary>
    /// <param name="value">The item to add.</param>
    public void Push(T value)
    {
        // Increase the size of the array if necessary.
        if (_count == _heap.Length)
        {
            var temp = new T[_count * 2];
            for (var i = 0; i < _count; ++i)
            {
                temp[i] = _heap[i];
            }

            _heap = temp;
        }

        // Loop invariant:
        //
        //  1.  index is a gap where we might insert the new node; initially
        //      it's the end of the array (bottom-right of the logical tree).
        var index = _count;
        while (index > 0)
        {
            var parentIndex = HeapParent(index);
            if (_comparer.Compare(value, _heap[parentIndex]) < 0)
            {
                // value is a better match than the parent node so exchange
                // places to preserve the "heap" property.
                _heap[index] = _heap[parentIndex];
                index = parentIndex;
            }
            else
            {
                // we can insert here.
                break;
            }
        }

        _heap[index] = value;
        _count++;
    }

    /// <summary>
    /// Removes the first node (i.e., the logical root) from the heap.
    /// </summary>
    public void Pop()
    {
        Contract.Assert(_count != 0);

        if (_count > 1)
        {
            // Loop invariants:
            //
            //  1.  parent is the index of a gap in the logical tree
            //  2.  leftChild is
            //      (a) the index of parent's left child if it has one, or
            //      (b) a value >= _count if parent is a leaf node
            //
            var parent = 0;
            var leftChild = HeapLeftChild(parent);

            while (leftChild < _count)
            {
                var rightChild = HeapRightFromLeft(leftChild);
                var bestChild =
                    (rightChild < _count && _comparer.Compare(_heap[rightChild], _heap[leftChild]) < 0)
                        ? rightChild
                        : leftChild;

                // Promote bestChild to fill the gap left by parent.
                _heap[parent] = _heap[bestChild];

                // Restore invariants, i.e., let parent point to the gap.
                parent = bestChild;
                leftChild = HeapLeftChild(parent);
            }

            // Fill the last gap by moving the last (i.e., bottom-rightmost) node.
            _heap[parent] = _heap[_count - 1];

            // FIX: Rebalance the heap
            var index = parent;
            var value = _heap[parent];

            while (index > 0)
            {
                var parentIndex = HeapParent(index);
                if (_comparer.Compare(value, _heap[parentIndex]) < 0)
                {
                    // value is a better match than the parent node so exchange
                    // places to preserve the "heap" property.
                    (_heap[index], _heap[parentIndex]) = (_heap[parentIndex], _heap[index]);
                    index = parentIndex;
                }
                else
                {
                    // Heap is balanced
                    break;
                }
            }
        }

        _count--;
    }

    /// <summary>
    /// Calculate the parent node index given a child node's index, taking advantage
    /// of the "shape" property.
    /// </summary>
    private static int HeapParent(int i)
    {
        return (i - 1) / 2;
    }

    /// <summary>
    /// Calculate the left child's index given the parent's index, taking advantage of
    /// the "shape" property. If there is no left child, the return value is >= _count.
    /// </summary>
    private static int HeapLeftChild(int i)
    {
        return (i * 2) + 1;
    }

    /// <summary>
    /// Calculate the right child's index from the left child's index, taking advantage
    /// of the "shape" property (i.e., sibling nodes are always adjacent). If there is
    /// no right child, the return value >= _count.
    /// </summary>
    private static int HeapRightFromLeft(int i)
    {
        return i + 1;
    }

    private const int DefaultCapacity = 6;
    private T[] _heap;
    private int _count;
    private readonly IComparer<T> _comparer;
}