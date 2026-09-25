/*******************************************************************************
* Purpose   :  SIMD, span and multi-threading primitives used by the bulk      *
*              operations of the library.                                      *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* Notes on determinism: every parallel operation below writes its result into  *
* a pre-sized, index-stable buffer and the final scalar reduction is performed *
* in the original (sequential) order. Consequently multi-threaded results are  *
* bit-identical to single-threaded ones.                                       *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  /// <summary>
  /// A max-heap of scanline Y values (the C++ std::priority_queue&lt;int64_t&gt;).
  /// Hand rolled so that no comparer object and no per-item node is allocated.
  /// </summary>
  internal struct ScanlineHeap
  {
    private long[]? _items;
    private int _size;
    private readonly int _initialCapacity;

    public ScanlineHeap(int capacity)
    {
      // nb: the storage is allocated on the first push - a ClipperBase that is
      // only fed paths (and never executed) never needs it at all
      _items = null;
      _size = 0;
      _initialCapacity = capacity < 16 ? 16 : capacity;
    }

    public readonly int Count => _size;

    public void Clear() { _size = 0; }

    public void Push(long y)
    {
      long[] items = _items ??= new long[_initialCapacity];
      if (_size == items.Length)
      {
        Array.Resize(ref _items, items.Length * 2);
        items = _items;
      }
      int i = _size++;
      items[i] = y;
      while (i > 0)
      {
        int parent = (i - 1) >> 1;
        if (items[parent] >= items[i]) break;
        (items[parent], items[i]) = (items[i], items[parent]);
        i = parent;
      }
    }

    private long PopMax()
    {
      long[] items = _items!;
      long top = items[0];
      items[0] = items[--_size];
      int i = 0;
      while (true)
      {
        int l = 2 * i + 1, r = l + 1, largest = i;
        if (l < _size && items[l] > items[largest]) largest = l;
        if (r < _size && items[r] > items[largest]) largest = r;
        if (largest == i) break;
        (items[largest], items[i]) = (items[i], items[largest]);
        i = largest;
      }
      return top;
    }

    public bool Pop(out long y)
    {
      y = 0;
      if (_size == 0) return false;
      y = PopMax();
      while (_size > 0 && _items![0] == y) PopMax(); // pop duplicates
      return true;
    }
  }

  internal static class BulkOps
  {
    /// <summary>Work below this (in "elements") is cheaper to do serially.</summary>
    internal const int ParallelThreshold = 8192;

    internal static int MaxThreads => Environment.ProcessorCount > 1
      ? Math.Min(Environment.ProcessorCount, 16)
      : 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ShouldParallelize(long work)
    {
      return work >= ParallelThreshold && MaxThreads > 1;
    }

    /// <summary>
    /// Runs <paramref name="body"/> for every index in [0, count) either serially
    /// or on several threads (each thread writes to distinct indices only).
    /// </summary>
    internal static void For(int count, Action<int> body)
    {
      if (!ShouldParallelize(count))
      {
        for (int i = 0; i < count; i++) body(i);
        return;
      }
      int threads = MaxThreads;
      int chunk = (count + threads - 1) / threads;
      Parallel.For(0, threads, t =>
      {
        int start = t * chunk;
        int end = Math.Min(start + chunk, count);
        for (int i = start; i < end; i++) body(i);
      });
    }

    /// <summary>
    /// Runs <paramref name="body"/> over the given ranges either serially or in
    /// parallel. Used when the parallel unit is a slice of a single big path.
    /// </summary>
    internal static void ForRanges(int count, long totalSize, Func<int, (int start, int end)> range,
      Action<int, int, int> body)
    {
      if (!ShouldParallelize(totalSize))
      {
        for (int i = 0; i < count; i++)
        {
          (int s, int e) = range(i);
          body(i, s, e);
        }
        return;
      }
      int threads = Math.Min(MaxThreads, count);
      Parallel.For(0, threads, t =>
      {
        for (int i = t; i < count; i += threads)
        {
          (int s, int e) = range(i);
          body(i, s, e);
        }
      });
    }

    // ------------------------------------------------------------------------
    // Vectorised minima/maxima
    // ------------------------------------------------------------------------

    /// <summary>
    /// Computes the min/max of the even and of the odd elements of an
    /// interleaved coordinate buffer (x0,y0,x1,y1,...).
    /// </summary>
    internal static void MinMaxInterleaved(ReadOnlySpan<long> vals,
      out long minEven, out long maxEven, out long minOdd, out long maxOdd)
    {
      int n = vals.Length;
      int i = 0;
      int w = Vector<long>.Count;
      long mnEven = long.MaxValue, mxEven = long.MinValue;
      long mnOdd = long.MaxValue, mxOdd = long.MinValue;

      if (Vector.IsHardwareAccelerated && n >= w * 2)
      {
        Vector<long> vmin = new Vector<long>(long.MaxValue);
        Vector<long> vmax = new Vector<long>(long.MinValue);
        for (; i + w * 2 <= n; i += w * 2)
        {
          Vector<long> a = new Vector<long>(vals.Slice(i, w));
          Vector<long> b = new Vector<long>(vals.Slice(i + w, w));
          vmin = Vector.Min(vmin, Vector.Min(a, b));
          vmax = Vector.Max(vmax, Vector.Max(a, b));
        }
        for (int k = 0; k < w; k++)
        {
          if ((k & 1) == 0)
          {
            if (vmin[k] < mnEven) mnEven = vmin[k];
            if (vmax[k] > mxEven) mxEven = vmax[k];
          }
          else
          {
            if (vmin[k] < mnOdd) mnOdd = vmin[k];
            if (vmax[k] > mxOdd) mxOdd = vmax[k];
          }
        }
      }

      // nb: 'i' is always a multiple of the vector width, so the element
      // parity of the scalar tail matches the interleaved layout
      for (; i + 1 < n; i += 2)
      {
        long x = vals[i], y = vals[i + 1];
        if (x < mnEven) mnEven = x;
        if (x > mxEven) mxEven = x;
        if (y < mnOdd) mnOdd = y;
        if (y > mxOdd) mxOdd = y;
      }
      minEven = mnEven; maxEven = mxEven; minOdd = mnOdd; maxOdd = mxOdd;
    }

    internal static void MinMaxInterleaved(ReadOnlySpan<double> vals,
      out double minEven, out double maxEven, out double minOdd, out double maxOdd)
    {
      int n = vals.Length;
      int i = 0;
      int w = Vector<double>.Count;
      double mnEven = double.MaxValue, mxEven = double.MinValue;
      double mnOdd = double.MaxValue, mxOdd = double.MinValue;

      if (Vector.IsHardwareAccelerated && n >= w * 2)
      {
        Vector<double> vmin = new Vector<double>(double.MaxValue);
        Vector<double> vmax = new Vector<double>(double.MinValue);
        for (; i + w * 2 <= n; i += w * 2)
        {
          Vector<double> a = new Vector<double>(vals.Slice(i, w));
          Vector<double> b = new Vector<double>(vals.Slice(i + w, w));
          vmin = Vector.Min(vmin, Vector.Min(a, b));
          vmax = Vector.Max(vmax, Vector.Max(a, b));
        }
        for (int k = 0; k < w; k++)
        {
          if ((k & 1) == 0)
          {
            if (vmin[k] < mnEven) mnEven = vmin[k];
            if (vmax[k] > mxEven) mxEven = vmax[k];
          }
          else
          {
            if (vmin[k] < mnOdd) mnOdd = vmin[k];
            if (vmax[k] > mxOdd) mxOdd = vmax[k];
          }
        }
      }

      for (; i + 1 < n; i += 2)
      {
        double x = vals[i], y = vals[i + 1];
        if (x < mnEven) mnEven = x;
        if (x > mxEven) mxEven = x;
        if (y < mnOdd) mnOdd = y;
        if (y > mxOdd) mxOdd = y;
      }
      minEven = mnEven; maxEven = mxEven; minOdd = mnOdd; maxOdd = mxOdd;
    }

    /// <summary>
    /// Fills a list with 'count' elements (without allocating anything beyond the
    /// list's own storage) and returns a span over them for direct writing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Span<T> GrowUninitialized<T>(List<T> list, int count)
    {
      CollectionsMarshal.SetCount(list, count);
      return CollectionsMarshal.AsSpan(list);
    }

    /// <summary>
    /// A pooled scratch array (used by the geometry algorithms that need
    /// temporary flags/distance buffers).
    /// </summary>
    internal sealed class Scratch<T> : IDisposable
    {
      private T[]? _array;

      /// <summary>
      /// Rents a scratch buffer of the requested size. nb: the buffer is always
      /// cleared, because ArrayPool hands out dirty arrays and the geometry
      /// algorithms rely on zeroed flags.
      /// </summary>
      public Span<T> Rent(int size)
      {
        _array = System.Buffers.ArrayPool<T>.Shared.Rent(size);
        Span<T> span = _array.AsSpan(0, size);
        span.Clear();
        return span;
      }

      /// <summary>
      /// Rents a scratch buffer without clearing it - only for callers that
      /// write every element before reading it (clearing a rented array is an
      /// extra pass over the whole buffer).
      /// </summary>
      public Span<T> RentDirty(int size)
      {
        _array = System.Buffers.ArrayPool<T>.Shared.Rent(size);
        return _array.AsSpan(0, size);
      }

      public void Dispose()
      {
        if (_array != null)
        {
          System.Buffers.ArrayPool<T>.Shared.Return(_array);
          _array = null;
        }
      }
    }
  }
}
