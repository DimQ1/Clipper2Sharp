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

  /// <summary>A pool of reusable Vertex objects.</summary>
  internal sealed class VertexPoolList : PooledList<Vertex>
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vertex Add(Point64 pt, VertexFlags flags, Vertex? prev)
    {
      EnsureCapacity();
      Vertex?[] items = _items!;
      Vertex? v = items[_size];
      if (v == null)
      {
        v = new Vertex();
        items[_size] = v;
      }
      v.pt = pt;
      v.flags = flags;
      v.prev = prev;
      v.next = null;
      _size++;
      return v;
    }
  }

  /// <summary>A pool of reusable OutPt objects.</summary>
  internal sealed class OutPtPoolList : PooledList<OutPt>
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OutPt Add(Point64 pt, OutRec outrec)
    {
      EnsureCapacity();
      OutPt?[] items = _items!;
      OutPt? op = items[_size];
      if (op == null)
      {
        op = new OutPt(pt, outrec);
        items[_size] = op;
      }
      else
      {
        op.pt = pt;
        op.outrec = outrec;
        op.next = op;
        op.prev = op;
        op.horz = null;
      }
      _size++;
      return op;
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
        or.frontEdge = null;
        or.backEdge = null;
        or.pts = null;
        or.polypath = null;
        or.splits = null;
        or.recursiveSplit = null;
        or.bounds = new Rect64();
        or.path = new Path64();
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
        or.frontEdge = null;
        or.backEdge = null;
        or.pts = null;
        or.polypath = null;
        or.splits = null;
        or.recursiveSplit = null;
        or.path = EmptyPath;
      }
      _size = 0;
    }

    private static readonly Path64 EmptyPath = new Path64(0);
  }
}
