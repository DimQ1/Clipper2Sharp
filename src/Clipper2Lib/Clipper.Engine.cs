/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  21 February 2026                                                *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2026                                         *
* Purpose   :  This is the main polygon clipping module                        *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.engine.h + src/clipper.engine.cpp (ver. 2.0.1)   *
*******************************************************************************/

#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  [Flags]
  public enum VertexFlags : uint
  {
    Empty = 0,
    OpenStart = 1,
    OpenEnd = 2,
    LocalMax = 4,
    LocalMin = 8
  }

  internal enum JoinWith { NoJoin, Left, Right }

  /// <summary>
  /// An input vertex. Vertices live in one array (a VertexStore) and link to
  /// each other by index (-1 == none), so they are plain data: no object per
  /// vertex, nothing for the GC to trace.
  /// </summary>
  internal struct Vertex
  {
    public Point64 pt;
    public int next;
    public int prev;
    public VertexFlags flags;
  }

  /// <summary>
  /// A point of an output polygon. Out-points live in one array owned by the
  /// ClipperBase and refer to each other (and to their OutRec, by its index in
  /// the out-rec list) by index, so the struct holds no object reference: the
  /// arena is rented from the shared array pool, is never scanned by the GC,
  /// and relinking a ring stores plain ints (no write barrier).
  /// </summary>
  internal struct OutPt
  {
    public Point64 pt;
    public int next;
    public int prev;
    public int outrec;  // index of the owning OutRec in _outrecList
    public bool horz;   // the C++ 'horz' pointer is only ever tested for null
  }

  internal class OutRec
  {
    public int idx;
    public OutRec? owner;
    public int frontEdge = -1;   // index of the front edge in the actives arena (-1 == none)
    public int backEdge = -1;
    public int pts = -1;         // out-point index (-1 == none)
    public PolyPathBase? polypath;
    public List<OutRec>? splits;
    public OutRec? recursiveSplit;
    public Rect64 bounds = new Rect64();
    public Path64? path; // created when needed (only polytree output reads it)
    public bool isOpen;
  }

  /// <summary>
  /// An edge in the active edge list. Actives live in one array owned by the
  /// ClipperBase (an arena) and refer to each other by handle - the element's
  /// byte offset in the arena, -1 meaning none: relinking the AEL/SEL then
  /// stores plain ints instead of object references, so the list surgery that
  /// dominates a big sweep pays no GC write barrier, and the intersection nodes
  /// become plain data that sorts twice as fast (docs/optimization-plan.md 2.2
  /// and phase 2).
  /// </summary>
  // nb: explicit layout so that the fields read while walking the AEL/SEL
  // (positions, slope, links, top vertex) share the first 64 bytes of the
  // element, and the two references sit together at the end
  [StructLayout(LayoutKind.Explicit)]
  internal struct Active
  {
#if USINGZ
    private const int PtSize = 24;
#else
    private const int PtSize = 16;
#endif
    private const int LinkBase = 16 + 2 * PtSize;
    private const int RefBase = LinkBase + 48; // 8 byte aligned for the references

    [FieldOffset(0)] public long currX;          // current (updated at every new scanline)
    [FieldOffset(8)] public double dx;
    [FieldOffset(16)] public Point64 bot;
    [FieldOffset(16 + PtSize)] public Point64 top;

    // AEL: 'active edge list' (Vatti's AET - active edge table)
    //      a linked list of all edges (from left to right) that are present
    //      (or 'active') within the current scanbeam (a horizontal 'beam' that
    //      sweeps from bottom to top over the paths in the clipping operation).
    [FieldOffset(LinkBase)] public int prevInAEL;
    [FieldOffset(LinkBase + 4)] public int nextInAEL;

    // SEL: 'sorted edge list' (Vatti's ST - sorted table)
    //      linked list used when sorting edges into their new positions at the
    //      top of scanbeams, but also (re)used to process horizontals.
    [FieldOffset(LinkBase + 8)] public int prevInSEL;
    [FieldOffset(LinkBase + 12)] public int nextInSEL;
    [FieldOffset(LinkBase + 16)] public int jump;
    [FieldOffset(LinkBase + 20)] public int windDx;          // 1 or -1 depending on winding direction
    [FieldOffset(LinkBase + 24)] public int vertexTop;       // vertex index
    [FieldOffset(LinkBase + 28)] public int windCount;
    [FieldOffset(LinkBase + 32)] public int windCount2;      // winding count of the opposite polytype
    [FieldOffset(LinkBase + 36)] public JoinWith joinWith;
    [FieldOffset(LinkBase + 40)] public bool isLeftBound;
    [FieldOffset(RefBase)] public OutRec? outrec;
    [FieldOffset(RefBase + 8)] public LocalMinima? localMin; // the bottom of an edge 'bound' (also Vatti)
  }

  internal class LocalMinima
  {
    public readonly int vertex;   // vertex index in the owning VertexStore
    public readonly PathType polytype;
    public readonly bool isOpen;

    public LocalMinima(int vertex, PathType polytype, bool isOpen)
    {
      this.vertex = vertex;
      this.polytype = polytype;
      this.isOpen = isOpen;
    }
    // nb: instances are always compared by reference, mirroring the C++ pointer compare
  }

  internal readonly struct IntersectNode
  {
    public readonly Point64 pt;
    public readonly int edge1;   // indices into the actives arena
    public readonly int edge2;

    public IntersectNode(int e1, int e2, Point64 pt)
    {
      this.pt = pt;
      edge1 = e1;
      edge2 = e2;
    }
  }

  internal struct HorzSegment
  {
    public int leftOp;     // out-point indices
    public int rightOp;    // -1 == none
    public bool leftToRight;

    public HorzSegment(int op) { leftOp = op; rightOp = -1; leftToRight = true; }
  }

  internal readonly struct HorzJoin
  {
    public readonly int op1;
    public readonly int op2;

    public HorzJoin(int ltr, int rtl)
    {
      op1 = ltr;
      op2 = rtl;
    }
  }

  // ReuseableDataContainer64 ---------------------------------------------------

  public class ReuseableDataContainer64
  {
    internal readonly List<LocalMinima> _minimaList = new List<LocalMinima>();
    internal readonly VertexStore _vertices = new VertexStore();

    public virtual void Clear()
    {
      _minimaList.Clear();
      _vertices.Release();
    }

    public void AddPaths(Paths64 paths, PathType polytype, bool isOpen)
    {
      ClipperEngine.AddPaths_(paths, polytype, isOpen, _vertices, _minimaList);
    }
  }

  // Internal helper functions --------------------------------------------------

  internal static class ClipperEngine
  {
    internal static readonly Rect64 InvalidRect = new Rect64(false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOdd(int val) => (val & 1) != 0;

    // nb: the helpers below read a single active, so they take it by reference;
    // those that follow the AEL/SEL links live on ClipperBase (they need the arena)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHotEdge(in Active e) => e.outrec != null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOpen(in Active e) => e.localMin!.isOpen;

    /*******************************************************************************
      *  Dx:                             0(90deg)                                    *
      *                                  |                                           *
      *               +inf (180deg) <--- o ---> -inf (0deg)                          *
      *******************************************************************************/

    internal static double GetDx(Point64 pt1, Point64 pt2)
    {
      double dy = (double) (pt2.Y - pt1.Y);
      if (dy != 0)
        return (double) (pt2.X - pt1.X) / dy;
      else if (pt2.X > pt1.X)
        return -double.MaxValue;
      else
        return double.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long TopX(in Active ae, long currentY)
    {
      if ((currentY == ae.top.Y) || (ae.top.X == ae.bot.X)) return ae.top.X;
      else if (currentY == ae.bot.Y) return ae.bot.X;
      else return ae.bot.X + (long) Math.Round(ae.dx * (currentY - ae.bot.Y));
      // nb: rounding (rather than truncation) substantially *improves* performance here
      // as it greatly improves the likelihood of edge adjacency in ProcessIntersectList().
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHorizontal(in Active e) => e.top.Y == e.bot.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHeadingRightHorz(in Active e) => e.dx == -double.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHeadingLeftHorz(in Active e) => e.dx == double.MaxValue;

    internal static void SwapActives(ref int e1, ref int e2)
    {
      (e1, e2) = (e2, e1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PathType GetPolyType(in Active e) => e.localMin!.polytype;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSamePolyType(in Active e1, in Active e2) =>
      e1.localMin!.polytype == e2.localMin!.polytype;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDx(ref Active e)
    {
      e.dx = GetDx(e.bot, e.top);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsJoined(in Active e) => e.joinWith != JoinWith.NoJoin;

    internal static int IntersectListSort(IntersectNode a, IntersectNode b)
    {
      // note different inequality tests ...
      return (a.pt.Y == b.pt.Y) ? a.pt.X.CompareTo(b.pt.X) : b.pt.Y.CompareTo(a.pt.Y);
    }

    internal static double AreaTriangle(Point64 pt1, Point64 pt2, Point64 pt3)
    {
      return ((double) (pt3.Y + pt1.Y) * (double) (pt3.X - pt1.X) +
        (double) (pt1.Y + pt2.Y) * (double) (pt1.X - pt2.X) +
        (double) (pt2.Y + pt3.Y) * (double) (pt2.X - pt3.X));
    }

    internal static void DisposeOutPts(OutRec outrec)
    {
      // nb: the out-points stay in the arena (it is recycled as a whole), so it
      // is enough to unhook the ring from its OutRec
      outrec.pts = -1;
    }

    internal static OutRec? GetRealOutRec(OutRec? outrec)
    {
      while (outrec != null && outrec.pts < 0) outrec = outrec.owner;
      return outrec;
    }

    internal static bool IsValidOwner(OutRec outrec, OutRec? testOwner)
    {
      // prevent outrec owning itself either directly or indirectly
      while (testOwner != null && !ReferenceEquals(testOwner, outrec))
        testOwner = testOwner.owner;
      return testOwner == null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool PtsReallyClose(Point64 pt1, Point64 pt2)
    {
      return (Math.Abs(pt1.X - pt2.X) < 2) && (Math.Abs(pt1.Y - pt2.Y) < 2);
    }

    internal static void SetOwner(OutRec outrec, OutRec newOwner)
    {
      // precondition1: new_owner is never null
      newOwner.owner = GetRealOutRec(newOwner.owner);
      OutRec? tmp = newOwner;
      while (tmp != null && !ReferenceEquals(tmp, outrec)) tmp = tmp.owner;
      if (tmp != null) newOwner.owner = outrec.owner;
      outrec.owner = newOwner;
    }

    internal static void AddLocMin(List<LocalMinima> list, Vertex[] vs,
      int vert, PathType polytype, bool isOpen)
    {
      // make sure the vertex is added only once ...
      if ((VertexFlags.LocalMin & vs[vert].flags) != VertexFlags.Empty) return;

      vs[vert].flags |= VertexFlags.LocalMin;
      list.Add(new LocalMinima(vert, polytype, isOpen));
    }

    internal static void AddPaths_(Paths64 paths, PathType polytype, bool isOpen,
      VertexStore store, List<LocalMinima> locMinList)
    {
      long totalVertexCount = 0;
      foreach (Path64 path in paths) totalVertexCount += path.Count;
      if (totalVertexCount == 0) return;
      // nb: the batch size is known here, so the store is grown once. The count
      // must include the vertices already stored, because the same store
      // receives the subject and then the clip paths.
      store.EnsureCapacity(store.count + (int) totalVertexCount);
      Vertex[] vs = store.items; // stable below: the capacity is already there

      foreach (Path64 path in paths)
      {
        // for each path create a circular double linked list of vertices
        int v0 = -1, prevV = -1;
        int cnt = 0;
        if (path.Count == 0) continue;

        Span<Point64> pts = CollectionsMarshal.AsSpan(path);
        for (int i = 0; i < pts.Length; i++)
        {
          Point64 pt = pts[i];
          if (prevV >= 0 && vs[prevV].pt == pt) continue; // ie skips duplicates
          int currV = store.Add(pt, prevV);
          if (prevV >= 0) vs[prevV].next = currV;
          if (v0 < 0) v0 = currV;
          prevV = currV;
          cnt++;
        }
        if (prevV < 0 || vs[prevV].prev < 0) continue;
        if (!isOpen && vs[prevV].pt == vs[v0].pt)
          prevV = vs[prevV].prev; // drop the duplicate closing vertex
        vs[prevV].next = v0;
        vs[v0].prev = prevV;
        if (cnt < 2 || (cnt == 2 && !isOpen)) continue;

        // now find and assign local minima
        bool goingUp, goingUp0;
        int currVertex;
        if (isOpen)
        {
          currVertex = vs[v0].next;
          while (currVertex != v0 && vs[currVertex].pt.Y == vs[v0].pt.Y)
            currVertex = vs[currVertex].next;
          goingUp = vs[currVertex].pt.Y <= vs[v0].pt.Y;
          if (goingUp)
          {
            vs[v0].flags = VertexFlags.OpenStart;
            AddLocMin(locMinList, vs, v0, polytype, true);
          }
          else
            vs[v0].flags = VertexFlags.OpenStart | VertexFlags.LocalMax;
        }
        else // closed path
        {
          int prevVertex0 = vs[v0].prev;
          while (prevVertex0 != v0 && vs[prevVertex0].pt.Y == vs[v0].pt.Y)
            prevVertex0 = vs[prevVertex0].prev;
          if (prevVertex0 == v0)
            continue; // only open paths can be completely flat
          goingUp = vs[prevVertex0].pt.Y > vs[v0].pt.Y;
        }

        goingUp0 = goingUp;
        int prevVtx = v0;
        currVertex = vs[v0].next;
        while (currVertex != v0)
        {
          if (vs[currVertex].pt.Y > vs[prevVtx].pt.Y && goingUp)
          {
            vs[prevVtx].flags |= VertexFlags.LocalMax;
            goingUp = false;
          }
          else if (vs[currVertex].pt.Y < vs[prevVtx].pt.Y && !goingUp)
          {
            goingUp = true;
            AddLocMin(locMinList, vs, prevVtx, polytype, isOpen);
          }
          prevVtx = currVertex;
          currVertex = vs[currVertex].next;
        }

        if (isOpen)
        {
          vs[prevVtx].flags |= VertexFlags.OpenEnd;
          if (goingUp)
            vs[prevVtx].flags |= VertexFlags.LocalMax;
          else
            AddLocMin(locMinList, vs, prevVtx, polytype, isOpen);
        }
        else if (goingUp != goingUp0)
        {
          if (goingUp0) AddLocMin(locMinList, vs, prevVtx, polytype, false);
          else vs[prevVtx].flags |= VertexFlags.LocalMax;
        }
      } // end processing current path
    }

    /// <summary>
    /// Stable sort helper (mirrors std::stable_sort used in the C++ engine).
    /// </summary>
    internal interface IStableComparer<T>
    {
      int Compare(in T a, in T b);
    }

    /// <summary>
    /// Stable merge sort (mirrors std::stable_sort used in the C++ engine). The
    /// comparer is a struct type parameter (a direct, inlinable call) and the
    /// merge buffer is kept by the caller, so repeated sorts allocate nothing.
    /// The merge decisions are unchanged, so the resulting order is too.
    /// </summary>
    internal static void StableSort<T, TComparer>(Span<T> items, ref T[]? buffer, TComparer comparer)
      where TComparer : struct, IStableComparer<T>
    {
      int n = items.Length;
      if (n < 2) return;
      if (buffer == null || buffer.Length < n) buffer = new T[Math.Max(n, 16)];
      MergeSort(items, buffer, 0, n - 1, comparer);
    }

    private static void MergeSort<T, TComparer>(Span<T> list, T[] tmp, int lo, int hi, TComparer comparer)
      where TComparer : struct, IStableComparer<T>
    {
      if (lo >= hi) return;
      int mid = lo + ((hi - lo) >> 1);
      MergeSort(list, tmp, lo, mid, comparer);
      MergeSort(list, tmp, mid + 1, hi, comparer);
      int i = lo, j = mid + 1, k = lo;
      while (i <= mid && j <= hi)
      {
        if (comparer.Compare(list[i], list[j]) <= 0) tmp[k++] = list[i++];
        else tmp[k++] = list[j++];
      }
      while (i <= mid) tmp[k++] = list[i++];
      while (j <= hi) tmp[k++] = list[j++];
      for (int x = lo; x <= hi; x++) list[x] = tmp[x];
    }
  }

  // ClipperBase -----------------------------------------------------------------

  public class ClipperBase
  {
    private ClipType _cliptype = ClipType.NoClip;
    private FillRule _fillrule = FillRule.EvenOdd;
    private readonly FillRule _fillpos = FillRule.Positive;
    private long _botY;
    private bool _minimaListSorted;
    internal bool _usingPolytree;
    // the actives arena (see Active): _act[0.._actCount) are the edges created
    // by the current operation, _actives / _sel are the list heads (-1 == empty)
    private Active[] _act = Array.Empty<Active>();
    private int _actCount;
    private int _actives = -1;
    private int _sel = -1;

    internal readonly List<LocalMinima> _minimaList = new List<LocalMinima>();
    private int _currentLocMin;
    private readonly VertexStore _vertices = new VertexStore();
    // nb: _vtx is _vertices.items, captured when an operation starts (vertices
    // are only added between operations, so it cannot change during one)
    private Vertex[] _vtx = Array.Empty<Vertex>();
    // the out-points arena (see OutPt): _op[0.._opCount) belong to the current
    // operation; it is rented from the shared pool and returned by CleanUp
    private OutPt[] _op = Array.Empty<OutPt>();
    private int _opCount;
    private readonly OutRecPoolList _outrecPool = new OutRecPoolList();

    // nb: the C++ std::priority_queue<int64_t> is a max-heap, so the heap here
    // returns scanline Y values in descending order (and skips duplicates)
    private ScanlineHeap _scanlineList = new ScanlineHeap(64);
    private readonly List<IntersectNode> _intersectNodes = new List<IntersectNode>();
    private readonly List<HorzSegment> _horzSegList = new List<HorzSegment>();
    private readonly List<HorzJoin> _horzJoinList = new List<HorzJoin>();

    protected bool _preserveCollinear = true;
    protected bool _reverseSolution;
    protected int _errorCode;
    protected bool _hasOpenPaths;
    protected bool _succeeded = true;
    private protected readonly List<OutRec> _outrecList = new List<OutRec>();

#if USINGZ
    public delegate void ZCallback64(Point64 bot1, Point64 top1,
      Point64 bot2, Point64 top2, ref Point64 intersectPt);

    public long DefaultZ { get; set; }
    protected ZCallback64? _zCallback;

    public void SetZCallback(ZCallback64 callback) { _zCallback = callback; }
#endif

    public ClipperBase()
    {
      _currentLocMin = 0;
    }

#if USINGZ
    private static bool XYCoordsEqual(Point64 pt1, Point64 pt2)
    {
      return (pt1.X == pt2.X && pt1.Y == pt2.Y);
    }

    private void SetZ(in Active e1, in Active e2, ref Point64 intersectPt)
    {
      if (_zCallback == null) return;
      // prioritize subject over clip vertices by passing
      // subject vertices before clip vertices in the callback
      if (ClipperEngine.GetPolyType(e1) == PathType.Subject)
      {
        if (XYCoordsEqual(intersectPt, e1.bot))
          intersectPt = new Point64(intersectPt, e1.bot.Z);
        else if (XYCoordsEqual(intersectPt, e1.top))
          intersectPt = new Point64(intersectPt, e1.top.Z);
        else if (XYCoordsEqual(intersectPt, e2.bot))
          intersectPt = new Point64(intersectPt, e2.bot.Z);
        else if (XYCoordsEqual(intersectPt, e2.top))
          intersectPt = new Point64(intersectPt, e2.top.Z);
        else
          intersectPt = new Point64(intersectPt) { Z = DefaultZ };
        _zCallback(e1.bot, e1.top, e2.bot, e2.top, ref intersectPt);
      }
      else
      {
        if (XYCoordsEqual(intersectPt, e2.bot))
          intersectPt = new Point64(intersectPt, e2.bot.Z);
        else if (XYCoordsEqual(intersectPt, e2.top))
          intersectPt = new Point64(intersectPt, e2.top.Z);
        else if (XYCoordsEqual(intersectPt, e1.bot))
          intersectPt = new Point64(intersectPt, e1.bot.Z);
        else if (XYCoordsEqual(intersectPt, e1.top))
          intersectPt = new Point64(intersectPt, e1.top.Z);
        else
          intersectPt = new Point64(intersectPt) { Z = DefaultZ };
        _zCallback(e2.bot, e2.top, e1.bot, e1.top, ref intersectPt);
      }
    }
#endif

    public bool PreserveCollinear
    {
      get => _preserveCollinear;
      set => _preserveCollinear = value;
    }

    public bool ReverseSolution
    {
      get => _reverseSolution;
      set => _reverseSolution = value;
    }

    public int ErrorCode() => _errorCode;

    internal int PooledObjectCount =>
      _vertices.Capacity + _outrecPool.Capacity;

    // An active is addressed by its *byte offset* in the arena (-1 == none): a
    // following link is then 'load + add', where an element index would need a
    // multiplication by the struct size in the middle of every pointer chase
    // (measured: the AEL walks of a large offset union ran ~15% slower that way).
    // nb: a handle is only ever produced by NewActive, and the arena is sized for
    // the whole operation up front (Reset), so it never moves while an operation
    // holds references into it; a handle is therefore always in range
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Active A(int handle) =>
      ref Unsafe.As<byte, Active>(ref Unsafe.AddByteOffset(
        ref Unsafe.As<Active, byte>(ref MemoryMarshal.GetArrayDataReference(_act)), (nint) (uint) handle));

    private int NewActive()
    {
      int i = _actCount++;
      if (i == _act.Length) Array.Resize(ref _act, Math.Max(16, _act.Length * 2));
      if ((long) _act.Length * Unsafe.SizeOf<Active>() > int.MaxValue)
        InternalClipper.DoError(Clipper2Error.RangeError);
      ref Active a = ref _act[i];
      a = default;
      a.windDx = 1;
      a.prevInAEL = -1;
      a.nextInAEL = -1;
      a.prevInSEL = -1;
      a.nextInSEL = -1;
      a.jump = -1;
      return i * Unsafe.SizeOf<Active>();
    }

    private void DeleteEdges()
    {
      // release the references the actives hold (vertices, minima, out-recs)
      // and hand the arena back: it is rented from the shared pool, because an
      // arena of more than ~700 actives is a large object heap allocation, and a
      // fresh one per operation (ClipperOffset runs a Clipper64 per call) would
      // cost more than the per-edge objects it replaces
      if (_actCount > 0) Array.Clear(_act, 0, _actCount);
      if (_act.Length > 0) System.Buffers.ArrayPool<Active>.Shared.Return(_act);
      _act = Array.Empty<Active>();
      _actCount = 0;
      _actives = -1;
      _sel = -1;
    }

    protected void CleanUp()
    {
      DeleteEdges();
      _scanlineList.Clear();
      _intersectNodes.Clear();
      DisposeAllOutRecs();
      _horzSegList.Clear();
      _horzJoinList.Clear();
      // nb: pooled objects are ready for reuse by the next operation
      _outrecPool.Clear();
      if (_op.Length > 0) System.Buffers.ArrayPool<OutPt>.Shared.Return(_op);
      _op = Array.Empty<OutPt>();
      _opCount = 0;
    }

    public void Clear()
    {
      CleanUp();
      DisposeVerticesAndLocalMinima();
      _currentLocMin = 0;
      _minimaListSorted = false;
      _hasOpenPaths = false;
    }

    private void Reset()
    {
      _vtx = _vertices.items;
      if (!_minimaListSorted)
      {
        ClipperEngine.StableSort(CollectionsMarshal.AsSpan(_minimaList),
          ref _minimaSortBuffer, new LocMinComparer(_vtx)); // #594
        _minimaListSorted = true;
      }
      for (int i = _minimaList.Count - 1; i >= 0; i--)
        InsertScanline(Vx(_minimaList[i].vertex).pt.Y);

      _currentLocMin = 0;
      // every local minimum creates at most two actives, so the arena is sized
      // once here and never has to grow (or move) during the sweep
      int needed = 2 * _minimaList.Count;
      if (_act.Length < needed)
      {
        if (_act.Length > 0) System.Buffers.ArrayPool<Active>.Shared.Return(_act);
        _act = System.Buffers.ArrayPool<Active>.Shared.Rent(Math.Max(needed, 16));
      }
      // an output has about as many points as the input has vertices; the
      // arena grows (by doubling) when an operation needs more
      _opCount = 0;
      int opNeeded = _vertices.count + 16;
      if (_op.Length < opNeeded)
      {
        if (_op.Length > 0) System.Buffers.ArrayPool<OutPt>.Shared.Return(_op);
        _op = System.Buffers.ArrayPool<OutPt>.Shared.Rent(opNeeded);
      }
      _actCount = 0;
      _actives = -1;
      _sel = -1;
      _succeeded = true;
    }

    private readonly struct LocMinComparer : ClipperEngine.IStableComparer<LocalMinima>
    {
      private readonly Vertex[] _vs;
      public LocMinComparer(Vertex[] vs) { _vs = vs; }

      public int Compare(in LocalMinima locMin1, in LocalMinima locMin2)
      {
        Point64 p1 = _vs[locMin1.vertex].pt, p2 = _vs[locMin2.vertex].pt;
        if (p2.Y != p1.Y)
          return p2.Y.CompareTo(p1.Y);
        else
          return p2.X.CompareTo(p1.X);
      }
    }

    // Vertices -----------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Vertex Vx(int i) =>
      ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_vtx), i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsMaxima(int v) => (Vx(v).flags & VertexFlags.LocalMax) != VertexFlags.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsMaxima(in Active e) => IsMaxima(e.vertexTop);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsOpenEnd(int v) =>
      (Vx(v).flags & (VertexFlags.OpenStart | VertexFlags.OpenEnd)) != VertexFlags.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsOpenEnd(in Active e) => IsOpenEnd(e.vertexTop);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NextVertex(in Active e)
    {
      if (e.windDx > 0)
        return Vx(e.vertexTop).next;
      else
        return Vx(e.vertexTop).prev;
    }

    // PrevPrevVertex: useful to get the (inverted Y-axis) top of the
    // alternate edge (ie left or right bound) during edge insertion.
    private int PrevPrevVertex(in Active ae)
    {
      if (ae.windDx > 0)
        return Vx(Vx(ae.vertexTop).prev).prev;
      else
        return Vx(Vx(ae.vertexTop).next).next;
    }

    private int GetCurrYMaximaVertex_Open(in Active e)
    {
      int result = e.vertexTop;
      if (e.windDx > 0)
        while (Vx(Vx(result).next).pt.Y == Vx(result).pt.Y &&
          (Vx(result).flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == VertexFlags.Empty)
          result = Vx(result).next;
      else
        while (Vx(Vx(result).prev).pt.Y == Vx(result).pt.Y &&
          (Vx(result).flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == VertexFlags.Empty)
          result = Vx(result).prev;
      if (!IsMaxima(result)) result = -1; // not a maxima
      return result;
    }

    private int GetCurrYMaximaVertex(in Active e)
    {
      int result = e.vertexTop;
      if (e.windDx > 0)
        while (Vx(Vx(result).next).pt.Y == Vx(result).pt.Y) result = Vx(result).next;
      else
        while (Vx(Vx(result).prev).pt.Y == Vx(result).pt.Y) result = Vx(result).prev;
      if (!IsMaxima(result)) result = -1; // not a maxima
      return result;
    }

    private LocalMinima[]? _minimaSortBuffer;
    private HorzSegment[]? _horzSortBuffer;
    private Path64? _pathScratchField;
    private PathD? _pathScratchDField;
    private Path64 _pathScratch => _pathScratchField ??= new Path64();
    private PathD _pathScratchD => _pathScratchDField ??= new PathD();

    /// <summary>A result path with exactly the capacity it needs (the scratch
    /// buffer absorbs the growth, so no intermediate arrays become garbage).</summary>
    private static Path64 ExactCopy(Path64 src)
    {
      Path64 result = new Path64(src.Count);
      result.AddRange(src);
      return result;
    }

    private static PathD ExactCopy(PathD src)
    {
      PathD result = new PathD(src.Count);
      result.AddRange(src);
      return result;
    }

    internal void AddPath(Path64 path, PathType polytype, bool isOpen)
    {
      AddPaths(new Paths64 { path }, polytype, isOpen);
    }

    private Paths64? _singlePath;

    /// <summary>Adds one path without wrapping it in a new Paths64 each time.</summary>
    internal void AddPathDirect(Path64 path, PathType polytype, bool isOpen = false)
    {
      Paths64 one = _singlePath ??= new Paths64(1);
      one.Add(path);
      try { AddPaths(one, polytype, isOpen); }
      finally { one.Clear(); }
    }

    internal void AddPaths(Paths64 paths, PathType polytype, bool isOpen)
    {
      if (isOpen) _hasOpenPaths = true;
      _minimaListSorted = false;
      ClipperEngine.AddPaths_(paths, polytype, isOpen, _vertices, _minimaList);
    }

    public void AddReuseableData(ReuseableDataContainer64 reuseableData)
    {
      // nb: reuseable_data will continue to own the vertices
      // and remains responsible for their clean up.
      _succeeded = false;
      _minimaListSorted = false;
      // nb: the container's vertices are copied behind this engine's own (their
      // links shifted by the offset); the engine never changes a vertex while
      // clipping, so the copy behaves exactly like the shared C++ vertices
      VertexStore src = reuseableData._vertices;
      int offset = _vertices.count;
      _vertices.EnsureCapacity(offset + src.count);
      Array.Copy(src.items, 0, _vertices.items, offset, src.count);
      Vertex[] vs = _vertices.items;
      for (int i = offset; i < offset + src.count; i++)
      {
        if (vs[i].next >= 0) vs[i].next += offset;
        if (vs[i].prev >= 0) vs[i].prev += offset;
      }
      _vertices.count += src.count;
      foreach (LocalMinima lm in reuseableData._minimaList)
      {
        _minimaList.Add(new LocalMinima(lm.vertex + offset, lm.polytype, lm.isOpen));
        if (lm.isOpen) _hasOpenPaths = true;
      }
    }

    private void InsertScanline(long y)
    {
      _scanlineList.Push(y);
    }

    private bool PopScanline(out long y)
    {
      return _scanlineList.Pop(out y);
    }

    private bool PopLocalMinima(long y, out LocalMinima? localMinima)
    {
      localMinima = null;
      if (_currentLocMin == _minimaList.Count ||
        Vx(_minimaList[_currentLocMin].vertex).pt.Y != y) return false;
      localMinima = _minimaList[_currentLocMin++];
      return true;
    }

    private void DisposeAllOutRecs()
    {
      foreach (OutRec outrec in _outrecList)
        outrec.pts = -1;
      _outrecList.Clear();
    }

    private void DisposeVerticesAndLocalMinima()
    {
      _minimaList.Clear();
      _vertices.Clear();
    }

    private bool IsContributingClosed(in Active e)
    {
      switch (_fillrule)
      {
        case FillRule.EvenOdd:
          break;
        case FillRule.NonZero:
          if (Math.Abs(e.windCount) != 1) return false;
          break;
        case FillRule.Positive:
          if (e.windCount != 1) return false;
          break;
        case FillRule.Negative:
          if (e.windCount != -1) return false;
          break;
        default:
          break;
      }

      switch (_cliptype)
      {
        case ClipType.NoClip:
          return false;
        case ClipType.Intersection:
          switch (_fillrule)
          {
            case FillRule.Positive:
              return (e.windCount2 > 0);
            case FillRule.Negative:
              return (e.windCount2 < 0);
            default:
              return (e.windCount2 != 0);
          }

        case ClipType.Union:
          switch (_fillrule)
          {
            case FillRule.Positive:
              return (e.windCount2 <= 0);
            case FillRule.Negative:
              return (e.windCount2 >= 0);
            default:
              return (e.windCount2 == 0);
          }

        case ClipType.Difference:
          bool result;
          switch (_fillrule)
          {
            case FillRule.Positive:
              result = (e.windCount2 <= 0);
              break;
            case FillRule.Negative:
              result = (e.windCount2 >= 0);
              break;
            default:
              result = (e.windCount2 == 0);
              break;
          }
          if (ClipperEngine.GetPolyType(e) == PathType.Subject)
            return result;
          else
            return !result;

        case ClipType.Xor:
          return true;
        default:
          break;
      }
      return false; // we should never get here
    }

    private bool IsContributingOpen(in Active e)
    {
      bool isInClip, isInSubj;
      switch (_fillrule)
      {
        case FillRule.Positive:
          isInClip = e.windCount2 > 0;
          isInSubj = e.windCount > 0;
          break;
        case FillRule.Negative:
          isInClip = e.windCount2 < 0;
          isInSubj = e.windCount < 0;
          break;
        default:
          isInClip = e.windCount2 != 0;
          isInSubj = e.windCount != 0;
          break;
      }

      switch (_cliptype)
      {
        case ClipType.Intersection: return isInClip;
        case ClipType.Union: return (!isInSubj && !isInClip);
        default: return !isInClip;
      }
    }

    private void SetWindCountForClosedPathEdge(int ei)
    {
      // Wind counts refer to polygon regions not edges, so here an edge's WindCnt
      // indicates the higher of the wind counts for the two regions touching the
      // edge. (NB Adjacent regions can only ever have their wind counts differ by
      // one. Also, open paths have no meaningful wind directions or counts.)

      ref Active e = ref A(ei);
      int e2i = e.prevInAEL;
      // find the nearest closed path edge of the same PolyType in AEL (heading left)
      PathType pt = ClipperEngine.GetPolyType(e);
      while (e2i >= 0 && (ClipperEngine.GetPolyType(A(e2i)) != pt || ClipperEngine.IsOpen(A(e2i))))
        e2i = A(e2i).prevInAEL;

      if (e2i < 0)
      {
        e.windCount = e.windDx;
        e2i = _actives;
      }
      else if (_fillrule == FillRule.EvenOdd)
      {
        ref Active e2 = ref A(e2i);
        e.windCount = e.windDx;
        e.windCount2 = e2.windCount2;
        e2i = e2.nextInAEL;
      }
      else
      {
        ref Active e2 = ref A(e2i);
        // NonZero, positive, or negative filling here ...
        // if e's WindCnt is in the SAME direction as its WindDx, then polygon
        // filling will be on the right of 'e'.
        // NB neither e2.WindCnt nor e2.WindDx should ever be 0.
        if (e2.windCount * e2.windDx < 0)
        {
          // opposite directions so 'e' is outside 'e2' ...
          if (Math.Abs(e2.windCount) > 1)
          {
            // outside prev poly but still inside another.
            if (e2.windDx * e.windDx < 0)
              // reversing direction so use the same WC
              e.windCount = e2.windCount;
            else
              // otherwise keep 'reducing' the WC by 1 (ie towards 0) ...
              e.windCount = e2.windCount + e.windDx;
          }
          else
            // now outside all polys of same polytype so set own WC ...
            e.windCount = (ClipperEngine.IsOpen(e) ? 1 : e.windDx);
        }
        else
        {
          // 'e' must be inside 'e2'
          if (e2.windDx * e.windDx < 0)
            // reversing direction so use the same WC
            e.windCount = e2.windCount;
          else
            // otherwise keep 'increasing' the WC by 1 (ie away from 0) ...
            e.windCount = e2.windCount + e.windDx;
        }
        e.windCount2 = e2.windCount2;
        e2i = e2.nextInAEL; // ie get ready to calc WindCnt2
      }

      // update wind_cnt2 ...
      if (_fillrule == FillRule.EvenOdd)
        while (e2i >= 0 && e2i != ei)
        {
          ref Active e2 = ref A(e2i);
          if (ClipperEngine.GetPolyType(e2) != pt && !ClipperEngine.IsOpen(e2))
            e.windCount2 = (e.windCount2 == 0 ? 1 : 0);
          e2i = e2.nextInAEL;
        }
      else
        while (e2i >= 0 && e2i != ei)
        {
          ref Active e2 = ref A(e2i);
          if (ClipperEngine.GetPolyType(e2) != pt && !ClipperEngine.IsOpen(e2))
            e.windCount2 += e2.windDx;
          e2i = e2.nextInAEL;
        }
    }

    private void SetWindCountForOpenPathEdge(int ei)
    {
      ref Active e = ref A(ei);
      int e2i = _actives;
      if (_fillrule == FillRule.EvenOdd)
      {
        int cnt1 = 0, cnt2 = 0;
        while (e2i != ei)
        {
          ref Active e2 = ref A(e2i);
          if (ClipperEngine.GetPolyType(e2) == PathType.Clip)
            cnt2++;
          else if (!ClipperEngine.IsOpen(e2))
            cnt1++;
          e2i = e2.nextInAEL;
        }
        e.windCount = (ClipperEngine.IsOdd(cnt1) ? 1 : 0);
        e.windCount2 = (ClipperEngine.IsOdd(cnt2) ? 1 : 0);
      }
      else
      {
        while (e2i != ei)
        {
          ref Active e2 = ref A(e2i);
          if (ClipperEngine.GetPolyType(e2) == PathType.Clip)
            e.windCount2 += e2.windDx;
          else if (!ClipperEngine.IsOpen(e2))
            e.windCount += e2.windDx;
          e2i = e2.nextInAEL;
        }
      }
    }

    /// <summary>IsValidAelOrder with its first (and by far most common)
    /// decision inlined into the AEL walks: edges at different X are ordered by X.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsValidAelOrderFast(int residentIdx, int newcomerIdx)
    {
      long r = A(residentIdx).currX, n = A(newcomerIdx).currX;
      if (n != r) return n > r;
      return IsValidAelOrder(residentIdx, newcomerIdx);
    }

    private bool IsValidAelOrder(int residentIdx, int newcomerIdx)
    {
      ref Active resident = ref A(residentIdx);
      ref Active newcomer = ref A(newcomerIdx);
      if (newcomer.currX != resident.currX)
        return newcomer.currX > resident.currX;

      // get the turning direction  a1.top, a2.bot, a2.top
      int i = InternalClipper.CrossProductSign(resident.top, newcomer.bot, newcomer.top);
      if (i != 0) return i < 0;

      // edges must be collinear to get here
      // for starting open paths, place them according to
      // the direction they're about to turn
      if (!IsMaxima(resident) && (resident.top.Y > newcomer.top.Y))
      {
        return (InternalClipper.CrossProductSign(newcomer.bot, resident.top,
          Vx(NextVertex(resident)).pt) <= 0);
      }
      else if (!IsMaxima(newcomer) && (newcomer.top.Y > resident.top.Y))
      {
        return (InternalClipper.CrossProductSign(newcomer.bot, newcomer.top,
          Vx(NextVertex(newcomer)).pt) >= 0);
      }

      long y = newcomer.bot.Y;
      bool newcomerIsLeft = newcomer.isLeftBound;

      if (resident.bot.Y != y || Vx(resident.localMin!.vertex).pt.Y != y)
        return newcomer.isLeftBound;
      // resident must also have just been inserted
      else if (resident.isLeftBound != newcomerIsLeft)
        return newcomerIsLeft;
      else if (InternalClipper.IsCollinear(Vx(PrevPrevVertex(resident)).pt,
        resident.bot, resident.top)) return true;
      else
        // compare turning direction of the alternate bound
        return (InternalClipper.CrossProductSign(Vx(PrevPrevVertex(resident)).pt,
          newcomer.bot, Vx(PrevPrevVertex(newcomer)).pt) > 0) == newcomerIsLeft;
    }

    private void InsertLeftEdge(int e)
    {
      if (_actives < 0)
      {
        ref Active ae = ref A(e);
        ae.prevInAEL = -1;
        ae.nextInAEL = -1;
        _actives = e;
      }
      else if (!IsValidAelOrderFast(_actives, e))
      {
        ref Active ae = ref A(e);
        ae.prevInAEL = -1;
        ae.nextInAEL = _actives;
        A(_actives).prevInAEL = e;
        _actives = e;
      }
      else
      {
        int e2 = _actives;
        while (A(e2).nextInAEL >= 0 && IsValidAelOrderFast(A(e2).nextInAEL, e))
          e2 = A(e2).nextInAEL;
        if (A(e2).joinWith == JoinWith.Right)
          e2 = A(e2).nextInAEL;
        if (e2 < 0) return; // should never happen
        ref Active ae = ref A(e);
        ref Active ae2 = ref A(e2);
        ae.nextInAEL = ae2.nextInAEL;
        if (ae2.nextInAEL >= 0) A(ae2.nextInAEL).prevInAEL = e;
        ae.prevInAEL = e2;
        ae2.nextInAEL = e;
      }
    }

    private void InsertRightEdge(int e, int e2)
    {
      ref Active ae = ref A(e);
      ref Active ae2 = ref A(e2);
      ae2.nextInAEL = ae.nextInAEL;
      if (ae.nextInAEL >= 0) A(ae.nextInAEL).prevInAEL = e2;
      ae2.prevInAEL = e;
      ae.nextInAEL = e2;
    }

    private void InsertLocalMinimaIntoAEL(long botY)
    {
      // Add any local minima (if any) at BotY ...
      // nb: horizontal local minima edges should contain locMin.vertex.prev
      while (PopLocalMinima(botY, out LocalMinima? localMinima))
      {
        int leftBound, rightBound;
        if ((Vx(localMinima!.vertex).flags & VertexFlags.OpenStart) != VertexFlags.Empty)
        {
          leftBound = -1;
        }
        else
        {
          leftBound = NewActive();
          ref Active lb = ref A(leftBound);
          lb.bot = Vx(localMinima.vertex).pt;
          lb.currX = lb.bot.X;
          lb.windDx = -1;
          lb.vertexTop = Vx(localMinima.vertex).prev; // ie descending
          lb.top = Vx(lb.vertexTop).pt;
          lb.localMin = localMinima;
          ClipperEngine.SetDx(ref lb);
        }

        if ((Vx(localMinima.vertex).flags & VertexFlags.OpenEnd) != VertexFlags.Empty)
        {
          rightBound = -1;
        }
        else
        {
          rightBound = NewActive();
          ref Active rb = ref A(rightBound);
          rb.bot = Vx(localMinima.vertex).pt;
          rb.currX = rb.bot.X;
          rb.windDx = 1;
          rb.vertexTop = Vx(localMinima.vertex).next; // ie ascending
          rb.top = Vx(rb.vertexTop).pt;
          rb.localMin = localMinima;
          ClipperEngine.SetDx(ref rb);
        }

        // Currently LeftB is just the descending bound and RightB is the ascending.
        // Now if the LeftB isn't on the left of RightB then we need swap them.
        if (leftBound >= 0 && rightBound >= 0)
        {
          if (ClipperEngine.IsHorizontal(A(leftBound)))
          {
            if (ClipperEngine.IsHeadingRightHorz(A(leftBound)))
              ClipperEngine.SwapActives(ref leftBound, ref rightBound);
          }
          else if (ClipperEngine.IsHorizontal(A(rightBound)))
          {
            if (ClipperEngine.IsHeadingLeftHorz(A(rightBound)))
              ClipperEngine.SwapActives(ref leftBound, ref rightBound);
          }
          else if (A(leftBound).dx < A(rightBound).dx)
            ClipperEngine.SwapActives(ref leftBound, ref rightBound);
        }
        else if (leftBound < 0)
        {
          leftBound = rightBound;
          rightBound = -1;
        }

        bool contributing;
        ref Active left = ref A(leftBound);
        left.isLeftBound = true;
        InsertLeftEdge(leftBound);

        if (ClipperEngine.IsOpen(left))
        {
          SetWindCountForOpenPathEdge(leftBound);
          contributing = IsContributingOpen(left);
        }
        else
        {
          SetWindCountForClosedPathEdge(leftBound);
          contributing = IsContributingClosed(left);
        }

        if (rightBound >= 0)
        {
          ref Active right = ref A(rightBound);
          right.isLeftBound = false;
          right.windCount = left.windCount;
          right.windCount2 = left.windCount2;
          InsertRightEdge(leftBound, rightBound);
          if (contributing)
          {
            AddLocalMinPoly(leftBound, rightBound, left.bot, true);
            if (!ClipperEngine.IsHorizontal(left))
              CheckJoinLeft(leftBound, left.bot);
          }

          while (right.nextInAEL >= 0 &&
            IsValidAelOrderFast(right.nextInAEL, rightBound))
          {
            IntersectEdges(rightBound, right.nextInAEL, right.bot);
            SwapPositionsInAEL(rightBound, right.nextInAEL);
          }

          if (ClipperEngine.IsHorizontal(right))
            PushHorz(rightBound);
          else
          {
            CheckJoinRight(rightBound, right.bot);
            InsertScanline(right.top.Y);
          }
        }
        else if (contributing)
        {
          StartOpenPath(leftBound, left.bot);
        }

        if (ClipperEngine.IsHorizontal(left))
          PushHorz(leftBound);
        else
          InsertScanline(left.top.Y);
      } // while (PopLocalMinima())
    }

    private void PushHorz(int e)
    {
      A(e).nextInSEL = _sel;
      _sel = e;
    }

    private bool PopHorz(out int e)
    {
      e = _sel;
      if (e < 0) return false;
      _sel = A(e).nextInSEL;
      return true;
    }

    // Helpers that follow the arena links --------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFront(int e) => e == A(e).outrec!.frontEdge;

    private int GetPrevHotEdge(int e)
    {
      int prev = A(e).prevInAEL;
      while (prev >= 0 && (ClipperEngine.IsOpen(A(prev)) || !ClipperEngine.IsHotEdge(A(prev))))
        prev = A(prev).prevInAEL;
      return prev;
    }

    private int ExtractFromSEL(int ae)
    {
      ref Active a = ref A(ae);
      int res = a.nextInSEL;
      if (res >= 0)
        A(res).prevInSEL = a.prevInSEL;
      A(a.prevInSEL).nextInSEL = res;
      return res;
    }

    private void Insert1Before2InSEL(int ae1, int ae2)
    {
      ref Active a1 = ref A(ae1);
      ref Active a2 = ref A(ae2);
      a1.prevInSEL = a2.prevInSEL;
      if (a1.prevInSEL >= 0)
        A(a1.prevInSEL).nextInSEL = ae1;
      a1.nextInSEL = ae2;
      a2.prevInSEL = ae1;
    }

    private int GetMaximaPair(int e)
    {
      int vt = A(e).vertexTop;
      int e2 = A(e).nextInAEL;
      while (e2 >= 0)
      {
        if (A(e2).vertexTop == vt) return e2; // Found!
        e2 = A(e2).nextInAEL;
      }
      return -1;
    }

    private static void SetSides(OutRec outrec, int startEdge, int endEdge)
    {
      outrec.frontEdge = startEdge;
      outrec.backEdge = endEdge;
    }

    private void SwapOutrecs(int e1, int e2)
    {
      OutRec? or1 = A(e1).outrec;
      OutRec? or2 = A(e2).outrec;
      if (ReferenceEquals(or1, or2))
      {
        int e = or1!.frontEdge;
        or1.frontEdge = or1.backEdge;
        or1.backEdge = e;
        return;
      }
      if (or1 != null)
      {
        if (e1 == or1.frontEdge)
          or1.frontEdge = e2;
        else
          or1.backEdge = e2;
      }
      if (or2 != null)
      {
        if (e2 == or2.frontEdge)
          or2.frontEdge = e1;
        else
          or2.backEdge = e1;
      }
      A(e1).outrec = or2;
      A(e2).outrec = or1;
    }

    private void UncoupleOutRec(int ae)
    {
      OutRec? outrec = A(ae).outrec;
      if (outrec == null) return;
      A(outrec.frontEdge).outrec = null;
      A(outrec.backEdge).outrec = null;
      outrec.frontEdge = -1;
      outrec.backEdge = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EdgesAdjacentInAEL(in IntersectNode inode)
    {
      ref Active e1 = ref A(inode.edge1);
      return e1.nextInAEL == inode.edge2 || e1.prevInAEL == inode.edge2;
    }

    // Out-point arena ----------------------------------------------------------

    // nb: _op can be replaced by a larger array whenever an out-point is created
    // (NewOutPt), so no reference into it is held across a call that may create
    // one - the code below always re-reads through O()
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref OutPt O(int i) =>
      ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_op), i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private OutRec Rec(int outrecIdx) => _outrecList[outrecIdx];

    private int NewOutPt(Point64 pt, int outrecIdx)
    {
      int i = _opCount;
      if (i == _op.Length) GrowOutPts();
      _opCount = i + 1;
      ref OutPt op = ref O(i);
      op.pt = pt;
      op.next = i;
      op.prev = i;
      op.outrec = outrecIdx;
      op.horz = false;
      return i;
    }

    private void GrowOutPts()
    {
      OutPt[] bigger = System.Buffers.ArrayPool<OutPt>.Shared.Rent(Math.Max(64, _op.Length * 2));
      Array.Copy(_op, bigger, _opCount);
      if (_op.Length > 0) System.Buffers.ArrayPool<OutPt>.Shared.Return(_op);
      _op = bigger;
    }

    private int DuplicateOp(int op, bool insertAfter)
    {
      int result = NewOutPt(O(op).pt, O(op).outrec);
      if (insertAfter)
      {
        int next = O(op).next;
        O(result).next = next;
        O(next).prev = result;
        O(result).prev = op;
        O(op).next = result;
      }
      else
      {
        int prev = O(op).prev;
        O(result).prev = prev;
        O(prev).next = result;
        O(result).next = op;
        O(op).prev = result;
      }
      return result;
    }

    private int DisposeOutPt(int op)
    {
      int next = O(op).next, prev = O(op).prev;
      O(prev).next = next;
      O(next).prev = prev;
      return next;
    }

    private double Area(int op)
    {
      // https://en.wikipedia.org/wiki/Shoelace_formula
      double result = 0.0;
      int op2 = op;
      do
      {
        ref OutPt o = ref O(op2);
        Point64 prevPt = O(o.prev).pt;
        result += (double) (prevPt.Y + o.pt.Y) *
          (double) (prevPt.X - o.pt.X);
        op2 = o.next;
      } while (op2 != op);
      return result * 0.5;
    }

    private bool IsVerySmallTriangle(int op)
    {
      ref OutPt o = ref O(op);
      return O(o.next).next == o.prev &&
        (ClipperEngine.PtsReallyClose(O(o.prev).pt, O(o.next).pt) ||
          ClipperEngine.PtsReallyClose(o.pt, O(o.next).pt) ||
          ClipperEngine.PtsReallyClose(o.pt, O(o.prev).pt));
    }

    private bool IsValidClosedPath(int op)
    {
      return op >= 0 && O(op).next != op &&
        O(op).next != O(op).prev && !IsVerySmallTriangle(op);
    }

    private void SwapFrontBackSides(OutRec outrec)
    {
      int tmp = outrec.frontEdge;
      outrec.frontEdge = outrec.backEdge;
      outrec.backEdge = tmp;
      outrec.pts = O(outrec.pts).next;
    }

    private PointInPolygonResult PointInOpPolygon(Point64 pt, int op)
    {
      if (op == O(op).next || O(op).prev == O(op).next)
        return PointInPolygonResult.IsOutside;

      int op2 = op;
      do
      {
        if (O(op).pt.Y != pt.Y) break;
        op = O(op).next;
      } while (op != op2);
      if (O(op).pt.Y == pt.Y) // not a proper polygon
        return PointInPolygonResult.IsOutside;

      bool isAbove = O(op).pt.Y < pt.Y, startingAbove = isAbove;
      int val = 0;
      op2 = O(op).next;
      while (op2 != op)
      {
        if (isAbove)
          while (op2 != op && O(op2).pt.Y < pt.Y) op2 = O(op2).next;
        else
          while (op2 != op && O(op2).pt.Y > pt.Y) op2 = O(op2).next;
        if (op2 == op) break;

        // must have touched or crossed the pt.Y horizontal
        // and this must happen an even number of times
        Point64 curr = O(op2).pt, prev = O(O(op2).prev).pt;

        if (curr.Y == pt.Y) // touching the horizontal
        {
          if (curr.X == pt.X || (curr.Y == prev.Y &&
            (pt.X < prev.X) != (pt.X < curr.X)))
            return PointInPolygonResult.IsOn;

          op2 = O(op2).next;
          if (op2 == op) break;
          continue;
        }

        if (pt.X < curr.X && pt.X < prev.X)
        {
          // do nothing because
          // we're only interested in edges crossing on the left
        }
        else if ((pt.X > prev.X && pt.X > curr.X))
          val = 1 - val; // toggle val
        else
        {
          int i = InternalClipper.CrossProductSign(prev, curr, pt);
          if (i == 0) return PointInPolygonResult.IsOn;
          if ((i < 0) == isAbove) val = 1 - val;
        }
        isAbove = !isAbove;
        op2 = O(op2).next;
      }

      if (isAbove != startingAbove)
      {
        int i = InternalClipper.CrossProductSign(O(O(op2).prev).pt, O(op2).pt, pt);
        if (i == 0) return PointInPolygonResult.IsOn;
        if ((i < 0) == isAbove) val = 1 - val;
      }

      if (val == 0) return PointInPolygonResult.IsOutside;
      else return PointInPolygonResult.IsInside;
    }

    private Path64 GetCleanPath(int op)
    {
      Path64 result = new Path64();
      int op2 = op;
      while (O(op2).next != op &&
        ((O(op2).pt.X == O(O(op2).next).pt.X && O(op2).pt.X == O(O(op2).prev).pt.X) ||
          (O(op2).pt.Y == O(O(op2).next).pt.Y && O(op2).pt.Y == O(O(op2).prev).pt.Y))) op2 = O(op2).next;
      result.Add(O(op2).pt);
      int prevOp = op2;
      op2 = O(op2).next;
      while (op2 != op)
      {
        Point64 p = O(op2).pt, nextPt = O(O(op2).next).pt, prevPt = O(prevOp).pt;
        if ((p.X != nextPt.X || p.X != prevPt.X) &&
          (p.Y != nextPt.Y || p.Y != prevPt.Y))
        {
          result.Add(p);
          prevOp = op2;
        }
        op2 = O(op2).next;
      }
      return result;
    }

    private bool Path2ContainsPath1(int op1, int op2)
    {
      // this function accommodates rounding errors that
      // can cause path micro intersections
      PointInPolygonResult pip = PointInPolygonResult.IsOn;
      int op = op1;
      do
      {
        switch (PointInOpPolygon(O(op).pt, op2))
        {
          case PointInPolygonResult.IsOutside:
            if (pip == PointInPolygonResult.IsOutside) return false;
            pip = PointInPolygonResult.IsOutside;
            break;
          case PointInPolygonResult.IsInside:
            if (pip == PointInPolygonResult.IsInside) return true;
            pip = PointInPolygonResult.IsInside;
            break;
          default: break;
        }
        op = O(op).next;
      } while (op != op1);
      // result unclear, so try again using cleaned paths
      return InternalClipper.Path2ContainsPath1(GetCleanPath(op1), GetCleanPath(op2)); // (#973)
    }

    private bool BuildPath64(int op, bool reverse, bool isOpen, Path64 path)
    {
      if (op < 0 || O(op).next == op || (!isOpen && O(op).next == O(op).prev))
        return false;

      path.Clear();
      Point64 lastPt;
      int op2;
      if (reverse)
      {
        lastPt = O(op).pt;
        op2 = O(op).prev;
      }
      else
      {
        op = O(op).next;
        lastPt = O(op).pt;
        op2 = O(op).next;
      }
      path.Add(lastPt);

      while (op2 != op)
      {
        ref OutPt o = ref O(op2);
        if (o.pt != lastPt)
        {
          lastPt = o.pt;
          path.Add(lastPt);
        }
        op2 = reverse ? o.prev : o.next;
      }

      if (!isOpen && path.Count == 3 && IsVerySmallTriangle(op2)) return false;
      else return true;
    }

    private bool BuildPathD(int op, bool reverse, bool isOpen, PathD path, double invScale)
    {
      if (op < 0 || O(op).next == op || (!isOpen && O(op).next == O(op).prev))
        return false;

      path.Clear();
      Point64 lastPt;
      int op2;
      if (reverse)
      {
        lastPt = O(op).pt;
        op2 = O(op).prev;
      }
      else
      {
        op = O(op).next;
        lastPt = O(op).pt;
        op2 = O(op).next;
      }
      path.Add(new PointD(lastPt.X * invScale, lastPt.Y * invScale
#if USINGZ
        , lastPt.Z
#endif
      ));

      while (op2 != op)
      {
        ref OutPt o = ref O(op2);
        if (o.pt != lastPt)
        {
          lastPt = o.pt;
          path.Add(new PointD(lastPt.X * invScale, lastPt.Y * invScale
#if USINGZ
            , lastPt.Z
#endif
          ));
        }
        op2 = reverse ? o.prev : o.next;
      }
      if (path.Count == 3 && IsVerySmallTriangle(op2)) return false;
      return true;
    }

    private int AddLocalMinPoly(int e1, int e2, Point64 pt, bool isNew = false)
    {
      OutRec outrec = NewOutRec();
      ref Active ae1 = ref A(e1);
      A(e2).outrec = outrec;
      ae1.outrec = outrec;

      if (ClipperEngine.IsOpen(ae1))
      {
        outrec.owner = null;
        outrec.isOpen = true;
        if (ae1.windDx > 0)
          SetSides(outrec, e1, e2);
        else
          SetSides(outrec, e2, e1);
      }
      else
      {
        int prevHotEdge = GetPrevHotEdge(e1);
        // e.windDx is the winding direction of the **input** paths
        // and unrelated to the winding direction of output polygons.
        // Output orientation is determined by e.outrec.frontE which is
        // the ascending edge (see AddLocalMinPoly).
        if (prevHotEdge >= 0)
        {
          if (_usingPolytree)
            ClipperEngine.SetOwner(outrec, A(prevHotEdge).outrec!);
          if (IsFront(prevHotEdge) == isNew)
            SetSides(outrec, e2, e1);
          else
            SetSides(outrec, e1, e2);
        }
        else
        {
          outrec.owner = null;
          if (isNew)
            SetSides(outrec, e1, e2);
          else
            SetSides(outrec, e2, e1);
        }
      }

      int op = NewOutPt(pt, outrec.idx);
      outrec.pts = op;
      return op;
    }

    private int AddLocalMaxPoly(int e1, int e2, Point64 pt)
    {
      if (ClipperEngine.IsJoined(A(e1))) Split(e1, pt);
      if (ClipperEngine.IsJoined(A(e2))) Split(e2, pt);

      if (IsFront(e1) == IsFront(e2))
      {
        if (IsOpenEnd(A(e1)))
          SwapFrontBackSides(A(e1).outrec!);
        else if (IsOpenEnd(A(e2)))
          SwapFrontBackSides(A(e2).outrec!);
        else
        {
          _succeeded = false;
          return -1;
        }
      }

      int result = AddOutPt(e1, pt);
      if (ReferenceEquals(A(e1).outrec, A(e2).outrec))
      {
        OutRec outrec = A(e1).outrec!;
        outrec.pts = result;

        if (_usingPolytree)
        {
          int e = GetPrevHotEdge(e1);
          if (e < 0)
            outrec.owner = null;
          else
            ClipperEngine.SetOwner(outrec, A(e).outrec!);
          // nb: outRec.owner here is likely NOT the real
          // owner but this will be checked in RecursiveCheckOwners()
        }

        UncoupleOutRec(e1);
        result = outrec.pts;
        if (outrec.owner != null && outrec.owner.frontEdge < 0)
          outrec.owner = ClipperEngine.GetRealOutRec(outrec.owner);
      }
      // and to preserve the winding orientation of outrec ...
      else if (ClipperEngine.IsOpen(A(e1)))
      {
        if (A(e1).windDx < 0)
          JoinOutrecPaths(e1, e2);
        else
          JoinOutrecPaths(e2, e1);
      }
      else if (A(e1).outrec!.idx < A(e2).outrec!.idx)
        JoinOutrecPaths(e1, e2);
      else
        JoinOutrecPaths(e2, e1);
      return result;
    }

    private void JoinOutrecPaths(int e1, int e2)
    {
      // join e2 outrec path onto e1 outrec path and then delete e2 outrec path
      // pointers. (NB Only very rarely do the joining ends share the same coords.)
      // nb: e1's/e2's outrec is re-read at every step, exactly like the C++,
      // because re-assigning a front/back edge can change it along the way
      int p1st = A(e1).outrec!.pts;
      int p2st = A(e2).outrec!.pts;
      int p1end = O(p1st).next;
      int p2end = O(p2st).next;
      if (IsFront(e1))
      {
        O(p2end).prev = p1st;
        O(p1st).next = p2end;
        O(p2st).next = p1end;
        O(p1end).prev = p2st;
        A(e1).outrec!.pts = p2st;
        A(e1).outrec!.frontEdge = A(e2).outrec!.frontEdge;
        if (A(e1).outrec!.frontEdge >= 0)
          A(A(e1).outrec!.frontEdge).outrec = A(e1).outrec;
      }
      else
      {
        O(p1end).prev = p2st;
        O(p2st).next = p1end;
        O(p1st).next = p2end;
        O(p2end).prev = p1st;
        A(e1).outrec!.backEdge = A(e2).outrec!.backEdge;
        if (A(e1).outrec!.backEdge >= 0)
          A(A(e1).outrec!.backEdge).outrec = A(e1).outrec;
      }

      // after joining, the e2.OutRec must contains no vertices ...
      A(e2).outrec!.frontEdge = -1;
      A(e2).outrec!.backEdge = -1;
      A(e2).outrec!.pts = -1;

      if (IsOpenEnd(A(e1)))
      {
        A(e2).outrec!.pts = A(e1).outrec!.pts;
        A(e1).outrec!.pts = -1;
      }
      else
        ClipperEngine.SetOwner(A(e2).outrec!, A(e1).outrec!);

      // and e1 and e2 are maxima and are about to be dropped from the Actives list.
      A(e1).outrec = null;
      A(e2).outrec = null;
    }

    private OutRec NewOutRec()
    {
      OutRec result = _outrecPool.Add();
      result.idx = _outrecList.Count;
      _outrecList.Add(result);
      return result;
    }

    private int AddOutPt(int e, Point64 pt)
    {
      // Outrec.OutPts: a circular doubly-linked-list of POutPt where ...
      // op_front[.Prev]* ~~~> op_back & op_back == op_front.Next
      OutRec outrec = A(e).outrec!;
      bool toFront = e == outrec.frontEdge;
      int opFront = outrec.pts;
      int opBack = O(opFront).next;

      if (toFront)
      {
        if (pt == O(opFront).pt)
          return opFront;
      }
      else if (pt == O(opBack).pt)
        return opBack;

      int newOp = NewOutPt(pt, outrec.idx);
      O(opBack).prev = newOp;
      O(newOp).prev = opFront;
      O(newOp).next = opBack;
      O(opFront).next = newOp;
      if (toFront) outrec.pts = newOp;
      return newOp;
    }

    private void CleanCollinear(OutRec? outrec)
    {
      outrec = ClipperEngine.GetRealOutRec(outrec);
      if (outrec == null || outrec.isOpen) return;
      if (!IsValidClosedPath(outrec.pts))
      {
        ClipperEngine.DisposeOutPts(outrec);
        return;
      }

      int startOp = outrec.pts, op2 = startOp;
      for (; ; )
      {
        Point64 prevPt = O(O(op2).prev).pt, pt = O(op2).pt, nextPt = O(O(op2).next).pt;
        // NB if preserveCollinear == true, then only remove 180 deg. spikes
        if (InternalClipper.IsCollinear(prevPt, pt, nextPt) &&
          (pt == prevPt ||
            pt == nextPt || !_preserveCollinear ||
            InternalClipper.DotProduct(prevPt, pt, nextPt) < 0))
        {
          if (op2 == outrec.pts) outrec.pts = O(op2).prev;

          op2 = DisposeOutPt(op2);
          if (!IsValidClosedPath(op2))
          {
            ClipperEngine.DisposeOutPts(outrec);
            return;
          }
          startOp = op2;
          continue;
        }
        op2 = O(op2).next;
        if (op2 == startOp) break;
      }
      FixSelfIntersects(outrec);
    }

    /// <summary>
    /// The exact shoelace sum of the ring FixSelfIntersects is working on, kept
    /// up to date across splits so a split costs O(1) instead of an O(n) area
    /// pass (docs/optimization-plan.md 5.1). Every term (y1 + y2) * (x1 - x2) is
    /// an integer, so the sum is exact and order independent in Int128, which
    /// is what makes the incremental update possible at all.
    /// </summary>
    private struct RingArea
    {
      public bool valid;      // initialised for the current ring
      public bool usable;     // coordinates small enough for the exact bookkeeping
      public Int128 sum;      // exact sum of the terms (twice the signed area)
      public UInt128 absSum;  // exact sum of |term|, for the rounding error bound
      public int count;       // vertices in the ring
    }

    // nb: |coordinate| < 2^52 keeps every term below 2^106 in magnitude, so up to
    // 2^20 terms cannot overflow Int128 (larger inputs use the double pass only)
    private const long RingAreaCoordLimit = 1L << 52;
    private const int RingAreaMaxCount = 1 << 20;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RingAreaCoordOk(Point64 pt) =>
      pt.X < RingAreaCoordLimit && pt.X > -RingAreaCoordLimit &&
      pt.Y < RingAreaCoordLimit && pt.Y > -RingAreaCoordLimit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Int128 RingTerm(Point64 prev, Point64 curr) =>
      (Int128) (prev.Y + curr.Y) * (prev.X - curr.X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UInt128 AbsTerm(Int128 t) => (UInt128) (t < 0 ? -t : t);

    private void InitRingArea(ref RingArea ra, int start)
    {
      ra.valid = true;
      ra.usable = false;
      Int128 sum = 0;
      UInt128 absSum = 0;
      int count = 0;
      int op = start;
      do
      {
        Point64 pt = O(op).pt;
        if (!RingAreaCoordOk(pt) || ++count > RingAreaMaxCount) return;
        Int128 t = RingTerm(O(O(op).prev).pt, pt);
        sum += t;
        absSum += AbsTerm(t);
        op = O(op).next;
      } while (op != start);
      ra.sum = sum;
      ra.absSum = absSum;
      ra.count = count;
      ra.usable = true;
    }

    /// <summary>
    /// Decides the three questions DoSplitOp asks about area1 (the double area
    /// the C++ computes) from the exact area, and returns false - so the caller
    /// computes area1 exactly the C++ way - whenever the double result could
    /// possibly land on the other side of any of them. The double sum differs
    /// from the exact one by at most n * u * sum|term| (every term is converted
    /// exactly - its factors are below 2^53 - and rounded once, then n roundings
    /// accumulate), so outside that band the answers are provably the C++ ones.
    /// </summary>
    private static bool DecideFromExactArea(in RingArea ra, double absArea2,
      out bool lessThan2, out bool positive, out bool area2Larger)
    {
      lessThan2 = positive = area2Larger = false;
      double a = (double) ra.sum * 0.5;
      // 2.3e-16 is twice the unit roundoff: a safety factor over the bound above,
      // which also covers the conversions of 'sum' and 'absSum' to double
      double e = (ra.count + 4) * 2.3e-16 * 0.5 * (double) ra.absSum + Math.Abs(a) * 2.3e-16;
      double absA = Math.Abs(a);
      if (absA <= e) return false; // sign (and |area1|) not certain
      if (absA + e < 2) lessThan2 = true;
      else if (absA - e < 2) return false;
      positive = a > 0;
      if (absArea2 > absA + e) area2Larger = true;
      else if (absArea2 >= absA - e) return false;
      return true;
    }

    private void DoSplitOp(OutRec outrec, int splitOp, ref RingArea ra)
    {
      // splitOp.prev -> splitOp &&
      // splitOp.next -> splitOp.next.next are intersecting
      int prevOp = O(splitOp).prev;
      int splitNext = O(splitOp).next;
      int nextNextOp = O(splitNext).next;
      outrec.pts = prevOp;
      Point64 prevPt = O(prevOp).pt, splitPt = O(splitOp).pt,
        splitNextPt = O(splitNext).pt, nextNextPt = O(nextNextOp).pt;

      Point64 ip;
      InternalClipper.GetLineIntersectPt(prevPt, splitPt,
        splitNextPt, nextNextPt, out ip);

#if USINGZ
      if (_zCallback != null) _zCallback(prevPt, splitPt,
        splitNextPt, nextNextPt, ref ip);
#endif
      double area2 = ClipperEngine.AreaTriangle(ip, splitPt, splitNextPt);
      double absArea2 = Math.Abs(area2);

      // nb: area1 (the path's area before splitting) is only ever used for the
      // three decisions below, so it is decided from the exact incremental area
      // when that is certain, and computed in full (as the C++ does) otherwise
      if (!ra.valid) InitRingArea(ref ra, prevOp);
      bool area1LessThan2, area1Positive, area2Larger;
      if (!ra.usable || !DecideFromExactArea(ra, absArea2,
        out area1LessThan2, out area1Positive, out area2Larger))
      {
        double area1 = Area(outrec.pts);
        double absArea1 = Math.Abs(area1);
        area1LessThan2 = absArea1 < 2;
        area1Positive = area1 > 0;
        area2Larger = absArea2 > absArea1;
      }
      if (area1LessThan2)
      {
        ClipperEngine.DisposeOutPts(outrec);
        return;
      }

      // keep the exact ring sum in step with the relinking below
      if (ra.usable)
      {
        Int128 t1 = RingTerm(prevPt, splitPt);
        Int128 t2 = RingTerm(splitPt, splitNextPt);
        Int128 t3 = RingTerm(splitNextPt, nextNextPt);
        ra.sum -= t1 + t2 + t3;
        ra.absSum -= AbsTerm(t1) + AbsTerm(t2) + AbsTerm(t3);
        if (ip == prevPt || ip == nextNextPt)
        {
          Int128 t = RingTerm(prevPt, nextNextPt);
          ra.sum += t;
          ra.absSum += AbsTerm(t);
          ra.count -= 2;
        }
        else if (RingAreaCoordOk(ip))
        {
          Int128 ta = RingTerm(prevPt, ip), tb = RingTerm(ip, nextNextPt);
          ra.sum += ta + tb;
          ra.absSum += AbsTerm(ta) + AbsTerm(tb);
          ra.count -= 1;
        }
        else ra.usable = false;
      }

      // de-link splitOp and splitOp.next from the path
      // while inserting the intersection point
      if (ip == prevPt || ip == nextNextPt)
      {
        O(nextNextOp).prev = prevOp;
        O(prevOp).next = nextNextOp;
      }
      else
      {
        int newOp2 = NewOutPt(ip, O(prevOp).outrec);
        O(newOp2).prev = prevOp;
        O(newOp2).next = nextNextOp;
        O(nextNextOp).prev = newOp2;
        O(prevOp).next = newOp2;
      }

      // area1 is the path's area *before* splitting, whereas area2 is
      // the area of the triangle containing splitOp & splitOp.next.
      // So the only way for these areas to have the same sign is if
      // the split triangle is larger than the path containing prevOp or
      // if there's more than one self-intersection.
      if (absArea2 >= 1 &&
        (area2Larger || (area2 > 0) == area1Positive))
      {
        OutRec newOr = NewOutRec();
        newOr.owner = outrec.owner;

        O(splitOp).outrec = newOr.idx;
        O(splitNext).outrec = newOr.idx;
        int newOp = NewOutPt(ip, newOr.idx);
        O(newOp).prev = splitNext;
        O(newOp).next = splitOp;
        newOr.pts = newOp;
        O(splitOp).prev = newOp;
        O(splitNext).next = newOp;

        if (_usingPolytree)
        {
          if (Path2ContainsPath1(prevOp, newOp))
          {
            newOr.splits = new List<OutRec> { outrec };
          }
          else
          {
            if (outrec.splits == null) outrec.splits = new List<OutRec>();
            outrec.splits.Add(newOr);
          }
        }
      }
      // else: splitOp & splitOp.next are simply bypassed
    }

    private void FixSelfIntersects(OutRec outrec)
    {
      int op2 = outrec.pts;
      if (O(op2).prev == O(O(op2).next).next)
        return; // because triangles can't self-intersect
      RingArea ra = default; // initialised on the first split
      for (; ; )
      {
        int next = O(op2).next;
        if (InternalClipper.SegsIntersect(O(O(op2).prev).pt,
          O(op2).pt, O(next).pt, O(O(next).next).pt))
        {
          if (op2 == outrec.pts || next == outrec.pts)
            outrec.pts = O(outrec.pts).prev;
          DoSplitOp(outrec, op2, ref ra);
          if (outrec.pts < 0) break;
          op2 = outrec.pts;
          if (O(op2).prev == O(O(op2).next).next)
            break; // again, because triangles can't self-intersect
          continue;
        }
        else
          op2 = next;

        if (op2 == outrec.pts) break;
      }
    }

    private int StartOpenPath(int e, Point64 pt)
    {
      OutRec outrec = NewOutRec();
      outrec.isOpen = true;

      if (A(e).windDx > 0)
      {
        outrec.frontEdge = e;
        outrec.backEdge = -1;
      }
      else
      {
        outrec.frontEdge = -1;
        outrec.backEdge = e;
      }

      A(e).outrec = outrec;

      int op = NewOutPt(pt, outrec.idx);
      outrec.pts = op;
      return op;
    }

    private void TrimHorz(ref Active horzEdge, bool preserveCollinear)
    {
      bool wasTrimmed = false;
      Point64 pt = Vx(NextVertex(horzEdge)).pt;
      while (pt.Y == horzEdge.top.Y)
      {
        // always trim 180 deg. spikes (in closed paths)
        // but otherwise break if preserveCollinear = true
        if (preserveCollinear &&
          ((pt.X < horzEdge.top.X) != (horzEdge.bot.X < horzEdge.top.X)))
          break;

        horzEdge.vertexTop = NextVertex(horzEdge);
        horzEdge.top = pt;
        wasTrimmed = true;
        if (IsMaxima(horzEdge)) break;
        pt = Vx(NextVertex(horzEdge)).pt;
      }

      if (wasTrimmed) ClipperEngine.SetDx(ref horzEdge); // +/-infinity
    }

    private void UpdateEdgeIntoAEL(int ei)
    {
      ref Active e = ref A(ei);
      e.bot = e.top;
      e.vertexTop = NextVertex(e);
      e.top = Vx(e.vertexTop).pt;
      e.currX = e.bot.X;
      ClipperEngine.SetDx(ref e);

      if (ClipperEngine.IsJoined(e)) Split(ei, e.bot);

      if (ClipperEngine.IsHorizontal(e))
      {
        if (!ClipperEngine.IsOpen(e)) TrimHorz(ref e, _preserveCollinear);
        return;
      }

      InsertScanline(e.top.Y);
      CheckJoinLeft(ei, e.bot);
      CheckJoinRight(ei, e.bot, true); // (#500)
    }

    private int FindEdgeWithMatchingLocMin(int e)
    {
      ref Active ae = ref A(e);
      int result = ae.nextInAEL;
      while (result >= 0)
      {
        ref Active r = ref A(result);
        if (ReferenceEquals(r.localMin, ae.localMin)) return result;
        else if (!ClipperEngine.IsHorizontal(r) && ae.bot != r.bot) result = -1;
        else result = r.nextInAEL;
      }
      result = ae.prevInAEL;
      while (result >= 0)
      {
        ref Active r = ref A(result);
        if (ReferenceEquals(r.localMin, ae.localMin)) return result;
        else if (!ClipperEngine.IsHorizontal(r) && ae.bot != r.bot) return -1;
        else result = r.prevInAEL;
      }
      return result;
    }

    private void IntersectEdges(int e1i, int e2i, Point64 pt)
    {
      ref Active e1 = ref A(e1i);
      ref Active e2 = ref A(e2i);

      // MANAGE OPEN PATH INTERSECTIONS SEPARATELY ...
      if (_hasOpenPaths && (ClipperEngine.IsOpen(e1) || ClipperEngine.IsOpen(e2)))
      {
        if (ClipperEngine.IsOpen(e1) && ClipperEngine.IsOpen(e2)) return;
        int edgeO, edgeC;
        if (ClipperEngine.IsOpen(e1))
        {
          edgeO = e1i;
          edgeC = e2i;
        }
        else
        {
          edgeO = e2i;
          edgeC = e1i;
        }
        if (ClipperEngine.IsJoined(A(edgeC))) Split(edgeC, pt); // needed for safety

        if (Math.Abs(A(edgeC).windCount) != 1) return;
        switch (_cliptype)
        {
          case ClipType.Union:
            if (!ClipperEngine.IsHotEdge(A(edgeC))) return;
            break;
          default:
            if (A(edgeC).localMin!.polytype == PathType.Subject)
              return;
            break;
        }

        switch (_fillrule)
        {
          case FillRule.Positive:
            if (A(edgeC).windCount != 1) return;
            break;
          case FillRule.Negative:
            if (A(edgeC).windCount != -1) return;
            break;
          default:
            if (Math.Abs(A(edgeC).windCount) != 1) return;
            break;
        }

#if USINGZ
        int openResultOp;
#endif
        // toggle contribution ...
        if (ClipperEngine.IsHotEdge(A(edgeO)))
        {
#if USINGZ
          openResultOp = AddOutPt(edgeO, pt);
#else
          AddOutPt(edgeO, pt);
#endif
          if (IsFront(edgeO)) A(edgeO).outrec!.frontEdge = -1;
          else A(edgeO).outrec!.backEdge = -1;
          A(edgeO).outrec = null;
        }

        // horizontal edges can pass under open paths at a LocMins
        else if (pt == Vx(A(edgeO).localMin!.vertex).pt &&
          !IsOpenEnd(A(edgeO).localMin!.vertex))
        {
          // find the other side of the LocMin and
          // if it's 'hot' join up with it ...
          int e3 = FindEdgeWithMatchingLocMin(edgeO);
          if (e3 >= 0 && ClipperEngine.IsHotEdge(A(e3)))
          {
            A(edgeO).outrec = A(e3).outrec;
            if (A(edgeO).windDx > 0)
              SetSides(A(e3).outrec!, edgeO, e3);
            else
              SetSides(A(e3).outrec!, e3, edgeO);
            return;
          }
          else
#if USINGZ
            openResultOp = StartOpenPath(edgeO, pt);
#else
            StartOpenPath(edgeO, pt);
#endif
        }
        else
#if USINGZ
          openResultOp = StartOpenPath(edgeO, pt);
#else
          StartOpenPath(edgeO, pt);
#endif

#if USINGZ
        if (_zCallback != null) SetZ(A(edgeO), A(edgeC), ref O(openResultOp).pt);
#endif
        return;
      } // end of an open path intersection

      // MANAGING CLOSED PATHS FROM HERE ON

      if (ClipperEngine.IsJoined(e1)) Split(e1i, pt);
      if (ClipperEngine.IsJoined(e2)) Split(e2i, pt);

      // UPDATE WINDING COUNTS...

      int oldE1WindCnt, oldE2WindCnt;
      if (e1.localMin!.polytype == e2.localMin!.polytype)
      {
        if (_fillrule == FillRule.EvenOdd)
        {
          oldE1WindCnt = e1.windCount;
          e1.windCount = e2.windCount;
          e2.windCount = oldE1WindCnt;
        }
        else
        {
          if (e1.windCount + e2.windDx == 0)
            e1.windCount = -e1.windCount;
          else
            e1.windCount += e2.windDx;
          if (e2.windCount - e1.windDx == 0)
            e2.windCount = -e2.windCount;
          else
            e2.windCount -= e1.windDx;
        }
      }
      else
      {
        if (_fillrule != FillRule.EvenOdd)
        {
          e1.windCount2 += e2.windDx;
          e2.windCount2 -= e1.windDx;
        }
        else
        {
          e1.windCount2 = (e1.windCount2 == 0 ? 1 : 0);
          e2.windCount2 = (e2.windCount2 == 0 ? 1 : 0);
        }
      }

      switch (_fillrule)
      {
        case FillRule.EvenOdd:
        case FillRule.NonZero:
          oldE1WindCnt = Math.Abs(e1.windCount);
          oldE2WindCnt = Math.Abs(e2.windCount);
          break;
        default:
          if (_fillrule == _fillpos)
          {
            oldE1WindCnt = e1.windCount;
            oldE2WindCnt = e2.windCount;
          }
          else
          {
            oldE1WindCnt = -e1.windCount;
            oldE2WindCnt = -e2.windCount;
          }
          break;
      }

      bool e1WindCntIn01 = oldE1WindCnt == 0 || oldE1WindCnt == 1;
      bool e2WindCntIn01 = oldE2WindCnt == 0 || oldE2WindCnt == 1;

      if ((!ClipperEngine.IsHotEdge(e1) && !e1WindCntIn01) ||
        (!ClipperEngine.IsHotEdge(e2) && !e2WindCntIn01))
        return;

      // NOW PROCESS THE INTERSECTION ...
#if USINGZ
      int resultOp = -1;
#endif
      // if both edges are 'hot' ...
      if (ClipperEngine.IsHotEdge(e1) && ClipperEngine.IsHotEdge(e2))
      {
        if ((oldE1WindCnt != 0 && oldE1WindCnt != 1) || (oldE2WindCnt != 0 && oldE2WindCnt != 1) ||
          (e1.localMin.polytype != e2.localMin.polytype && _cliptype != ClipType.Xor))
        {
#if USINGZ
          resultOp = AddLocalMaxPoly(e1i, e2i, pt);
          if (_zCallback != null && resultOp >= 0) SetZ(e1, e2, ref O(resultOp).pt);
#else
          AddLocalMaxPoly(e1i, e2i, pt);
#endif
        }
        else if (IsFront(e1i) || ReferenceEquals(e1.outrec, e2.outrec))
        {
          // this 'else if' condition isn't strictly needed but
          // it's sensible to split polygons that only touch at
          // a common vertex (not at common edges).
#if USINGZ
          resultOp = AddLocalMaxPoly(e1i, e2i, pt);
          int op2 = AddLocalMinPoly(e1i, e2i, pt);
          if (_zCallback != null && resultOp >= 0) SetZ(e1, e2, ref O(resultOp).pt);
          if (_zCallback != null) SetZ(e1, e2, ref O(op2).pt);
#else
          AddLocalMaxPoly(e1i, e2i, pt);
          AddLocalMinPoly(e1i, e2i, pt);
#endif
        }
        else
        {
#if USINGZ
          resultOp = AddOutPt(e1i, pt);
          int op2 = AddOutPt(e2i, pt);
          if (_zCallback != null)
          {
            SetZ(e1, e2, ref O(resultOp).pt);
            SetZ(e1, e2, ref O(op2).pt);
          }
#else
          AddOutPt(e1i, pt);
          AddOutPt(e2i, pt);
#endif
          SwapOutrecs(e1i, e2i);
        }
      }
      else if (ClipperEngine.IsHotEdge(e1))
      {
#if USINGZ
        resultOp = AddOutPt(e1i, pt);
        if (_zCallback != null) SetZ(e1, e2, ref O(resultOp).pt);
#else
        AddOutPt(e1i, pt);
#endif
        SwapOutrecs(e1i, e2i);
      }
      else if (ClipperEngine.IsHotEdge(e2))
      {
#if USINGZ
        resultOp = AddOutPt(e2i, pt);
        if (_zCallback != null) SetZ(e1, e2, ref O(resultOp).pt);
#else
        AddOutPt(e2i, pt);
#endif
        SwapOutrecs(e1i, e2i);
      }
      else
      {
        long e1Wc2, e2Wc2;
        switch (_fillrule)
        {
          case FillRule.EvenOdd:
          case FillRule.NonZero:
            e1Wc2 = Math.Abs(e1.windCount2);
            e2Wc2 = Math.Abs(e2.windCount2);
            break;
          default:
            if (_fillrule == _fillpos)
            {
              e1Wc2 = e1.windCount2;
              e2Wc2 = e2.windCount2;
            }
            else
            {
              e1Wc2 = -e1.windCount2;
              e2Wc2 = -e2.windCount2;
            }
            break;
        }

        if (!ClipperEngine.IsSamePolyType(e1, e2))
        {
#if USINGZ
          resultOp = AddLocalMinPoly(e1i, e2i, pt, false);
          if (_zCallback != null) SetZ(e1, e2, ref O(resultOp).pt);
#else
          AddLocalMinPoly(e1i, e2i, pt, false);
#endif
        }
        else if (oldE1WindCnt == 1 && oldE2WindCnt == 1)
        {
#if USINGZ
          resultOp = -1;
#endif
          switch (_cliptype)
          {
            case ClipType.Union:
              if (e1Wc2 <= 0 && e2Wc2 <= 0)
#if USINGZ
                resultOp = AddLocalMinPoly(e1i, e2i, pt, false);
#else
                AddLocalMinPoly(e1i, e2i, pt, false);
#endif
              break;
            case ClipType.Difference:
              if (((ClipperEngine.GetPolyType(e1) == PathType.Clip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                ((ClipperEngine.GetPolyType(e1) == PathType.Subject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
              {
#if USINGZ
                resultOp = AddLocalMinPoly(e1i, e2i, pt, false);
#else
                AddLocalMinPoly(e1i, e2i, pt, false);
#endif
              }
              break;
            case ClipType.Xor:
#if USINGZ
              resultOp = AddLocalMinPoly(e1i, e2i, pt, false);
#else
              AddLocalMinPoly(e1i, e2i, pt, false);
#endif
              break;
            default:
              if (e1Wc2 > 0 && e2Wc2 > 0)
#if USINGZ
                resultOp = AddLocalMinPoly(e1i, e2i, pt, false);
#else
                AddLocalMinPoly(e1i, e2i, pt, false);
#endif
              break;
          }
#if USINGZ
          if (resultOp >= 0 && _zCallback != null) SetZ(e1, e2, ref O(resultOp).pt);
#endif
        }
      }
    }

    private void DeleteFromAEL(int e)
    {
      int prev = A(e).prevInAEL;
      int next = A(e).nextInAEL;
      if (prev < 0 && next < 0 && e != _actives) return; // already deleted
      if (prev >= 0)
        A(prev).nextInAEL = next;
      else
        _actives = next;
      if (next >= 0) A(next).prevInAEL = prev;
      // nb: the arena slot is simply abandoned (it is recycled by the next operation)
    }

    /// <summary>Returns true when the edges are still in X order at topY.</summary>
    private bool AdjustCurrXAndCopyToSEL(long topY)
    {
      int e = _actives;
      _sel = e;
      bool ordered = true;
      long lastX = long.MinValue;
      while (e >= 0)
      {
        ref Active ae = ref A(e);
        ae.prevInSEL = ae.prevInAEL;
        ae.nextInSEL = ae.nextInAEL;
        ae.jump = ae.nextInSEL;
        // it is safe to ignore 'joined' edges here because
        // if necessary they will be split in IntersectEdges()
        long x = ClipperEngine.TopX(ae, topY);
        ae.currX = x;
        if (x < lastX) ordered = false;
        lastX = x;
        e = ae.nextInAEL;
      }
      return ordered;
    }

    private bool ExecuteInternal(ClipType ct, FillRule fillRule, bool usePolytrees)
    {
      _cliptype = ct;
      _fillrule = fillRule;
      _usingPolytree = usePolytrees;
      Reset();
      if (ct == ClipType.NoClip || !PopScanline(out long y)) return true;

      while (_succeeded)
      {
        InsertLocalMinimaIntoAEL(y);
        while (PopHorz(out int e)) DoHorizontal(e);
        if (_horzSegList.Count > 0)
        {
          ConvertHorzSegsToJoins();
          _horzSegList.Clear();
        }
        _botY = y; // bot_y_ == bottom of scanbeam
        if (!PopScanline(out y)) break; // y new top of scanbeam
        DoIntersections(y);
        DoTopOfScanbeam(y);
        while (PopHorz(out int e2)) DoHorizontal(e2);
      }
      if (_succeeded) ProcessHorzJoins();
      return _succeeded;
    }

    private void FixOutRecPts(OutRec outrec)
    {
      int op = outrec.pts;
      int idx = outrec.idx;
      do
      {
        O(op).outrec = idx;
        op = O(op).next;
      } while (op != outrec.pts);
    }

    private bool SetHorzSegHeadingForward(ref HorzSegment hs, int opP, int opN)
    {
      long xP = O(opP).pt.X, xN = O(opN).pt.X;
      if (xP == xN) return false;
      if (xP < xN)
      {
        hs.leftOp = opP;
        hs.rightOp = opN;
        hs.leftToRight = true;
      }
      else
      {
        hs.leftOp = opN;
        hs.rightOp = opP;
        hs.leftToRight = false;
      }
      return true;
    }

    private bool UpdateHorzSegment(ref HorzSegment hs)
    {
      int op = hs.leftOp;
      OutRec? outrec = ClipperEngine.GetRealOutRec(Rec(O(op).outrec));
      bool outrecHasEdges = outrec!.frontEdge >= 0;
      long currY = O(op).pt.Y;
      int opP = op, opN = op;
      if (outrecHasEdges)
      {
        int opA = outrec.pts, opZ = O(opA).next;
        while (opP != opZ && O(O(opP).prev).pt.Y == currY)
          opP = O(opP).prev;
        while (opN != opA && O(O(opN).next).pt.Y == currY)
          opN = O(opN).next;
      }
      else
      {
        while (O(opP).prev != opN && O(O(opP).prev).pt.Y == currY)
          opP = O(opP).prev;
        while (O(opN).next != opP && O(O(opN).next).pt.Y == currY)
          opN = O(opN).next;
      }
      bool result =
        SetHorzSegHeadingForward(ref hs, opP, opN) &&
        !O(hs.leftOp).horz;

      if (result)
        O(hs.leftOp).horz = true;
      else
        hs.rightOp = -1; // (for sorting)
      return result;
    }

    private readonly struct HorzSegComparer : ClipperEngine.IStableComparer<HorzSegment>
    {
      private readonly OutPt[] _op;
      public HorzSegComparer(OutPt[] op) { _op = op; }

      public int Compare(in HorzSegment hs1, in HorzSegment hs2)
      {
        if (hs1.rightOp < 0 || hs2.rightOp < 0) return (hs1.rightOp >= 0) ? -1 : 1;
        return _op[hs1.leftOp].pt.X.CompareTo(_op[hs2.leftOp].pt.X);
      }
    }

    private void ConvertHorzSegsToJoins()
    {
      int j = 0;
      Span<HorzSegment> segs = CollectionsMarshal.AsSpan(_horzSegList);
      for (int k = 0; k < segs.Length; k++)
        if (UpdateHorzSegment(ref segs[k])) j++;
      if (j < 2) return;

      // nb: the C++ std::stable_sort places segments with a null right_op *first*
      // (see HorzSegSorter), so 'j' updated segments head the list
      ClipperEngine.StableSort(segs, ref _horzSortBuffer, new HorzSegComparer(_op));

      int hsEnd = j;
      int hsEnd1 = hsEnd - 1;

      for (int i1 = 0; i1 < hsEnd1; i1++)
      {
        ref HorzSegment hs1 = ref segs[i1];
        for (int i2 = i1 + 1; i2 < hsEnd; i2++)
        {
          ref HorzSegment hs2 = ref segs[i2];
          if ((O(hs2.leftOp).pt.X >= O(hs1.rightOp).pt.X) ||
            (hs2.leftToRight == hs1.leftToRight) ||
            (O(hs2.rightOp).pt.X <= O(hs1.leftOp).pt.X)) continue;
          long currY = O(hs1.leftOp).pt.Y;
          if (hs1.leftToRight)
          {
            while (O(O(hs1.leftOp).next).pt.Y == currY &&
              O(O(hs1.leftOp).next).pt.X <= O(hs2.leftOp).pt.X)
              hs1.leftOp = O(hs1.leftOp).next;
            while (O(O(hs2.leftOp).prev).pt.Y == currY &&
              O(O(hs2.leftOp).prev).pt.X <= O(hs1.leftOp).pt.X)
              hs2.leftOp = O(hs2.leftOp).prev;
            int op1 = DuplicateOp(hs1.leftOp, true);
            int op2 = DuplicateOp(hs2.leftOp, false);
            _horzJoinList.Add(new HorzJoin(op1, op2));
          }
          else
          {
            while (O(O(hs1.leftOp).prev).pt.Y == currY &&
              O(O(hs1.leftOp).prev).pt.X <= O(hs2.leftOp).pt.X)
              hs1.leftOp = O(hs1.leftOp).prev;
            while (O(O(hs2.leftOp).next).pt.Y == currY &&
              O(O(hs2.leftOp).next).pt.X <= O(hs1.leftOp).pt.X)
              hs2.leftOp = O(hs2.leftOp).next;
            int op1 = DuplicateOp(hs2.leftOp, true);
            int op2 = DuplicateOp(hs1.leftOp, false);
            _horzJoinList.Add(new HorzJoin(op1, op2));
          }
        }
      }
    }

    private static void MoveSplits(OutRec fromOr, OutRec toOr)
    {
      if (toOr.splits == null) toOr.splits = new List<OutRec>();
      foreach (OutRec or in fromOr.splits!)
        if (!ReferenceEquals(toOr, or)) // #987
          toOr.splits.Add(or);
      fromOr.splits!.Clear();
    }

    private void ProcessHorzJoins()
    {
      foreach (HorzJoin j in _horzJoinList)
      {
        OutRec? or1 = ClipperEngine.GetRealOutRec(Rec(O(j.op1).outrec));
        OutRec? or2 = ClipperEngine.GetRealOutRec(Rec(O(j.op2).outrec));

        int op1b = O(j.op1).next;
        int op2b = O(j.op2).prev;
        O(j.op1).next = j.op2;
        O(j.op2).prev = j.op1;
        O(op1b).prev = op2b;
        O(op2b).next = op1b;

        if (ReferenceEquals(or1, or2)) // 'join' is really a split
        {
          or2 = NewOutRec();
          or2.pts = op1b;
          FixOutRecPts(or2);

          // if or1->pts has moved to or2 then update or1->pts!!
          if (O(or1!.pts).outrec == or2.idx)
          {
            or1.pts = j.op1;
            O(or1.pts).outrec = or1.idx;
          }

          if (_usingPolytree) // #498, #520, #584, D#576, #618
          {
            if (Path2ContainsPath1(or1.pts, or2.pts))
            {
              // swap or1's & or2's pts
              int tmp = or1.pts;
              or1.pts = or2.pts;
              or2.pts = tmp;
              FixOutRecPts(or1);
              FixOutRecPts(or2);
              // or2 is now inside or1
              or2.owner = or1;
            }
            else if (Path2ContainsPath1(or2.pts, or1.pts))
            {
              or2.owner = or1;
            }
            else
              or2.owner = or1.owner;

            if (or1.splits == null) or1.splits = new List<OutRec>();
            or1.splits.Add(or2);
          }
          else
            or2.owner = or1;
        }
        else // joining, not splitting
        {
          or2!.pts = -1;
          if (_usingPolytree)
          {
            ClipperEngine.SetOwner(or2, or1!);
            if (or2.splits != null)
              MoveSplits(or2, or1!); // #618
          }
          else
            or2.owner = or1;
        }
      }
    }

    private void DoIntersections(long topY)
    {
      if (BuildIntersectList(topY))
      {
        ProcessIntersectList();
        _intersectNodes.Clear();
      }
    }

    private void AddNewIntersectNode(int e1i, int e2i, long topY)
    {
      ref Active e1 = ref A(e1i);
      ref Active e2 = ref A(e2i);
      Point64 ip;
      if (!InternalClipper.GetLineIntersectPt(e1.bot, e1.top, e2.bot, e2.top, out ip))
        ip = new Point64(e1.currX, topY); // parallel edges

      // rounding errors can occasionally place the calculated intersection
      // point either below or above the scanbeam, so check and correct ...
      if (ip.Y > _botY || ip.Y < topY)
      {
        double absDx1 = Math.Abs(e1.dx);
        double absDx2 = Math.Abs(e2.dx);
        if (absDx1 > 100 && absDx2 > 100)
        {
          if (absDx1 > absDx2)
            ip = InternalClipper.GetClosestPointOnSegment(ip, e1.bot, e1.top);
          else
            ip = InternalClipper.GetClosestPointOnSegment(ip, e2.bot, e2.top);
        }
        else if (absDx1 > 100)
          ip = InternalClipper.GetClosestPointOnSegment(ip, e1.bot, e1.top);
        else if (absDx2 > 100)
          ip = InternalClipper.GetClosestPointOnSegment(ip, e2.bot, e2.top);
        else
        {
          if (ip.Y < topY) ip.Y = topY;
          else ip.Y = _botY;
          if (absDx1 < absDx2) ip.X = ClipperEngine.TopX(e1, ip.Y);
          else ip.X = ClipperEngine.TopX(e2, ip.Y);
        }
      }
      _intersectNodes.Add(new IntersectNode(e1i, e2i, ip));
    }

    private bool BuildIntersectList(long topY)
    {
      if (_actives < 0 || A(_actives).nextInAEL < 0) return false;

      // Calculate edge positions at the top of the current scanbeam, and from this
      // we will determine the intersections required to reach these new positions.
      // nb: when no edge has overtaken its left neighbour the merge sort below
      // would find no inversion - so it would neither add a node nor relink a
      // single edge - and the (log n) merge passes can be skipped outright
      if (AdjustCurrXAndCopyToSEL(topY)) return false;
      // Find all edge intersections in the current scanbeam using a stable merge
      // sort that ensures only adjacent edges are intersecting. Intersect info is
      // stored in intersect nodes ready to be processed in ProcessIntersectList.
      // Re merge sorts see https://stackoverflow.com/a/46319131/359538

      int left = _sel, right, lEnd, rEnd, currBase, tmp;

      while (left >= 0 && A(left).jump >= 0)
      {
        int prevBase = -1;
        while (left >= 0 && A(left).jump >= 0)
        {
          currBase = left;
          right = A(left).jump;
          lEnd = right;
          rEnd = A(right).jump;
          A(left).jump = rEnd;
          while (left != lEnd && right != rEnd)
          {
            if (A(right).currX < A(left).currX)
            {
              tmp = A(right).prevInSEL;
              for (; ; )
              {
                AddNewIntersectNode(tmp, right, topY);
                if (tmp == left) break;
                tmp = A(tmp).prevInSEL;
              }

              tmp = right;
              right = ExtractFromSEL(tmp);
              lEnd = right;
              Insert1Before2InSEL(tmp, left);
              if (left == currBase)
              {
                currBase = tmp;
                A(currBase).jump = rEnd;
                if (prevBase < 0) _sel = currBase;
                else A(prevBase).jump = currBase;
              }
            }
            else left = A(left).nextInSEL;
          }
          prevBase = currBase;
          left = rEnd;
        }
        left = _sel;
      }
      return _intersectNodes.Count > 0;
    }

    private void ProcessIntersectList()
    {
      // We now have a list of intersections required so that edges will be
      // correctly positioned at the top of the scanbeam. However, it's important
      // that edge intersections are processed from the bottom up, but it's also
      // crucial that intersections only occur between adjacent edges.

      // First we do a quicksort so intersections proceed in a bottom up order ...
      // nb: the nodes hold no object references, so the sort moves plain data
      Span<IntersectNode> nodes = CollectionsMarshal.AsSpan(_intersectNodes);
      IntroSort.Sort(nodes, new IntersectNodeLess());
      // Now as we process these intersections, we must sometimes adjust the order
      // to ensure that intersecting edges are always adjacent ...

      for (int i = 0; i < nodes.Length; i++)
      {
        if (!EdgesAdjacentInAEL(nodes[i]))
        {
          int ii = i + 1;
          while (ii < nodes.Length && !EdgesAdjacentInAEL(nodes[ii])) ii++;
          if (ii >= nodes.Length) continue;
          (nodes[i], nodes[ii]) = (nodes[ii], nodes[i]);
        }

        IntersectNode node = nodes[i];
        IntersectEdges(node.edge1, node.edge2, node.pt);
        SwapPositionsInAEL(node.edge1, node.edge2);

        A(node.edge1).currX = node.pt.X;
        A(node.edge2).currX = node.pt.X;
        CheckJoinLeft(node.edge2, node.pt, true);
        CheckJoinRight(node.edge1, node.pt, true);
      }
    }

    private void SwapPositionsInAEL(int e1, int e2)
    {
      // preconditon: e1 must be immediately to the left of e2
      ref Active a1 = ref A(e1);
      ref Active a2 = ref A(e2);
      int next = a2.nextInAEL;
      if (next >= 0) A(next).prevInAEL = e1;
      int prev = a1.prevInAEL;
      if (prev >= 0) A(prev).nextInAEL = e2;
      a2.prevInAEL = prev;
      a2.nextInAEL = e1;
      a1.prevInAEL = e2;
      a1.nextInAEL = next;
      if (prev < 0) _actives = e2;
    }

    private int GetLastOp(int hotEdge)
    {
      OutRec outrec = A(hotEdge).outrec!;
      int result = outrec.pts;
      if (hotEdge != outrec.frontEdge)
        result = O(result).next;
      return result;
    }

    private void AddTrialHorzJoin(int op)
    {
      if (Rec(O(op).outrec).isOpen) return;
      _horzSegList.Add(new HorzSegment(op));
    }

    private bool ResetHorzDirection(int horzIdx, int maxVertex,
      out long horzLeft, out long horzRight)
    {
      ref Active horz = ref A(horzIdx);
      if (horz.bot.X == horz.top.X)
      {
        // the horizontal edge is going nowhere ...
        horzLeft = horz.currX;
        horzRight = horz.currX;
        int e = horz.nextInAEL;
        while (e >= 0 && A(e).vertexTop != maxVertex) e = A(e).nextInAEL;
        return e >= 0;
      }
      else if (horz.currX < horz.top.X)
      {
        horzLeft = horz.currX;
        horzRight = horz.top.X;
        return true;
      }
      else
      {
        horzLeft = horz.top.X;
        horzRight = horz.currX;
        return false; // right to left
      }
    }

    private void DoHorizontal(int horzIdx)
    /*******************************************************************************
        * Notes: Horizontal edges (HEs) at scanline intersections (ie at the top or    *
        * bottom of a scanbeam) are processed as if layered.The order in which HEs     *
        * are processed doesn't matter. HEs intersect with the bottom vertices of      *
        * other HEs[#] and with non-horizontal edges [*]. Once these intersections     *
        * are completed, intermediate HEs are 'promoted' to the next edge in their     *
        * bounds, and they in turn may be intersected[%] by other HEs.                 *
        *                                                                              *
        * eg: 3 horizontals at a scanline:    /   |                     /           /  *
        *              |                     /    |     (HE3)o ========%========== o   *
        *              o ======= o(HE2)     /     |         /         /                *
        *          o ============#=========*======*========#=========o (HE1)           *
        *         /              |        /       |       /                            *
        *******************************************************************************/
    {
      ref Active horz = ref A(horzIdx);
      Point64 pt;
      bool horzIsOpen = ClipperEngine.IsOpen(horz);
      long y = horz.bot.Y;
      int vertexMax;
      if (horzIsOpen)
        vertexMax = GetCurrYMaximaVertex_Open(horz);
      else
        vertexMax = GetCurrYMaximaVertex(horz);

      bool isLeftToRight =
        ResetHorzDirection(horzIdx, vertexMax, out long horzLeft, out long horzRight);

      if (ClipperEngine.IsHotEdge(horz))
      {
#if USINGZ
        int op = AddOutPt(horzIdx, new Point64(horz.currX, y, horz.bot.Z));
#else
        int op = AddOutPt(horzIdx, new Point64(horz.currX, y));
#endif
        AddTrialHorzJoin(op);
      }

      while (true) // loop through consec. horizontal edges
      {
        int e;
        if (isLeftToRight) e = horz.nextInAEL;
        else e = horz.prevInAEL;

        while (e >= 0)
        {
          if (A(e).vertexTop == vertexMax)
          {
            if (ClipperEngine.IsHotEdge(horz) && ClipperEngine.IsJoined(A(e)))
              Split(e, A(e).top);

            if (ClipperEngine.IsHotEdge(horz))
            {
              while (horz.vertexTop != vertexMax)
              {
                AddOutPt(horzIdx, horz.top);
                UpdateEdgeIntoAEL(horzIdx);
              }
              if (isLeftToRight)
                AddLocalMaxPoly(horzIdx, e, horz.top);
              else
                AddLocalMaxPoly(e, horzIdx, horz.top);
            }
            DeleteFromAEL(e);
            DeleteFromAEL(horzIdx);
            return;
          }

          // if horzEdge is a maxima, keep going until we reach
          // its maxima pair, otherwise check for break conditions
          if (vertexMax != horz.vertexTop || IsOpenEnd(horz))
          {
            // otherwise stop when 'ae' is beyond the end of the horizontal line
            if ((isLeftToRight && A(e).currX > horzRight) ||
              (!isLeftToRight && A(e).currX < horzLeft)) break;

            if (A(e).currX == horz.top.X && !ClipperEngine.IsHorizontal(A(e)))
            {
              pt = Vx(NextVertex(horz)).pt;
              if (isLeftToRight)
              {
                // with open paths we'll only break once past horz's end
                if (ClipperEngine.IsOpen(A(e)) && !ClipperEngine.IsSamePolyType(A(e), horz) &&
                  !ClipperEngine.IsHotEdge(A(e)))
                {
                  if (ClipperEngine.TopX(A(e), pt.Y) > pt.X) break;
                }
                // otherwise we'll only break when horz's outslope is greater than e's
                else if (ClipperEngine.TopX(A(e), pt.Y) >= pt.X) break;
              }
              else
              {
                if (ClipperEngine.IsOpen(A(e)) && !ClipperEngine.IsSamePolyType(A(e), horz) &&
                  !ClipperEngine.IsHotEdge(A(e)))
                {
                  if (ClipperEngine.TopX(A(e), pt.Y) < pt.X) break;
                }
                else if (ClipperEngine.TopX(A(e), pt.Y) <= pt.X) break;
              }
            }
          }

          pt = new Point64(A(e).currX, horz.bot.Y);
          if (isLeftToRight)
          {
            IntersectEdges(horzIdx, e, pt);
            SwapPositionsInAEL(horzIdx, e);
            CheckJoinLeft(e, pt);
            horz.currX = A(e).currX;
            e = horz.nextInAEL;
          }
          else
          {
            IntersectEdges(e, horzIdx, pt);
            SwapPositionsInAEL(e, horzIdx);
            CheckJoinRight(e, pt);
            horz.currX = A(e).currX;
            e = horz.prevInAEL;
          }

          if (horz.outrec != null)
          {
            // nb: The outrec containing the op returned by IntersectEdges
            // above may no longer be associated with horzEdge.
            AddTrialHorzJoin(GetLastOp(horzIdx));
          }
        }

        // check if we've finished with (consecutive) horizontals ...
        if (horzIsOpen && IsOpenEnd(horz)) // ie open at top
        {
          if (ClipperEngine.IsHotEdge(horz))
          {
            AddOutPt(horzIdx, horz.top);
            if (IsFront(horzIdx))
              horz.outrec!.frontEdge = -1;
            else
              horz.outrec!.backEdge = -1;
            horz.outrec = null;
          }
          DeleteFromAEL(horzIdx);
          return;
        }
        else if (Vx(NextVertex(horz)).pt.Y != horz.top.Y)
          break;

        // still more horizontals in bound to process ...
        if (ClipperEngine.IsHotEdge(horz))
          AddOutPt(horzIdx, horz.top);
        UpdateEdgeIntoAEL(horzIdx);

        isLeftToRight =
          ResetHorzDirection(horzIdx, vertexMax, out horzLeft, out horzRight);
      }

      if (ClipperEngine.IsHotEdge(horz))
      {
        int op = AddOutPt(horzIdx, horz.top);
        AddTrialHorzJoin(op);
      }

      UpdateEdgeIntoAEL(horzIdx); // end of an intermediate horiz.
    }

    private void DoTopOfScanbeam(long y)
    {
      _sel = -1; // sel_ is reused to flag horizontals (see PushHorz below)
      int e = _actives;
      while (e >= 0)
      {
        ref Active ae = ref A(e);
        // nb: 'e' will never be horizontal here
        if (ae.top.Y == y)
        {
          ae.currX = ae.top.X;
          if (IsMaxima(ae))
          {
            e = DoMaxima(e); // TOP OF BOUND (MAXIMA)
            continue;
          }
          else
          {
            // INTERMEDIATE VERTEX ...
            if (ClipperEngine.IsHotEdge(ae)) AddOutPt(e, ae.top);
            UpdateEdgeIntoAEL(e);
            if (ClipperEngine.IsHorizontal(ae))
              PushHorz(e); // horizontals are processed later
          }
        }
        else // i.e. not the top of the edge
          ae.currX = ClipperEngine.TopX(ae, y);

        e = ae.nextInAEL;
      }
    }

    private int DoMaxima(int e)
    {
      int nextE, prevE, maxPair;
      prevE = A(e).prevInAEL;
      nextE = A(e).nextInAEL;
      if (IsOpenEnd(A(e)))
      {
        if (ClipperEngine.IsHotEdge(A(e))) AddOutPt(e, A(e).top);
        if (!ClipperEngine.IsHorizontal(A(e)))
        {
          if (ClipperEngine.IsHotEdge(A(e)))
          {
            if (IsFront(e))
              A(e).outrec!.frontEdge = -1;
            else
              A(e).outrec!.backEdge = -1;
            A(e).outrec = null;
          }
          DeleteFromAEL(e);
        }
        return nextE;
      }

      maxPair = GetMaximaPair(e);
      if (maxPair < 0) return nextE; // eMaxPair is horizontal

      if (ClipperEngine.IsJoined(A(e))) Split(e, A(e).top);
      if (ClipperEngine.IsJoined(A(maxPair))) Split(maxPair, A(maxPair).top);

      // only non-horizontal maxima here.
      // process any edges between maxima pair ...
      while (nextE != maxPair)
      {
        IntersectEdges(e, nextE, A(e).top);
        SwapPositionsInAEL(e, nextE);
        nextE = A(e).nextInAEL;
      }

      if (ClipperEngine.IsOpen(A(e)))
      {
        if (ClipperEngine.IsHotEdge(A(e)))
          AddLocalMaxPoly(e, maxPair, A(e).top);
        DeleteFromAEL(maxPair);
        DeleteFromAEL(e);
        return (prevE >= 0 ? A(prevE).nextInAEL : _actives);
      }

      // e.next_in_ael == max_pair ...
      if (ClipperEngine.IsHotEdge(A(e)))
        AddLocalMaxPoly(e, maxPair, A(e).top);

      DeleteFromAEL(e);
      DeleteFromAEL(maxPair);
      return (prevE >= 0 ? A(prevE).nextInAEL : _actives);
    }

    private void Split(int e, Point64 pt)
    {
      if (A(e).joinWith == JoinWith.Right)
      {
        A(e).joinWith = JoinWith.NoJoin;
        int next = A(e).nextInAEL;
        A(next).joinWith = JoinWith.NoJoin;
        AddLocalMinPoly(e, next, pt, true);
      }
      else
      {
        A(e).joinWith = JoinWith.NoJoin;
        int prev = A(e).prevInAEL;
        A(prev).joinWith = JoinWith.NoJoin;
        AddLocalMinPoly(prev, e, pt, true);
      }
    }

    private void CheckJoinLeft(int ei, Point64 pt, bool checkCurrX = false)
    {
      ref Active e = ref A(ei);
      int prevIdx = e.prevInAEL;
      if (prevIdx < 0) return;
      ref Active prev = ref A(prevIdx);
      if (!ClipperEngine.IsHotEdge(e) || !ClipperEngine.IsHotEdge(prev) ||
        ClipperEngine.IsHorizontal(e) || ClipperEngine.IsHorizontal(prev) ||
        ClipperEngine.IsOpen(e) || ClipperEngine.IsOpen(prev)) return;
      if ((pt.Y < e.top.Y + 2 || pt.Y < prev.top.Y + 2) &&
        ((e.bot.Y > pt.Y) || (prev.bot.Y > pt.Y))) return; // avoid trivial joins

      if (checkCurrX)
      {
        if (Clipper.PerpendicDistFromLineSqrd(pt, prev.bot, prev.top) > 0.25) return;
      }
      else if (e.currX != prev.currX) return;
      if (!InternalClipper.IsCollinear(e.top, pt, prev.top)) return;

      if (e.outrec!.idx == prev.outrec!.idx)
        AddLocalMaxPoly(prevIdx, ei, pt);
      else if (e.outrec.idx < prev.outrec.idx)
        JoinOutrecPaths(ei, prevIdx);
      else
        JoinOutrecPaths(prevIdx, ei);
      prev.joinWith = JoinWith.Right;
      e.joinWith = JoinWith.Left;
    }

    private void CheckJoinRight(int ei, Point64 pt, bool checkCurrX = false)
    {
      ref Active e = ref A(ei);
      int nextIdx = e.nextInAEL;
      if (nextIdx < 0) return;
      ref Active next = ref A(nextIdx);
      if (!ClipperEngine.IsHotEdge(e) || !ClipperEngine.IsHotEdge(next) ||
        ClipperEngine.IsHorizontal(e) || ClipperEngine.IsHorizontal(next) ||
        ClipperEngine.IsOpen(e) || ClipperEngine.IsOpen(next)) return;
      if ((pt.Y < e.top.Y + 2 || pt.Y < next.top.Y + 2) &&
        ((e.bot.Y > pt.Y) || (next.bot.Y > pt.Y))) return; // avoid trivial joins

      if (checkCurrX)
      {
        if (Clipper.PerpendicDistFromLineSqrd(pt, next.bot, next.top) > 0.35) return;
      }
      else if (e.currX != next.currX) return;
      if (!InternalClipper.IsCollinear(e.top, pt, next.top)) return;

      if (e.outrec!.idx == next.outrec!.idx)
        AddLocalMaxPoly(ei, nextIdx, pt);
      else if (e.outrec.idx < next.outrec.idx)
        JoinOutrecPaths(ei, nextIdx);
      else
        JoinOutrecPaths(nextIdx, ei);

      e.joinWith = JoinWith.Right;
      next.joinWith = JoinWith.Left;
    }

    private bool CheckBounds(OutRec outrec)
    {
      if (outrec.pts < 0) return false;
      if (!outrec.bounds.IsEmpty()) return true;
      // nb: CleanCollinear (called below) can add OutRecs to the list
      CleanCollinear(outrec);
      if (outrec.pts < 0 ||
        !BuildPath64(outrec.pts, _reverseSolution, false, _pathScratch))
        return false;
      outrec.path = ExactCopy(_pathScratch);
      outrec.bounds = InternalClipper.GetBounds(outrec.path);
      return true;
    }

    private bool CheckSplitOwner(OutRec outrec, List<OutRec> splits)
    {
      // nb: use indexing (not an iterator) in case 'splits' is modified inside this loop (#1029)
      for (int idx = 0; idx < splits.Count; ++idx)
      {
        OutRec split = splits[idx];
        if (split.pts < 0 && split.splits != null &&
          CheckSplitOwner(outrec, split.splits)) return true; // #942
        OutRec? realSplit = ClipperEngine.GetRealOutRec(split);
        if (realSplit == null || ReferenceEquals(realSplit, outrec) ||
          ReferenceEquals(realSplit.recursiveSplit, outrec)) continue;
        realSplit.recursiveSplit = outrec; // prevent infinite loops

        if (realSplit.splits != null && CheckSplitOwner(outrec, realSplit.splits))
          return true;

        if (!CheckBounds(realSplit) || !realSplit.bounds.Contains(outrec.bounds) ||
          !Path2ContainsPath1(outrec.pts, realSplit.pts)) continue;

        if (!ClipperEngine.IsValidOwner(outrec, realSplit)) // split is owned by outrec! (#957)
          realSplit.owner = outrec.owner;

        outrec.owner = realSplit;
        return true;
      }
      return false;
    }

    private void RecursiveCheckOwners(OutRec outrec, PolyPathBase polypath)
    {
      // pre-condition: outrec will have valid bounds
      // post-condition: if a valid path, outrec will have a polypath

      if (outrec.polypath != null || outrec.bounds.IsEmpty()) return;
      while (outrec.owner != null)
      {
        if (outrec.owner.splits != null && CheckSplitOwner(outrec, outrec.owner.splits)) break;
        if (outrec.owner.pts >= 0 && CheckBounds(outrec.owner) &&
          outrec.owner.bounds.Contains(outrec.bounds) &&
          Path2ContainsPath1(outrec.pts, outrec.owner.pts)) break;
        outrec.owner = outrec.owner.owner;
      }

      if (outrec.owner != null)
      {
        if (outrec.owner.polypath == null)
          RecursiveCheckOwners(outrec.owner, polypath);
        outrec.polypath = outrec.owner.polypath!.AddChild(outrec.path!);
      }
      else
        outrec.polypath = polypath.AddChild(outrec.path!);
    }

    // Clipper64 / ClipperD support ---------------------------------------------

    internal void BuildPaths64(Paths64 solutionClosed, Paths64? solutionOpen)
    {
      solutionClosed.Clear();
      if (solutionOpen != null) solutionOpen.Clear();

      // nb: outrec_list_.size() may change in the following
      // while loop because polygons may be split during
      // calls to CleanCollinear which calls FixSelfIntersects
      for (int i = 0; i < _outrecList.Count; ++i)
      {
        OutRec outrec = _outrecList[i];
        if (outrec.pts < 0) continue;

        if (solutionOpen != null && outrec.isOpen)
        {
          if (BuildPath64(outrec.pts, _reverseSolution, true, _pathScratch))
            solutionOpen.Add(ExactCopy(_pathScratch));
        }
        else
        {
          // nb: CleanCollinear can add to outrec_list_
          CleanCollinear(outrec);
          // closed paths should always return a Positive orientation
          if (BuildPath64(outrec.pts, _reverseSolution, false, _pathScratch))
            solutionClosed.Add(ExactCopy(_pathScratch));
        }
      }
    }

    internal void BuildTree64(PolyPath64 polytree, Paths64 openPaths)
    {
      polytree.Clear();
      openPaths.Clear();

      // outrec_list_.size() is not static here because
      // CheckBounds below can indirectly add additional
      // OutRec (via FixOutRecPts & CleanCollinear)
      for (int i = 0; i < _outrecList.Count; ++i)
      {
        OutRec outrec = _outrecList[i];
        if (outrec.pts < 0) continue;

        if (outrec.isOpen)
        {
          if (BuildPath64(outrec.pts, _reverseSolution, true, _pathScratch))
            openPaths.Add(ExactCopy(_pathScratch));
          continue;
        }

        if (CheckBounds(outrec))
          RecursiveCheckOwners(outrec, polytree);
      }
    }

    internal void BuildPathsD(PathsD solutionClosed, PathsD? solutionOpen, double invScale)
    {
      solutionClosed.Clear();
      if (solutionOpen != null) solutionOpen.Clear();

      // outrec_list_.size() is not static here because
      // CleanCollinear below can indirectly add additional
      // OutRec (via FixOutRecPts)
      for (int i = 0; i < _outrecList.Count; ++i)
      {
        OutRec outrec = _outrecList[i];
        if (outrec.pts < 0) continue;

        if (solutionOpen != null && outrec.isOpen)
        {
          if (BuildPathD(outrec.pts, _reverseSolution, true, _pathScratchD, invScale))
            solutionOpen.Add(ExactCopy(_pathScratchD));
        }
        else
        {
          CleanCollinear(outrec);
          // closed paths should always return a Positive orientation
          if (BuildPathD(outrec.pts, _reverseSolution, false, _pathScratchD, invScale))
            solutionClosed.Add(ExactCopy(_pathScratchD));
        }
      }
    }

    internal void BuildTreeD(PolyPathD polytree, PathsD openPaths, double invScale)
    {
      polytree.Clear();
      openPaths.Clear();
      polytree.Scale = invScale;

      for (int i = 0; i < _outrecList.Count; ++i)
      {
        OutRec outrec = _outrecList[i];
        if (outrec.pts < 0) continue;

        if (outrec.isOpen)
        {
          if (BuildPathD(outrec.pts, _reverseSolution, true, _pathScratchD, invScale))
            openPaths.Add(ExactCopy(_pathScratchD));
          continue;
        }

        if (CheckBounds(outrec))
          RecursiveCheckOwners(outrec, polytree);
      }
    }

    internal bool ExecuteInternal64(ClipType clipType, FillRule fillRule, bool usePolytrees)
    {
      return ExecuteInternal(clipType, fillRule, usePolytrees);
    }
  }

  // Clipper64 -------------------------------------------------------------------

  public class Clipper64 : ClipperBase
  {
    // A per thread engine for the library's own one-shot operations (the final
    // union of ClipperOffset, Clipper.BooleanOp): reusing it keeps its vertex,
    // out-point and out-rec pools, so a stream of small operations stops
    // allocating an object per vertex. It is taken out of the slot while in use,
    // so a re-entrant call (e.g. from a Z callback) simply gets a fresh engine,
    // and an engine whose pools grew very large is not kept alive.
    [ThreadStatic] private static Clipper64? t_shared;
    private const int SharedPoolLimit = 1 << 17;

    internal static Clipper64 RentShared()
    {
      Clipper64? c = t_shared;
      if (c == null) return new Clipper64();
      t_shared = null;
      return c;
    }

    internal static void ReturnShared(Clipper64 c)
    {
      c.Clear();
      if (c.PooledObjectCount > SharedPoolLimit) return;
      c.PreserveCollinear = true;
      c.ReverseSolution = false;
#if USINGZ
      c._zCallback = null;
      c.DefaultZ = 0;
#endif
      t_shared = c;
    }

    public void AddSubject(Path64 path)
    {
      AddPath(path, PathType.Subject, false);
    }

    public void AddOpenSubject(Path64 path)
    {
      AddPath(path, PathType.Subject, true);
    }

    public void AddClip(Path64 path)
    {
      AddPath(path, PathType.Clip, false);
    }

    public void AddSubject(Paths64 paths)
    {
      AddPaths(paths, PathType.Subject, false);
    }

    public void AddOpenSubject(Paths64 paths)
    {
      AddPaths(paths, PathType.Subject, true);
    }

    public void AddClip(Paths64 paths)
    {
      AddPaths(paths, PathType.Clip, false);
    }

    public bool Execute(ClipType clipType, FillRule fillRule,
      PolyTree64 polytree, Paths64 openPaths)
    {
      if (ExecuteInternal64(clipType, fillRule, true))
      {
        openPaths.Clear();
        polytree.Clear();
        BuildTree64(polytree, openPaths);
      }
      CleanUp();
      return _succeeded;
    }

    public bool Execute(ClipType clipType, FillRule fillRule, PolyTree64 polytree)
    {
      Paths64 dummy = new Paths64();
      return Execute(clipType, fillRule, polytree, dummy);
    }

    public bool Execute(ClipType clipType, FillRule fillRule,
      Paths64 solutionClosed, Paths64 openPaths)
    {
      solutionClosed.Clear();
      openPaths.Clear();
      if (ExecuteInternal64(clipType, fillRule, false))
        BuildPaths64(solutionClosed, openPaths);
      CleanUp();
      return _succeeded;
    }

    public bool Execute(ClipType clipType, FillRule fillRule, Paths64 solutionClosed)
    {
      Paths64 dummy = new Paths64();
      return Execute(clipType, fillRule, solutionClosed, dummy);
    }
  }

  // ClipperD -------------------------------------------------------------------

  public class ClipperD : ClipperBase
  {
    private readonly double _scale = 1.0;
    private readonly double _invScale = 1.0;

#if USINGZ
    public delegate void ZCallbackD(PointD bot1, PointD top1,
      PointD bot2, PointD top2, ref PointD intersectPt);

    protected ZCallbackD? _zCallbackD;

    public void SetZCallback(ZCallbackD callback) { _zCallbackD = callback; }

    private void ZCB(Point64 e1bot, Point64 e1top,
      Point64 e2bot, Point64 e2top, ref Point64 pt)
    {
      // de-scale (x & y)
      // temporarily convert integers to their initial float values
      // this will slow clipping marginally but will make it much easier
      // to understand the coordinates passed to the callback function
      PointD tmp = new PointD(pt, _invScale);
      PointD e1b = new PointD(e1bot, _invScale);
      PointD e1t = new PointD(e1top, _invScale);
      PointD e2b = new PointD(e2bot, _invScale);
      PointD e2t = new PointD(e2top, _invScale);
      _zCallbackD!(e1b, e1t, e2b, e2t, ref tmp);
      pt = new Point64(pt) { Z = tmp.z }; // only update 'z'
    }

    private void CheckCallback()
    {
      if (_zCallbackD != null)
        // if the user defined float point callback has been assigned
        // then assign the proxy callback function
        _zCallback = ZCB;
      else
        _zCallback = null;
    }
#endif

    public ClipperD(int precision = 2)
    {
      InternalClipper.CheckPrecisionRange(ref precision, ref _errorCode);
      // to optimize scaling / descaling precision
      // set the scale to a power of double's radix (2) (#25)
      _scale = Math.Pow(2.0, Math.ILogB(Math.Pow(10, precision)) + 1);
      _invScale = 1 / _scale;
    }

    private Paths64 ScalePathsIn(PathsD paths)
    {
      int errorCode = 0;
      Paths64 result = InternalClipper.ScalePaths(paths, _scale, ref errorCode);
      _errorCode |= errorCode;
      return result;
    }

    public void AddPath(PathD path, PathType polytype, bool isOpen = false)
    {
      AddPaths(ScalePathsIn(new PathsD { path }), polytype, isOpen);
    }

    public void AddPaths(PathsD paths, PathType polytype, bool isOpen = false)
    {
      AddPaths(ScalePathsIn(paths), polytype, isOpen);
    }

    public void AddSubject(PathD path) { AddPath(path, PathType.Subject, false); }
    public void AddOpenSubject(PathD path) { AddPath(path, PathType.Subject, true); }
    public void AddClip(PathD path) { AddPath(path, PathType.Clip, false); }
    public void AddSubject(PathsD paths) { AddPaths(paths, PathType.Subject, false); }
    public void AddOpenSubject(PathsD paths) { AddPaths(paths, PathType.Subject, true); }
    public void AddClip(PathsD paths) { AddPaths(paths, PathType.Clip, false); }

    public bool Execute(ClipType clipType, FillRule fillRule,
      PathsD solutionClosed, PathsD openPaths)
    {
#if USINGZ
      CheckCallback();
#endif
      if (ExecuteInternal64(clipType, fillRule, false))
        BuildPathsD(solutionClosed, openPaths, _invScale);
      CleanUp();
      return _succeeded;
    }

    public bool Execute(ClipType clipType, FillRule fillRule, PathsD solutionClosed)
    {
      PathsD dummy = new PathsD();
      return Execute(clipType, fillRule, solutionClosed, dummy);
    }

    public bool Execute(ClipType clipType, FillRule fillRule,
      PolyTreeD polytree, PathsD openPaths)
    {
#if USINGZ
      CheckCallback();
#endif
      if (ExecuteInternal64(clipType, fillRule, true))
      {
        polytree.Clear();
        polytree.Scale = _invScale;
        openPaths.Clear();
        BuildTreeD(polytree, openPaths, _invScale);
      }
      CleanUp();
      return _succeeded;
    }

    public bool Execute(ClipType clipType, FillRule fillRule, PolyTreeD polytree)
    {
      PathsD dummy = new PathsD();
      return Execute(clipType, fillRule, polytree, dummy);
    }
  }

  // PolyPath / PolyTree ---------------------------------------------------------

  // PolyTree: is intended as a READ-ONLY data structure for CLOSED paths returned
  // by clipping operations. While this structure is more complex than the
  // alternative Paths structure, it does preserve path 'ownership' - ie those
  // paths that contain (or own) other paths. This will be useful to some users.

  public abstract class PolyPathBase : IEnumerable
  {
    internal PolyPathBase? _parent;
    internal List<PolyPathBase> _childs = new List<PolyPathBase>();

    public IEnumerator GetEnumerator()
    {
      return new NodeEnumerator(_childs);
    }

    private class NodeEnumerator : IEnumerator
    {
      private int position = -1;
      private readonly List<PolyPathBase> _nodes;

      public NodeEnumerator(List<PolyPathBase> nodes)
      {
        _nodes = new List<PolyPathBase>(nodes);
      }

      public bool MoveNext()
      {
        position++;
        return (position < _nodes.Count);
      }

      public void Reset()
      {
        position = -1;
      }

      public object Current
      {
        get
        {
          if (position < 0 || position >= _nodes.Count)
            throw new InvalidOperationException();
          return _nodes[position];
        }
      }
    }

    public bool IsHole => GetIsHole();

    public PolyPathBase(PolyPathBase? parent = null) { _parent = parent; }

    private int GetLevel()
    {
      int result = 0;
      PolyPathBase? pp = _parent;
      while (pp != null) { ++result; pp = pp._parent; }
      return result;
    }

    public int Level => GetLevel();

    private bool GetIsHole()
    {
      int lvl = GetLevel();
      return lvl != 0 && (lvl & 1) == 0;
    }

    public int Count => _childs.Count;

    public abstract PolyPathBase AddChild(Path64 p);

    public void Clear()
    {
      _childs.Clear();
    }

    internal string ToStringInternal(int idx, int level)
    {
      string result = "", padding = "", plural = "s";
      if (_childs.Count == 1) plural = "";
      padding = padding.PadLeft(level * 2);
      if ((level & 1) == 0)
        result += $"{padding}+- hole ({idx}) contains {_childs.Count} nested polygon{plural}.\n";
      else
        result += $"{padding}+- polygon ({idx}) contains {_childs.Count} hole{plural}.\n";

      for (int i = 0; i < Count; i++)
        if (_childs[i].Count > 0)
          result += _childs[i].ToStringInternal(i, level + 1);
      return result;
    }

    public override string ToString()
    {
      if (Level > 0) return ""; // only accept tree root
      string plural = "s";
      if (_childs.Count == 1) plural = "";
      string result = $"Polytree with {_childs.Count} polygon{plural}.\n";
      for (int i = 0; i < Count; i++)
        if (_childs[i].Count > 0)
          result += _childs[i].ToStringInternal(i, 1);
      return result + '\n';
    }
  }

  public class PolyPath64 : PolyPathBase
  {
    public Path64? Polygon { get; private set; } // polytree root's polygon == null

    public PolyPath64(PolyPathBase? parent = null) : base(parent) { }

    public override PolyPathBase AddChild(Path64 p)
    {
      PolyPathBase newChild = new PolyPath64(this);
      (newChild as PolyPath64)!.Polygon = p;
      _childs.Add(newChild);
      return newChild;
    }

    public PolyPath64 this[int index]
    {
      get
      {
        if (index < 0 || index >= _childs.Count)
          throw new InvalidOperationException();
        return (PolyPath64) _childs[index];
      }
    }

    public PolyPath64 Child(int index)
    {
      if (index < 0 || index >= _childs.Count)
        throw new InvalidOperationException();
      return (PolyPath64) _childs[index];
    }

    public double Area()
    {
      double result = Polygon == null ? 0 : Clipper.Area(Polygon);
      foreach (PolyPathBase polyPathBase in _childs)
      {
        PolyPath64 child = (PolyPath64) polyPathBase;
        result += child.Area();
      }
      return result;
    }
  }

  public class PolyPathD : PolyPathBase
  {
    // nb: the factor from engine coordinates to the tree's (the C++ scale_):
    // ClipperD sets it to its inverse scale, so a Path64 child is multiplied by it
    internal double Scale { get; set; } = 1.0;
    public PathD? Polygon { get; private set; }

    public PolyPathD(PolyPathBase? parent = null) : base(parent)
    {
      if (parent is PolyPathD ppd) Scale = ppd.Scale;
    }

    public override PolyPathBase AddChild(Path64 p)
    {
      PolyPathD newChild = new PolyPathD(this);
      newChild.Scale = Scale;
      newChild.Polygon = Clipper.ScalePathD(p, Scale); // C++: ScalePath(path, scale_)
      _childs.Add(newChild);
      return newChild;
    }

    public PolyPathBase AddChild(PathD p)
    {
      PolyPathD newChild = new PolyPathD(this);
      newChild.Scale = Scale;
      newChild.Polygon = p;
      _childs.Add(newChild);
      return newChild;
    }

    public PolyPathD this[int index]
    {
      get
      {
        if (index < 0 || index >= _childs.Count)
          throw new InvalidOperationException();
        return (PolyPathD) _childs[index];
      }
    }

    public PolyPathD Child(int index)
    {
      if (index < 0 || index >= _childs.Count)
        throw new InvalidOperationException();
      return (PolyPathD) _childs[index];
    }

    public double Area()
    {
      double result = Polygon == null ? 0 : Clipper.Area(Polygon);
      foreach (PolyPathBase polyPathBase in _childs)
      {
        PolyPathD child = (PolyPathD) polyPathBase;
        result += child.Area();
      }
      return result;
    }
  }

  public class PolyTree64 : PolyPath64 { }

  public class PolyTreeD : PolyPathD
  {
    public new double Scale
    {
      get => base.Scale;
      set => base.Scale = value;
    }
  }

  public class ClipperLibException : Exception
  {
    public ClipperLibException(string description) : base(description) { }
  }
}
