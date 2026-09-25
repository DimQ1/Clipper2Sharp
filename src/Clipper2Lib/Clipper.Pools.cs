/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Purpose   :  Pools of reusable engine objects (memory optimisation)          *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* The C++ engine allocates its vertices, out-points and out-recs in large      *
* contiguous blocks. C# cannot do that for reference types, so instead the     *
* objects are pooled in growable blocks and reused by every successive         *
* clipping operation performed by the same ClipperBase instance.               *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  /// <summary>
  /// A growable block of reusable reference objects. Items are handed out
  /// sequentially and are never handed out twice before the pool is <see cref="Clear"/>ed,
  /// which means a dead item stays dead for the duration of one operation.
  /// </summary>
  internal class PooledList<T> where T : class
  {
    // nb: the block is allocated on first use. A ClipperBase instance owns three
    // of these pools (~0.5 KB of block each) and many operations only ever touch
    // some of them, so paying for all three up front is wasted allocation.
    protected T?[]? _items;
    protected int _size;
    private readonly int _initialCapacity;

    public PooledList(int capacity = 64)
    {
      _initialCapacity = capacity < 4 ? 4 : capacity;
    }

    public int Count => _size;
    public int Capacity => _items?.Length ?? 0;
    public T? this[int index] => _items![index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void EnsureCapacity()
    {
      T?[]? items = _items;
      if (items == null)
        _items = new T?[_initialCapacity];
      else if (_size == items.Length)
        Array.Resize(ref _items, items.Length * 2);
    }

    /// <summary>
    /// Makes room for at least <paramref name="required"/> items in total. Used
    /// when the size of the batch is known up front, so the block is grown once
    /// instead of by repeated doubling (which leaves each intermediate block as
    /// garbage).
    /// </summary>
    public void EnsureCapacity(int required)
    {
      T?[]? items = _items;
      if (items == null)
      {
        if (required > 0) _items = new T?[Math.Max(required, _initialCapacity)];
      }
      else if (required > items.Length)
      {
        int newLen = items.Length * 2;
        if (newLen < required) newLen = required;
        Array.Resize(ref _items, newLen);
      }
    }

    /// <summary>Releases the objects for reuse (they are not given back to the GC).</summary>
    public virtual void Clear()
    {
      _size = 0;
    }

    /// <summary>Drops all references so the pooled objects can be collected.</summary>
    public virtual void Release()
    {
      if (_items != null) Array.Clear(_items, 0, _items.Length);
      _size = 0;
    }
  }

  /// <summary>
  /// The input vertices of an engine (or of a ReuseableDataContainer64): one
  /// growable array of Vertex structs, addressed by index.
  /// </summary>
  internal sealed class VertexStore
  {
    public Vertex[] items = Array.Empty<Vertex>();
    public int count;

    public int Capacity => items.Length;

    public void EnsureCapacity(int required)
    {
      if (required <= items.Length) return;
      int newLen = Math.Max(Math.Max(required, items.Length * 2), 64);
      Array.Resize(ref items, newLen);
    }

    /// <summary>Appends a vertex (the capacity must have been ensured).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(Point64 pt, int prev)
    {
      int i = count++;
      ref Vertex v = ref items[i];
      v.pt = pt;
      v.flags = VertexFlags.Empty;
      v.prev = prev;
      v.next = -1;
      return i;
    }

    /// <summary>Forgets the vertices (keeps the storage for the next paths).</summary>
    public void Clear() { count = 0; }

    /// <summary>Forgets the vertices and drops the storage.</summary>
    public void Release()
    {
      items = Array.Empty<Vertex>();
      count = 0;
    }
  }

  /// <summary>A pool of reusable OutRec objects.</summary>
  internal sealed class OutRecPoolList : PooledList<OutRec>
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OutRec Add()
    {
      EnsureCapacity();
      OutRec?[] items = _items!;
      OutRec? or = items[_size];
      if (or == null)
      {
        or = new OutRec();
        items[_size] = or;
      }
      else
      {
        // nb: 'path' is deliberately NOT reused - BuildPaths/BuildTree hand the
        // very same Path64 instance back to the caller, so a fresh one is needed
        // for every use (this is what keeps previously returned results intact).
        or.idx = 0;
        or.owner = null;
        or.frontEdge = -1;
        or.backEdge = -1;
        or.pts = -1;
        or.polypath = null;
        or.splits = null;
        or.recursiveSplit = null;
        or.bounds = new Rect64();
        or.path = null;
        or.isOpen = false;
      }
      _size++;
      return or;
    }

    public override void Clear()
    {
      // keep the objects for reuse but release their references so that
      // large object graphs (paths, edges) are not kept alive between operations
      OutRec?[]? items = _items;
      for (int i = 0; items != null && i < _size; i++)
      {
        OutRec? or = items[i];
        if (or == null) break;
        or.owner = null;
        or.frontEdge = -1;
        or.backEdge = -1;
        or.pts = -1;
        or.polypath = null;
        or.splits = null;
        or.recursiveSplit = null;
        or.path = null;
      }
      _size = 0;
    }
  }
}
