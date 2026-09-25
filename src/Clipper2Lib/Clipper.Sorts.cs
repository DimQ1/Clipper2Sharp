/*******************************************************************************
* Purpose   :  Sort helpers for the engine's hot lists.                        *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* The engine sorts three lists per operation:                                   *
*   * the intersection list (the hottest - a single big union builds hundreds    *
*     of thousands of nodes; sorting was ~35% of the operation),                *
*   * the triangulator's edges (by vL.X) and vertices (Y descending, X ascending),
*     another ~14% of a triangulation.                                          *
*                                                                              *
* All three used to go through the generic sorts, and every one of those routes *
* dispatches per comparison:                                                    *
*   * List<T>.Sort(Comparison<T>)                -> delegate call               *
*   * List<T>.Sort(IComparer<T>)                 -> IComparer<T> call            *
*   * MemoryExtensions.Sort(span, struct comparer) -> same: the runtime's       *
*     ArraySortHelper is not specialised for a struct comparer, which the       *
*     profiler showed as 11.6% of a workload inside one Compare frame.          *
*                                                                              *
* So the sort below is written once, generic over the element and over a struct *
* comparer, which the JIT turns into a direct (non-virtual) call per comparison.*
* The algorithm is a faithful copy of libstdc++'s std::sort (introsort:         *
* median-of-3 quicksort, insertion sort under 16 elements, heapsort fallback    *
* when the recursion depth exceeds 2*log2(n)) - the same algorithm the C++      *
* engine gets from std::sort, so elements that compare equal end up in the same *
* relative order as the C++ build produces.                                     *
*******************************************************************************/

#nullable enable
using System;
using System.Runtime.CompilerServices;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  /// <summary>The ordering used by <see cref="IntroSort"/>; implemented by structs
  /// so that <c>Less</c> is a direct, inlinable call rather than an interface or
  /// delegate dispatch.</summary>
  internal interface ISortComparer<T>
  {
    bool Less(in T a, in T b);
  }

  internal static class IntroSort
  {
    private const int insertionThreshold = 16;

    public static void Sort<T, TComparer>(Span<T> items, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      int n = items.Length;
      if (n < 2) return;
      Loop(items, 0, n, 2 * FloorLog2(n), comparer);
      FinalInsertionSort(items, 0, n, comparer);
    }

    private static int FloorLog2(int n)
    {
      int result = 0;
      while (n > 1) { n >>= 1; result++; }
      return result;
    }

    private static void Loop<T, TComparer>(Span<T> items, int lo, int hi, int depthLimit, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      while (hi - lo > insertionThreshold)
      {
        if (depthLimit == 0)
        {
          PartialSort(items, lo, hi, comparer);
          return;
        }
        depthLimit--;
        int cut = UnguardedPartitionPivot(items, lo, hi, comparer);
        Loop(items, cut, hi, depthLimit, comparer);
        hi = cut;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap<T>(Span<T> items, int i, int j)
    {
      T tmp = items[i];
      items[i] = items[j];
      items[j] = tmp;
    }

    /// <summary>Moves the median of three candidates into <paramref name="result"/>
    /// (libstdc++ __move_median_to_first).</summary>
    private static void MoveMedianToFirst<T, TComparer>(Span<T> items, int result, int a, int b, int c,
      TComparer comparer) where TComparer : struct, ISortComparer<T>
    {
      if (comparer.Less(items[a], items[b]))
      {
        if (comparer.Less(items[b], items[c])) Swap(items, result, b);
        else if (comparer.Less(items[a], items[c])) Swap(items, result, c);
        else Swap(items, result, a);
      }
      else if (comparer.Less(items[a], items[c])) Swap(items, result, a);
      else if (comparer.Less(items[b], items[c])) Swap(items, result, c);
      else Swap(items, result, b);
    }

    private static int UnguardedPartitionPivot<T, TComparer>(Span<T> items, int lo, int hi, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      int mid = lo + (hi - lo) / 2;
      MoveMedianToFirst(items, lo, lo + 1, mid, hi - 1, comparer);
      return UnguardedPartition(items, lo + 1, hi, lo, comparer);
    }

    /// <summary>libstdc++ __unguarded_partition: the pivot stays at
    /// <paramref name="pivot"/>, which must be inside [lo, hi).</summary>
    private static int UnguardedPartition<T, TComparer>(Span<T> items, int lo, int hi, int pivot,
      TComparer comparer) where TComparer : struct, ISortComparer<T>
    {
      T pivotItem = items[pivot];
      while (true)
      {
        while (comparer.Less(items[lo], pivotItem)) lo++;
        hi--;
        while (comparer.Less(pivotItem, items[hi])) hi--;
        if (lo >= hi) return lo;
        Swap(items, lo, hi);
        lo++;
      }
    }

    private static void FinalInsertionSort<T, TComparer>(Span<T> items, int lo, int hi, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      if (hi - lo > insertionThreshold)
      {
        InsertionSort(items, lo, lo + insertionThreshold, comparer);
        for (int i = lo + insertionThreshold; i < hi; i++) LinearInsert(items, i, lo, comparer);
      }
      else
      {
        InsertionSort(items, lo, hi, comparer);
      }
    }

    /// <summary>Insertion sort (libstdc++ __insertion_sort).</summary>
    private static void InsertionSort<T, TComparer>(Span<T> items, int lo, int hi, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      if (lo == hi) return;
      for (int i = lo + 1; i < hi; i++)
      {
        if (comparer.Less(items[i], items[lo]))
        {
          T val = items[i];
          for (int j = i; j > lo; j--) items[j] = items[j - 1];
          items[lo] = val;
        }
        else
        {
          LinearInsert(items, i, lo, comparer);
        }
      }
    }

    /// <summary>
    /// Inserts <paramref name="pos"/> into the sorted run to its left, never
    /// scanning past <paramref name="guard"/>. (libstdc++ uses an unguarded scan
    /// here and relies on a sentinel; bounding it costs one comparison per shift
    /// and cannot walk off the buffer.)
    /// </summary>
    private static void LinearInsert<T, TComparer>(Span<T> items, int pos, int guard, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      T val = items[pos];
      int j = pos - 1;
      while (j >= guard && comparer.Less(val, items[j]))
      {
        items[j + 1] = items[j];
        j--;
      }
      items[j + 1] = val;
    }

    /// <summary>Heap sort fallback (libstdc++ __partial_sort over the whole range)
    /// used when the quicksort recursion gets too deep.</summary>
    private static void PartialSort<T, TComparer>(Span<T> items, int lo, int hi, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      int n = hi - lo;
      for (int i = n / 2; i > 0; i--) SiftDown(items, lo, i, n, comparer);
      for (int end = n - 1; end > 0; end--)
      {
        Swap(items, lo, lo + end);
        SiftDown(items, lo, 1, end, comparer);
      }
    }

    private static void SiftDown<T, TComparer>(Span<T> items, int lo, int start, int end, TComparer comparer)
      where TComparer : struct, ISortComparer<T>
    {
      int root = start - 1;
      int child = 2 * root + 1;
      while (child < end)
      {
        if (child + 1 < end && comparer.Less(items[lo + child], items[lo + child + 1]))
          child++;
        if (comparer.Less(items[lo + root], items[lo + child]))
        {
          Swap(items, lo + root, lo + child);
          root = child;
          child = 2 * root + 1;
        }
        else break;
      }
    }
  }

  // The orderings the engine needs ---------------------------------------------

  /// <summary>Bottom up (Y descending) and, within a scanbeam, left to right
  /// (X ascending) - the engine's intersection list order.</summary>
  internal readonly struct IntersectNodeLess : ISortComparer<IntersectNode>
  {
    public bool Less(in IntersectNode a, in IntersectNode b)
    {
      if (a.pt.Y != b.pt.Y) return b.pt.Y < a.pt.Y;
      return a.pt.X < b.pt.X;
    }
  }

  /// <summary>Edges from left to right (the triangulator's edge list).</summary>
  internal readonly struct EdgeLess : ISortComparer<Edge>
  {
    public bool Less(in Edge a, in Edge b) => a.vL!.pt.X < b.vL!.pt.X;
  }

  /// <summary>Vertices top first, and left to right within a scanbeam (the
  /// triangulator's vertex list).</summary>
  internal readonly struct Vertex2Less : ISortComparer<Vertex2>
  {
    public bool Less(in Vertex2 a, in Vertex2 b)
    {
      if (a.pt.Y != b.pt.Y) return b.pt.Y < a.pt.Y;
      return a.pt.X < b.pt.X;
    }
  }
}
