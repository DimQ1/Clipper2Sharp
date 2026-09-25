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

  internal class Vertex
  {
    public Point64 pt;
    public Vertex? next;
    public Vertex? prev;
    public VertexFlags flags = VertexFlags.Empty;
  }

  internal class OutPt
  {
    public Point64 pt;
    public OutPt next;
    public OutPt prev;
    public OutRec outrec;
    public HorzSegment? horz;

    public OutPt(Point64 pt, OutRec outrec)
    {
      this.pt = pt;
      this.outrec = outrec;
      next = this;
      prev = this;
    }
  }

  internal class OutRec
  {
    public int idx;
    public OutRec? owner;
    public Active? frontEdge;
    public Active? backEdge;
    public OutPt? pts;
    public PolyPathBase? polypath;
    public List<OutRec>? splits;
    public OutRec? recursiveSplit;
    public Rect64 bounds = new Rect64();
    public Path64 path = new Path64();
    public bool isOpen;
  }

  internal class Active
  {
    public Point64 bot;
    public Point64 top;
    public long currX;          // current (updated at every new scanline)
    public double dx;
    public int windDx = 1;      // 1 or -1 depending on winding direction
    public int windCount;
    public int windCount2;      // winding count of the opposite polytype
    public OutRec? outrec;

    // AEL: 'active edge list' (Vatti's AET - active edge table)
    //      a linked list of all edges (from left to right) that are present
    //      (or 'active') within the current scanbeam (a horizontal 'beam' that
    //      sweeps from bottom to top over the paths in the clipping operation).
    public Active? prevInAEL;
    public Active? nextInAEL;

    // SEL: 'sorted edge list' (Vatti's ST - sorted table)
    //      linked list used when sorting edges into their new positions at the
    //      top of scanbeams, but also (re)used to process horizontals.
    public Active? prevInSEL;
    public Active? nextInSEL;
    public Active? jump;
    public Vertex? vertexTop;
    public LocalMinima? localMin;     // the bottom of an edge 'bound' (also Vatti)

    public bool isLeftBound;
    public JoinWith joinWith = JoinWith.NoJoin;
  }

  internal class LocalMinima
  {
    public readonly Vertex vertex;
    public readonly PathType polytype;
    public readonly bool isOpen;

    public LocalMinima(Vertex vertex, PathType polytype, bool isOpen)
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
    public readonly Active edge1;
    public readonly Active edge2;

    public IntersectNode(Active e1, Active e2, Point64 pt)
    {
      this.pt = pt;
      edge1 = e1;
      edge2 = e2;
    }
  }

  internal class HorzSegment
  {
    public OutPt leftOp;
    public OutPt? rightOp;
    public bool leftToRight = true;

    public HorzSegment(OutPt op) { leftOp = op; }
  }

  internal class HorzJoin
  {
    public OutPt op1;
    public OutPt op2;

    public HorzJoin(OutPt ltr, OutPt rtl)
    {
      op1 = ltr;
      op2 = rtl;
    }
  }

  // ReuseableDataContainer64 ---------------------------------------------------

  public class ReuseableDataContainer64
  {
    internal readonly List<LocalMinima> _minimaList = new List<LocalMinima>();
    internal readonly VertexPoolList _vertexPool = new VertexPoolList();

    public virtual void Clear()
    {
      _minimaList.Clear();
      _vertexPool.Release();
    }

    public void AddPaths(Paths64 paths, PathType polytype, bool isOpen)
    {
      ClipperEngine.AddPaths_(paths, polytype, isOpen, _vertexPool, _minimaList);
    }
  }

  // Internal helper functions --------------------------------------------------

  internal static class ClipperEngine
  {
    internal static readonly Rect64 InvalidRect = new Rect64(false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOdd(int val) => (val & 1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHotEdge(Active e) => e.outrec != null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOpen(Active e) => e.localMin!.isOpen;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOpenEnd(Vertex v) =>
      (v.flags & (VertexFlags.OpenStart | VertexFlags.OpenEnd)) != VertexFlags.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOpenEnd(Active e) => IsOpenEnd(e.vertexTop!);

    internal static Active? GetPrevHotEdge(Active e)
    {
      Active? prev = e.prevInAEL;
      while (prev != null && (IsOpen(prev) || !IsHotEdge(prev)))
        prev = prev.prevInAEL;
      return prev;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsFront(Active e) => ReferenceEquals(e, e.outrec!.frontEdge);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsInvalidPath(OutPt? op) => op == null || ReferenceEquals(op.next, op);

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

    internal static long TopX(Active ae, long currentY)
    {
      if ((currentY == ae.top.Y) || (ae.top.X == ae.bot.X)) return ae.top.X;
      else if (currentY == ae.bot.Y) return ae.bot.X;
      else return ae.bot.X + (long) Math.Round(ae.dx * (currentY - ae.bot.Y));
      // nb: rounding (rather than truncation) substantially *improves* performance here
      // as it greatly improves the likelihood of edge adjacency in ProcessIntersectList().
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHorizontal(Active e) => e.top.Y == e.bot.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHeadingRightHorz(Active e) => e.dx == -double.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsHeadingLeftHorz(Active e) => e.dx == double.MaxValue;

    internal static void SwapActives(ref Active e1, ref Active e2)
    {
      Active e = e1;
      e1 = e2;
      e2 = e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PathType GetPolyType(Active e) => e.localMin!.polytype;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSamePolyType(Active e1, Active e2) =>
      e1.localMin!.polytype == e2.localMin!.polytype;

    internal static void SetDx(Active e)
    {
      e.dx = GetDx(e.bot, e.top);
    }

    internal static Vertex NextVertex(Active e)
    {
      if (e.windDx > 0)
        return e.vertexTop!.next!;
      else
        return e.vertexTop!.prev!;
    }

    // PrevPrevVertex: useful to get the (inverted Y-axis) top of the
    // alternate edge (ie left or right bound) during edge insertion.
    internal static Vertex PrevPrevVertex(Active ae)
    {
      if (ae.windDx > 0)
        return ae.vertexTop!.prev!.prev!;
      else
        return ae.vertexTop!.next!.next!;
    }

    internal static Active? ExtractFromSEL(Active ae)
    {
      Active? res = ae.nextInSEL;
      if (res != null)
        res.prevInSEL = ae.prevInSEL;
      ae.prevInSEL!.nextInSEL = res;
      return res;
    }

    internal static void Insert1Before2InSEL(Active ae1, Active ae2)
    {
      ae1.prevInSEL = ae2.prevInSEL;
      if (ae1.prevInSEL != null)
        ae1.prevInSEL.nextInSEL = ae1;
      ae1.nextInSEL = ae2;
      ae2.prevInSEL = ae1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMaxima(Vertex v) => (v.flags & VertexFlags.LocalMax) != VertexFlags.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMaxima(Active e) => IsMaxima(e.vertexTop!);

    internal static Vertex? GetCurrYMaximaVertex_Open(Active e)
    {
      Vertex result = e.vertexTop!;
      if (e.windDx > 0)
        while (result.next!.pt.Y == result.pt.Y &&
          (result.flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == VertexFlags.Empty)
          result = result.next;
      else
        while (result.prev!.pt.Y == result.pt.Y &&
          (result.flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == VertexFlags.Empty)
          result = result.prev;
      if (!IsMaxima(result)) result = null!; // not a maxima
      return result;
    }

    internal static Vertex? GetCurrYMaximaVertex(Active e)
    {
      Vertex result = e.vertexTop!;
      if (e.windDx > 0)
        while (result.next!.pt.Y == result.pt.Y) result = result.next;
      else
        while (result.prev!.pt.Y == result.pt.Y) result = result.prev;
      if (!IsMaxima(result)) result = null!; // not a maxima
      return result;
    }

    internal static Active? GetMaximaPair(Active e)
    {
      Active? e2 = e.nextInAEL;
      while (e2 != null)
      {
        if (ReferenceEquals(e2.vertexTop, e.vertexTop)) return e2; // Found!
        e2 = e2.nextInAEL;
      }
      return null;
    }

    internal static int PointCount(OutPt? op)
    {
      if (op == null) return 0;
      OutPt op2 = op;
      int cnt = 0;
      do
      {
        op2 = op2.next;
        ++cnt;
      } while (!ReferenceEquals(op2, op));
      return cnt;
    }

    internal static OutPt DuplicateOp(OutPt op, bool insertAfter, OutPtPoolList pool)
    {
      OutPt result = pool.Add(op.pt, op.outrec);
      if (insertAfter)
      {
        result.next = op.next;
        result.next.prev = result;
        result.prev = op;
        op.next = result;
      }
      else
      {
        result.prev = op.prev;
        result.prev.next = result;
        result.next = op;
        op.prev = result;
      }
      return result;
    }

    internal static OutPt? DisposeOutPt(OutPt op)
    {
      OutPt? result = op.next;
      op.prev.next = op.next;
      op.next.prev = op.prev;
      return result;
    }

    internal static void DisposeOutPts(OutRec outrec)
    {
      // nb: the GC handles the (circular) OutPt list, so it's enough
      // to simply unhook the list from its OutRec
      outrec.pts = null;
    }

    internal static int IntersectListSort(IntersectNode a, IntersectNode b)
    {
      // note different inequality tests ...
      return (a.pt.Y == b.pt.Y) ? a.pt.X.CompareTo(b.pt.X) : b.pt.Y.CompareTo(a.pt.Y);
    }
    internal static void SetSides(OutRec outrec, Active startEdge, Active endEdge)
    {
      outrec.frontEdge = startEdge;
      outrec.backEdge = endEdge;
    }

    internal static void SwapOutrecs(Active e1, Active e2)
    {
      OutRec? or1 = e1.outrec;
      OutRec? or2 = e2.outrec;
      if (ReferenceEquals(or1, or2))
      {
        Active? e = or1!.frontEdge;
        or1.frontEdge = or1.backEdge;
        or1.backEdge = e;
        return;
      }
      if (or1 != null)
      {
        if (ReferenceEquals(e1, or1.frontEdge))
          or1.frontEdge = e2;
        else
          or1.backEdge = e2;
      }
      if (or2 != null)
      {
        if (ReferenceEquals(e2, or2.frontEdge))
          or2.frontEdge = e1;
        else
          or2.backEdge = e1;
      }
      e1.outrec = or2;
      e2.outrec = or1;
    }

    internal static double Area(OutPt op)
    {
      // https://en.wikipedia.org/wiki/Shoelace_formula
      double result = 0.0;
      OutPt op2 = op;
      do
      {
        result += (double) (op2.prev.pt.Y + op2.pt.Y) *
          (double) (op2.prev.pt.X - op2.pt.X);
        op2 = op2.next;
      } while (!ReferenceEquals(op2, op));
      return result * 0.5;
    }

    internal static double AreaTriangle(Point64 pt1, Point64 pt2, Point64 pt3)
    {
      return ((double) (pt3.Y + pt1.Y) * (double) (pt3.X - pt1.X) +
        (double) (pt1.Y + pt2.Y) * (double) (pt1.X - pt2.X) +
        (double) (pt2.Y + pt3.Y) * (double) (pt2.X - pt3.X));
    }

    internal static void ReverseOutPts(OutPt? op)
    {
      if (op == null) return;

      OutPt op1 = op;
      OutPt op2;

      do
      {
        op2 = op1.next;
        op1.next = op1.prev;
        op1.prev = op2;
        op1 = op2;
      } while (!ReferenceEquals(op1, op));
    }

    internal static void SwapSides(OutRec outrec)
    {
      Active? e2 = outrec.frontEdge;
      outrec.frontEdge = outrec.backEdge;
      outrec.backEdge = e2;
      outrec.pts = outrec.pts!.next;
    }

    internal static OutRec? GetRealOutRec(OutRec? outrec)
    {
      while (outrec != null && outrec.pts == null) outrec = outrec.owner;
      return outrec;
    }

    internal static bool IsValidOwner(OutRec outrec, OutRec? testOwner)
    {
      // prevent outrec owning itself either directly or indirectly
      while (testOwner != null && !ReferenceEquals(testOwner, outrec))
        testOwner = testOwner.owner;
      return testOwner == null;
    }

    internal static void UncoupleOutRec(Active ae)
    {
      OutRec? outrec = ae.outrec;
      if (outrec == null) return;
      outrec.frontEdge!.outrec = null;
      outrec.backEdge!.outrec = null;
      outrec.frontEdge = null;
      outrec.backEdge = null;
    }

    internal static bool PtsReallyClose(Point64 pt1, Point64 pt2)
    {
      return (Math.Abs(pt1.X - pt2.X) < 2) && (Math.Abs(pt1.Y - pt2.Y) < 2);
    }

    internal static bool IsVerySmallTriangle(OutPt op)
    {
      return ReferenceEquals(op.next.next, op.prev) &&
        (PtsReallyClose(op.prev.pt, op.next.pt) ||
          PtsReallyClose(op.pt, op.next.pt) ||
          PtsReallyClose(op.pt, op.prev.pt));
    }

    internal static bool IsValidClosedPath(OutPt? op)
    {
      return op != null && (!ReferenceEquals(op.next, op)) &&
        (!ReferenceEquals(op.next, op.prev)) && !IsVerySmallTriangle(op);
    }

    internal static bool OutrecIsAscending(Active? hotEdge)
    {
      return ReferenceEquals(hotEdge, hotEdge!.outrec!.frontEdge);
    }

    internal static void SwapFrontBackSides(OutRec outrec)
    {
      Active? tmp = outrec.frontEdge;
      outrec.frontEdge = outrec.backEdge;
      outrec.backEdge = tmp;
      outrec.pts = outrec.pts!.next;
    }

    internal static bool EdgesAdjacentInAEL(IntersectNode inode)
    {
      return ReferenceEquals(inode.edge1.nextInAEL, inode.edge2) ||
        ReferenceEquals(inode.edge1.prevInAEL, inode.edge2);
    }

    internal static bool IsJoined(Active e) => e.joinWith != JoinWith.NoJoin;

    internal static void SetOwner(OutRec outrec, OutRec newOwner)
    {
      // precondition1: new_owner is never null
      newOwner.owner = GetRealOutRec(newOwner.owner);
      OutRec? tmp = newOwner;
      while (tmp != null && !ReferenceEquals(tmp, outrec)) tmp = tmp.owner;
      if (tmp != null) newOwner.owner = outrec.owner;
      outrec.owner = newOwner;
    }

    internal static PointInPolygonResult PointInOpPolygon(Point64 pt, OutPt op)
    {
      if (ReferenceEquals(op, op.next) || ReferenceEquals(op.prev, op.next))
        return PointInPolygonResult.IsOutside;

      OutPt op2 = op;
      do
      {
        if (op.pt.Y != pt.Y) break;
        op = op.next;
      } while (!ReferenceEquals(op, op2));
      if (op.pt.Y == pt.Y) // not a proper polygon
        return PointInPolygonResult.IsOutside;

      bool isAbove = op.pt.Y < pt.Y, startingAbove = isAbove;
      int val = 0;
      op2 = op.next;
      while (!ReferenceEquals(op2, op))
      {
        if (isAbove)
          while (!ReferenceEquals(op2, op) && op2.pt.Y < pt.Y) op2 = op2.next;
        else
          while (!ReferenceEquals(op2, op) && op2.pt.Y > pt.Y) op2 = op2.next;
        if (ReferenceEquals(op2, op)) break;

        // must have touched or crossed the pt.Y horizontal
        // and this must happen an even number of times

        if (op2.pt.Y == pt.Y) // touching the horizontal
        {
          if (op2.pt.X == pt.X || (op2.pt.Y == op2.prev.pt.Y &&
            (pt.X < op2.prev.pt.X) != (pt.X < op2.pt.X)))
            return PointInPolygonResult.IsOn;

          op2 = op2.next;
          if (ReferenceEquals(op2, op)) break;
          continue;
        }

        if (pt.X < op2.pt.X && pt.X < op2.prev.pt.X)
        {
          // do nothing because
          // we're only interested in edges crossing on the left
        }
        else if ((pt.X > op2.prev.pt.X && pt.X > op2.pt.X))
          val = 1 - val; // toggle val
        else
        {
          int i = InternalClipper.CrossProductSign(op2.prev.pt, op2.pt, pt);
          if (i == 0) return PointInPolygonResult.IsOn;
          if ((i < 0) == isAbove) val = 1 - val;
        }
        isAbove = !isAbove;
        op2 = op2.next;
      }

      if (isAbove != startingAbove)
      {
        int i = InternalClipper.CrossProductSign(op2.prev.pt, op2.pt, pt);
        if (i == 0) return PointInPolygonResult.IsOn;
        if ((i < 0) == isAbove) val = 1 - val;
      }

      if (val == 0) return PointInPolygonResult.IsOutside;
      else return PointInPolygonResult.IsInside;
    }

    internal static Path64 GetCleanPath(OutPt op)
    {
      Path64 result = new Path64();
      OutPt op2 = op;
      while (!ReferenceEquals(op2.next, op) &&
        ((op2.pt.X == op2.next.pt.X && op2.pt.X == op2.prev.pt.X) ||
          (op2.pt.Y == op2.next.pt.Y && op2.pt.Y == op2.prev.pt.Y))) op2 = op2.next;
      result.Add(op2.pt);
      OutPt prevOp = op2;
      op2 = op2.next;
      while (!ReferenceEquals(op2, op))
      {
        if ((op2.pt.X != op2.next.pt.X || op2.pt.X != prevOp.pt.X) &&
          (op2.pt.Y != op2.next.pt.Y || op2.pt.Y != prevOp.pt.Y))
        {
          result.Add(op2.pt);
          prevOp = op2;
        }
        op2 = op2.next;
      }
      return result;
    }

    internal static bool Path2ContainsPath1(OutPt op1, OutPt op2)
    {
      // this function accommodates rounding errors that
      // can cause path micro intersections
      PointInPolygonResult pip = PointInPolygonResult.IsOn;
      OutPt op = op1;
      do
      {
        switch (PointInOpPolygon(op.pt, op2))
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
        op = op.next;
      } while (!ReferenceEquals(op, op1));
      // result unclear, so try again using cleaned paths
      return InternalClipper.Path2ContainsPath1(GetCleanPath(op1), GetCleanPath(op2)); // (#973)
    }

    internal static void AddLocMin(List<LocalMinima> list,
      Vertex vert, PathType polytype, bool isOpen)
    {
      // make sure the vertex is added only once ...
      if ((VertexFlags.LocalMin & vert.flags) != VertexFlags.Empty) return;

      vert.flags |= VertexFlags.LocalMin;
      list.Add(new LocalMinima(vert, polytype, isOpen));
    }

    internal static void AddPaths_(Paths64 paths, PathType polytype, bool isOpen,
      VertexPoolList vertexPool, List<LocalMinima> locMinList)
    {
      long totalVertexCount = 0;
      foreach (Path64 path in paths) totalVertexCount += path.Count;
      if (totalVertexCount == 0) return;
      // nb: the batch size is known here, so the block is grown once (doubling
      // from the initial 64 would leave every intermediate block as garbage).
      // The count must include the vertices already pooled, because the same
      // pool receives the subject and then the clip paths.
      // nb: the batch size is known here, so the block is grown once (doubling
      // from the initial 64 would leave every intermediate block as garbage).
      // The count must include the vertices already pooled, because the same
      // pool receives the subject and then the clip paths.
      vertexPool.EnsureCapacity(vertexPool.Count + (int) totalVertexCount);

      foreach (Path64 path in paths)
      {
        // for each path create a circular double linked list of vertices
        Vertex? v0 = null, prevV = null;
        int cnt = 0;
        if (path.Count == 0) continue;

        Span<Point64> pts = CollectionsMarshal.AsSpan(path);
        for (int i = 0; i < pts.Length; i++)
        {
          Point64 pt = pts[i];
          if (prevV != null && prevV.pt == pt) continue; // ie skips duplicates
          Vertex currV = vertexPool.Add(pt, VertexFlags.Empty, prevV);
          if (prevV != null) prevV.next = currV;
          if (v0 == null) v0 = currV;
          prevV = currV;
          cnt++;
        }
        if (prevV == null || prevV.prev == null) continue;
        if (!isOpen && prevV.pt == v0!.pt)
          prevV = prevV.prev; // drop the duplicate closing vertex
        prevV.next = v0;
        v0.prev = prevV;
        if (cnt < 2 || (cnt == 2 && !isOpen)) continue;

        // now find and assign local minima
        bool goingUp, goingUp0;
        Vertex currVertex;
        if (isOpen)
        {
          currVertex = v0.next!;
          while (!ReferenceEquals(currVertex, v0) && currVertex.pt.Y == v0.pt.Y)
            currVertex = currVertex.next!;
          goingUp = currVertex.pt.Y <= v0.pt.Y;
          if (goingUp)
          {
            v0.flags = VertexFlags.OpenStart;
            AddLocMin(locMinList, v0, polytype, true);
          }
          else
            v0.flags = VertexFlags.OpenStart | VertexFlags.LocalMax;
        }
        else // closed path
        {
          Vertex prevVertex0 = v0.prev!;
          while (!ReferenceEquals(prevVertex0, v0) && prevVertex0.pt.Y == v0.pt.Y)
            prevVertex0 = prevVertex0.prev!;
          if (ReferenceEquals(prevVertex0, v0))
            continue; // only open paths can be completely flat
          goingUp = prevVertex0.pt.Y > v0.pt.Y;
        }

        goingUp0 = goingUp;
        Vertex prevVtx = v0;
        currVertex = v0.next!;
        while (!ReferenceEquals(currVertex, v0))
        {
          if (currVertex.pt.Y > prevVtx.pt.Y && goingUp)
          {
            prevVtx.flags |= VertexFlags.LocalMax;
            goingUp = false;
          }
          else if (currVertex.pt.Y < prevVtx.pt.Y && !goingUp)
          {
            goingUp = true;
            AddLocMin(locMinList, prevVtx, polytype, isOpen);
          }
          prevVtx = currVertex;
          currVertex = currVertex.next!;
        }

        if (isOpen)
        {
          prevVtx.flags |= VertexFlags.OpenEnd;
          if (goingUp)
            prevVtx.flags |= VertexFlags.LocalMax;
          else
            AddLocMin(locMinList, prevVtx, polytype, isOpen);
        }
        else if (goingUp != goingUp0)
        {
          if (goingUp0) AddLocMin(locMinList, prevVtx, polytype, false);
          else prevVtx.flags |= VertexFlags.LocalMax;
        }
      } // end processing current path
    }

    internal static bool BuildPath64(OutPt? op, bool reverse, bool isOpen, Path64 path)
    {
      if (op == null || ReferenceEquals(op.next, op) || (!isOpen && ReferenceEquals(op.next, op.prev)))
        return false;

      path.Clear();
      Point64 lastPt;
      OutPt op2;
      if (reverse)
      {
        lastPt = op.pt;
        op2 = op.prev;
      }
      else
      {
        op = op.next;
        lastPt = op.pt;
        op2 = op.next;
      }
      path.Add(lastPt);

      while (!ReferenceEquals(op2, op))
      {
        if (op2.pt != lastPt)
        {
          lastPt = op2.pt;
          path.Add(lastPt);
        }
        if (reverse)
          op2 = op2.prev;
        else
          op2 = op2.next;
      }

      if (!isOpen && path.Count == 3 && IsVerySmallTriangle(op2)) return false;
      else return true;
    }

    internal static bool BuildPathD(OutPt? op, bool reverse, bool isOpen, PathD path, double invScale)
    {
      if (op == null || ReferenceEquals(op.next, op) || (!isOpen && ReferenceEquals(op.next, op.prev)))
        return false;

      path.Clear();
      Point64 lastPt;
      OutPt op2;
      if (reverse)
      {
        lastPt = op.pt;
        op2 = op.prev;
      }
      else
      {
        op = op.next;
        lastPt = op.pt;
        op2 = op.next;
      }
      path.Add(new PointD(lastPt.X * invScale, lastPt.Y * invScale
#if USINGZ
        , lastPt.Z
#endif
      ));

      while (!ReferenceEquals(op2, op))
      {
        if (op2.pt != lastPt)
        {
          lastPt = op2.pt;
          path.Add(new PointD(lastPt.X * invScale, lastPt.Y * invScale
#if USINGZ
            , lastPt.Z
#endif
          ));
        }
        if (reverse)
          op2 = op2.prev;
        else
          op2 = op2.next;
      }
      if (path.Count == 3 && IsVerySmallTriangle(op2)) return false;
      return true;
    }

    internal static bool GetHorzExtendedHorzSeg(ref OutPt op, ref OutPt op2)
    {
      OutRec? outrec = GetRealOutRec(op.outrec);
      op2 = op;
      if (outrec!.frontEdge != null)
      {
        while (!ReferenceEquals(op.prev, outrec.pts) &&
          op.prev.pt.Y == op.pt.Y) op = op.prev;
        while (!ReferenceEquals(op2, outrec.pts) &&
          op2.next.pt.Y == op2.pt.Y) op2 = op2.next;
        return !ReferenceEquals(op2, op);
      }
      else
      {
        while (!ReferenceEquals(op.prev, op2) && op.prev.pt.Y == op.pt.Y)
          op = op.prev;
        while (!ReferenceEquals(op2.next, op) && op2.next.pt.Y == op2.pt.Y)
          op2 = op2.next;
        return !ReferenceEquals(op2, op) && !ReferenceEquals(op2.next, op);
      }
    }

    /// <summary>
    /// Stable sort helper (mirrors std::stable_sort used in the C++ engine).
    /// </summary>
    internal static void StableSort<T>(List<T> list, Comparison<T> comparison)
    {
      int n = list.Count;
      if (n < 2) return;
      T[] tmp = new T[n];
      MergeSort(list, tmp, 0, n - 1, comparison);
    }

    private static void MergeSort<T>(List<T> list, T[] tmp, int lo, int hi, Comparison<T> comparison)
    {
      if (lo >= hi) return;
      int mid = lo + ((hi - lo) >> 1);
      MergeSort(list, tmp, lo, mid, comparison);
      MergeSort(list, tmp, mid + 1, hi, comparison);
      int i = lo, j = mid + 1, k = lo;
      while (i <= mid && j <= hi)
      {
        if (comparison(list[i], list[j]) <= 0) tmp[k++] = list[i++];
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
    private Active? _actives;
    private Active? _sel;

    internal readonly List<LocalMinima> _minimaList = new List<LocalMinima>();
    private int _currentLocMin;
    private readonly VertexPoolList _vertexPool = new VertexPoolList();
    private readonly OutPtPoolList _outPtPool = new OutPtPoolList();
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

    private void SetZ(Active e1, Active e2, ref Point64 intersectPt)
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

    private void DeleteEdges(ref Active? e)
    {
      // nb: the GC cleans up unreferenced actives
      e = null;
    }

    protected void CleanUp()
    {
      DeleteEdges(ref _actives);
      _scanlineList.Clear();
      _intersectNodes.Clear();
      DisposeAllOutRecs();
      _horzSegList.Clear();
      _horzJoinList.Clear();
      // nb: pooled objects are ready for reuse by the next operation
      _outrecPool.Clear();
      _outPtPool.Clear();
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
      if (!_minimaListSorted)
      {
        ClipperEngine.StableSort(_minimaList, LocMinSorter); // #594
        _minimaListSorted = true;
      }
      for (int i = _minimaList.Count - 1; i >= 0; i--)
        InsertScanline(_minimaList[i].vertex.pt.Y);

      _currentLocMin = 0;
      _actives = null;
      _sel = null;
      _succeeded = true;
    }

    private static int LocMinSorter(LocalMinima locMin1, LocalMinima locMin2)
    {
      if (locMin2.vertex.pt.Y != locMin1.vertex.pt.Y)
        return locMin2.vertex.pt.Y.CompareTo(locMin1.vertex.pt.Y);
      else
        return locMin2.vertex.pt.X.CompareTo(locMin1.vertex.pt.X);
    }

    internal void AddPath(Path64 path, PathType polytype, bool isOpen)
    {
      AddPaths(new Paths64 { path }, polytype, isOpen);
    }

    internal void AddPaths(Paths64 paths, PathType polytype, bool isOpen)
    {
      if (isOpen) _hasOpenPaths = true;
      _minimaListSorted = false;
      ClipperEngine.AddPaths_(paths, polytype, isOpen, _vertexPool, _minimaList);
    }

    public void AddReuseableData(ReuseableDataContainer64 reuseableData)
    {
      // nb: reuseable_data will continue to own the vertices
      // and remains responsible for their clean up.
      _succeeded = false;
      _minimaListSorted = false;
      foreach (LocalMinima lm in reuseableData._minimaList)
      {
        _minimaList.Add(new LocalMinima(lm.vertex, lm.polytype, lm.isOpen));
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
        _minimaList[_currentLocMin].vertex.pt.Y != y) return false;
      localMinima = _minimaList[_currentLocMin++];
      return true;
    }

    private void DisposeAllOutRecs()
    {
      foreach (OutRec outrec in _outrecList)
      {
        if (outrec.pts != null) ClipperEngine.DisposeOutPts(outrec);
      }
      _outrecList.Clear();
    }

    private void DisposeVerticesAndLocalMinima()
    {
      _minimaList.Clear();
      _vertexPool.Clear();
    }

    private bool IsContributingClosed(Active e)
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

    private bool IsContributingOpen(Active e)
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

    private void SetWindCountForClosedPathEdge(Active e)
    {
      // Wind counts refer to polygon regions not edges, so here an edge's WindCnt
      // indicates the higher of the wind counts for the two regions touching the
      // edge. (NB Adjacent regions can only ever have their wind counts differ by
      // one. Also, open paths have no meaningful wind directions or counts.)

      Active? e2 = e.prevInAEL;
      // find the nearest closed path edge of the same PolyType in AEL (heading left)
      PathType pt = ClipperEngine.GetPolyType(e);
      while (e2 != null && (ClipperEngine.GetPolyType(e2) != pt || ClipperEngine.IsOpen(e2)))
        e2 = e2.prevInAEL;

      if (e2 == null)
      {
        e.windCount = e.windDx;
        e2 = _actives;
      }
      else if (_fillrule == FillRule.EvenOdd)
      {
        e.windCount = e.windDx;
        e.windCount2 = e2.windCount2;
        e2 = e2.nextInAEL;
      }
      else
      {
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
        e2 = e2.nextInAEL; // ie get ready to calc WindCnt2
      }

      // update wind_cnt2 ...
      if (_fillrule == FillRule.EvenOdd)
        while (e2 != null && !ReferenceEquals(e2, e))
        {
          if (ClipperEngine.GetPolyType(e2) != pt && !ClipperEngine.IsOpen(e2))
            e.windCount2 = (e.windCount2 == 0 ? 1 : 0);
          e2 = e2.nextInAEL;
        }
      else
        while (e2 != null && !ReferenceEquals(e2, e))
        {
          if (ClipperEngine.GetPolyType(e2) != pt && !ClipperEngine.IsOpen(e2))
            e.windCount2 += e2.windDx;
          e2 = e2.nextInAEL;
        }
    }

    private void SetWindCountForOpenPathEdge(Active e)
    {
      Active? e2 = _actives;
      if (_fillrule == FillRule.EvenOdd)
      {
        int cnt1 = 0, cnt2 = 0;
        while (!ReferenceEquals(e2, e))
        {
          if (ClipperEngine.GetPolyType(e2!) == PathType.Clip)
            cnt2++;
          else if (!ClipperEngine.IsOpen(e2!))
            cnt1++;
          e2 = e2!.nextInAEL;
        }
        e.windCount = (ClipperEngine.IsOdd(cnt1) ? 1 : 0);
        e.windCount2 = (ClipperEngine.IsOdd(cnt2) ? 1 : 0);
      }
      else
      {
        while (!ReferenceEquals(e2, e))
        {
          if (ClipperEngine.GetPolyType(e2!) == PathType.Clip)
            e.windCount2 += e2!.windDx;
          else if (!ClipperEngine.IsOpen(e2!))
            e.windCount += e2!.windDx;
          e2 = e2!.nextInAEL;
        }
      }
    }

    private static bool IsValidAelOrder(Active resident, Active newcomer)
    {
      if (newcomer.currX != resident.currX)
        return newcomer.currX > resident.currX;

      // get the turning direction  a1.top, a2.bot, a2.top
      int i = InternalClipper.CrossProductSign(resident.top, newcomer.bot, newcomer.top);
      if (i != 0) return i < 0;

      // edges must be collinear to get here
      // for starting open paths, place them according to
      // the direction they're about to turn
      if (!ClipperEngine.IsMaxima(resident) && (resident.top.Y > newcomer.top.Y))
      {
        return (InternalClipper.CrossProductSign(newcomer.bot, resident.top,
          ClipperEngine.NextVertex(resident).pt) <= 0);
      }
      else if (!ClipperEngine.IsMaxima(newcomer) && (newcomer.top.Y > resident.top.Y))
      {
        return (InternalClipper.CrossProductSign(newcomer.bot, newcomer.top,
          ClipperEngine.NextVertex(newcomer).pt) >= 0);
      }

      long y = newcomer.bot.Y;
      bool newcomerIsLeft = newcomer.isLeftBound;

      if (resident.bot.Y != y || resident.localMin!.vertex.pt.Y != y)
        return newcomer.isLeftBound;
      // resident must also have just been inserted
      else if (resident.isLeftBound != newcomerIsLeft)
        return newcomerIsLeft;
      else if (InternalClipper.IsCollinear(ClipperEngine.PrevPrevVertex(resident).pt,
        resident.bot, resident.top)) return true;
      else
        // compare turning direction of the alternate bound
        return (InternalClipper.CrossProductSign(ClipperEngine.PrevPrevVertex(resident).pt,
          newcomer.bot, ClipperEngine.PrevPrevVertex(newcomer).pt) > 0) == newcomerIsLeft;
    }

    private void InsertLeftEdge(Active e)
    {
      Active? e2;
      if (_actives == null)
      {
        e.prevInAEL = null;
        e.nextInAEL = null;
        _actives = e;
      }
      else if (!IsValidAelOrder(_actives, e))
      {
        e.prevInAEL = null;
        e.nextInAEL = _actives;
        _actives.prevInAEL = e;
        _actives = e;
      }
      else
      {
        e2 = _actives;
        while (e2.nextInAEL != null && IsValidAelOrder(e2.nextInAEL, e))
          e2 = e2.nextInAEL;
        if (e2.joinWith == JoinWith.Right)
          e2 = e2.nextInAEL;
        if (e2 == null) return; // should never happen
        e.nextInAEL = e2.nextInAEL;
        if (e2.nextInAEL != null) e2.nextInAEL.prevInAEL = e;
        e.prevInAEL = e2;
        e2.nextInAEL = e;
      }
    }

    private static void InsertRightEdge(Active e, Active e2)
    {
      e2.nextInAEL = e.nextInAEL;
      if (e.nextInAEL != null) e.nextInAEL.prevInAEL = e2;
      e2.prevInAEL = e;
      e.nextInAEL = e2;
    }

    private void InsertLocalMinimaIntoAEL(long botY)
    {
      // Add any local minima (if any) at BotY ...
      // nb: horizontal local minima edges should contain locMin.vertex.prev
      while (PopLocalMinima(botY, out LocalMinima? localMinima))
      {
        Active? leftBound, rightBound;
        if ((localMinima!.vertex.flags & VertexFlags.OpenStart) != VertexFlags.Empty)
        {
          leftBound = null;
        }
        else
        {
          leftBound = new Active();
          leftBound.bot = localMinima.vertex.pt;
          leftBound.currX = leftBound.bot.X;
          leftBound.windDx = -1;
          leftBound.vertexTop = localMinima.vertex.prev; // ie descending
          leftBound.top = leftBound.vertexTop!.pt;
          leftBound.localMin = localMinima;
          ClipperEngine.SetDx(leftBound);
        }

        if ((localMinima.vertex.flags & VertexFlags.OpenEnd) != VertexFlags.Empty)
        {
          rightBound = null;
        }
        else
        {
          rightBound = new Active();
          rightBound.bot = localMinima.vertex.pt;
          rightBound.currX = rightBound.bot.X;
          rightBound.windDx = 1;
          rightBound.vertexTop = localMinima.vertex.next; // ie ascending
          rightBound.top = rightBound.vertexTop!.pt;
          rightBound.localMin = localMinima;
          ClipperEngine.SetDx(rightBound);
        }

        // Currently LeftB is just the descending bound and RightB is the ascending.
        // Now if the LeftB isn't on the left of RightB then we need swap them.
        if (leftBound != null && rightBound != null)
        {
          if (ClipperEngine.IsHorizontal(leftBound))
          {
            if (ClipperEngine.IsHeadingRightHorz(leftBound))
              ClipperEngine.SwapActives(ref leftBound, ref rightBound);
          }
          else if (ClipperEngine.IsHorizontal(rightBound))
          {
            if (ClipperEngine.IsHeadingLeftHorz(rightBound))
              ClipperEngine.SwapActives(ref leftBound, ref rightBound);
          }
          else if (leftBound.dx < rightBound.dx)
            ClipperEngine.SwapActives(ref leftBound, ref rightBound);
        }
        else if (leftBound == null)
        {
          leftBound = rightBound;
          rightBound = null;
        }

        bool contributing;
        leftBound!.isLeftBound = true;
        InsertLeftEdge(leftBound);

        if (ClipperEngine.IsOpen(leftBound))
        {
          SetWindCountForOpenPathEdge(leftBound);
          contributing = IsContributingOpen(leftBound);
        }
        else
        {
          SetWindCountForClosedPathEdge(leftBound);
          contributing = IsContributingClosed(leftBound);
        }

        if (rightBound != null)
        {
          rightBound.isLeftBound = false;
          rightBound.windCount = leftBound.windCount;
          rightBound.windCount2 = leftBound.windCount2;
          InsertRightEdge(leftBound, rightBound);
          if (contributing)
          {
            AddLocalMinPoly(leftBound, rightBound, leftBound.bot, true);
            if (!ClipperEngine.IsHorizontal(leftBound))
              CheckJoinLeft(leftBound, leftBound.bot);
          }

          while (rightBound.nextInAEL != null &&
            IsValidAelOrder(rightBound.nextInAEL, rightBound))
          {
            IntersectEdges(rightBound, rightBound.nextInAEL, rightBound.bot);
            SwapPositionsInAEL(rightBound, rightBound.nextInAEL);
          }

          if (ClipperEngine.IsHorizontal(rightBound))
            PushHorz(rightBound);
          else
          {
            CheckJoinRight(rightBound, rightBound.bot);
            InsertScanline(rightBound.top.Y);
          }
        }
        else if (contributing)
        {
          StartOpenPath(leftBound, leftBound.bot);
        }

        if (ClipperEngine.IsHorizontal(leftBound))
          PushHorz(leftBound);
        else
          InsertScanline(leftBound.top.Y);
      } // while (PopLocalMinima())
    }

    private void PushHorz(Active e)
    {
      e.nextInSEL = _sel ?? null;
      _sel = e;
    }

    private bool PopHorz(out Active? e)
    {
      e = _sel;
      if (e == null) return false;
      _sel = _sel!.nextInSEL;
      return true;
    }

    private OutPt AddLocalMinPoly(Active e1, Active e2, Point64 pt, bool isNew = false)
    {
      OutRec outrec = NewOutRec();
      e1.outrec = outrec;
      e2.outrec = outrec;

      if (ClipperEngine.IsOpen(e1))
      {
        outrec.owner = null;
        outrec.isOpen = true;
        if (e1.windDx > 0)
          ClipperEngine.SetSides(outrec, e1, e2);
        else
          ClipperEngine.SetSides(outrec, e2, e1);
      }
      else
      {
        Active? prevHotEdge = ClipperEngine.GetPrevHotEdge(e1);
        // e.windDx is the winding direction of the **input** paths
        // and unrelated to the winding direction of output polygons.
        // Output orientation is determined by e.outrec.frontE which is
        // the ascending edge (see AddLocalMinPoly).
        if (prevHotEdge != null)
        {
          if (_usingPolytree)
            ClipperEngine.SetOwner(outrec, prevHotEdge.outrec!);
          if (ClipperEngine.OutrecIsAscending(prevHotEdge) == isNew)
            ClipperEngine.SetSides(outrec, e2, e1);
          else
            ClipperEngine.SetSides(outrec, e1, e2);
        }
        else
        {
          outrec.owner = null;
          if (isNew)
            ClipperEngine.SetSides(outrec, e1, e2);
          else
            ClipperEngine.SetSides(outrec, e2, e1);
        }
      }

      OutPt op = _outPtPool.Add(pt, outrec);
      outrec.pts = op;
      return op;
    }

    private OutPt? AddLocalMaxPoly(Active e1, Active e2, Point64 pt)
    {
      if (ClipperEngine.IsJoined(e1)) Split(e1, pt);
      if (ClipperEngine.IsJoined(e2)) Split(e2, pt);

      if (ClipperEngine.IsFront(e1) == ClipperEngine.IsFront(e2))
      {
        if (ClipperEngine.IsOpenEnd(e1))
          ClipperEngine.SwapFrontBackSides(e1.outrec!);
        else if (ClipperEngine.IsOpenEnd(e2))
          ClipperEngine.SwapFrontBackSides(e2.outrec!);
        else
        {
          _succeeded = false;
          return null;
        }
      }

      OutPt? result = AddOutPt(e1, pt);
      if (ReferenceEquals(e1.outrec, e2.outrec))
      {
        OutRec outrec = e1.outrec!;
        outrec.pts = result;

        if (_usingPolytree)
        {
          Active? e = ClipperEngine.GetPrevHotEdge(e1);
          if (e == null)
            outrec.owner = null;
          else
            ClipperEngine.SetOwner(outrec, e.outrec!);
          // nb: outRec.owner here is likely NOT the real
          // owner but this will be checked in RecursiveCheckOwners()
        }

        ClipperEngine.UncoupleOutRec(e1);
        result = outrec.pts;
        if (outrec.owner != null && outrec.owner.frontEdge == null)
          outrec.owner = ClipperEngine.GetRealOutRec(outrec.owner);
      }
      // and to preserve the winding orientation of outrec ...
      else if (ClipperEngine.IsOpen(e1))
      {
        if (e1.windDx < 0)
          JoinOutrecPaths(e1, e2);
        else
          JoinOutrecPaths(e2, e1);
      }
      else if (e1.outrec!.idx < e2.outrec!.idx)
        JoinOutrecPaths(e1, e2);
      else
        JoinOutrecPaths(e2, e1);
      return result;
    }

    private void JoinOutrecPaths(Active e1, Active e2)
    {
      // join e2 outrec path onto e1 outrec path and then delete e2 outrec path
      // pointers. (NB Only very rarely do the joining ends share the same coords.)
      OutPt p1st = e1.outrec!.pts!;
      OutPt p2st = e2.outrec!.pts!;
      OutPt p1end = p1st.next;
      OutPt p2end = p2st.next;
      if (ClipperEngine.IsFront(e1))
      {
        p2end.prev = p1st;
        p1st.next = p2end;
        p2st.next = p1end;
        p1end.prev = p2st;
        e1.outrec.pts = p2st;
        e1.outrec.frontEdge = e2.outrec.frontEdge;
        if (e1.outrec.frontEdge != null)
          e1.outrec.frontEdge.outrec = e1.outrec;
      }
      else
      {
        p1end.prev = p2st;
        p2st.next = p1end;
        p1st.next = p2end;
        p2end.prev = p1st;
        e1.outrec.backEdge = e2.outrec.backEdge;
        if (e1.outrec.backEdge != null)
          e1.outrec.backEdge.outrec = e1.outrec;
      }

      // after joining, the e2.OutRec must contains no vertices ...
      e2.outrec.frontEdge = null;
      e2.outrec.backEdge = null;
      e2.outrec.pts = null;

      if (ClipperEngine.IsOpenEnd(e1))
      {
        e2.outrec.pts = e1.outrec.pts;
        e1.outrec.pts = null;
      }
      else
        ClipperEngine.SetOwner(e2.outrec, e1.outrec);

      // and e1 and e2 are maxima and are about to be dropped from the Actives list.
      e1.outrec = null;
      e2.outrec = null;
    }

    private OutRec NewOutRec()
    {
      OutRec result = _outrecPool.Add();
      result.idx = _outrecList.Count;
      _outrecList.Add(result);
      return result;
    }

    private OutPt? AddOutPt(Active e, Point64 pt)
    {
      OutPt? newOp;

      // Outrec.OutPts: a circular doubly-linked-list of POutPt where ...
      // op_front[.Prev]* ~~~> op_back & op_back == op_front.Next
      OutRec outrec = e.outrec!;
      bool toFront = ClipperEngine.IsFront(e);
      OutPt opFront = outrec.pts!;
      OutPt opBack = opFront.next;

      if (toFront)
      {
        if (pt == opFront.pt)
          return opFront;
      }
      else if (pt == opBack.pt)
        return opBack;

      newOp = _outPtPool.Add(pt, outrec);
      opBack.prev = newOp;
      newOp.prev = opFront;
      newOp.next = opBack;
      opFront.next = newOp;
      if (toFront) outrec.pts = newOp;
      return newOp;
    }

    private void CleanCollinear(OutRec? outrec)
    {
      outrec = ClipperEngine.GetRealOutRec(outrec);
      if (outrec == null || outrec.isOpen) return;
      if (!ClipperEngine.IsValidClosedPath(outrec.pts))
      {
        ClipperEngine.DisposeOutPts(outrec);
        return;
      }

      OutPt startOp = outrec.pts!, op2 = startOp;
      for (; ; )
      {
        // NB if preserveCollinear == true, then only remove 180 deg. spikes
        if (InternalClipper.IsCollinear(op2.prev.pt, op2.pt, op2.next.pt) &&
          (op2.pt == op2.prev.pt ||
            op2.pt == op2.next.pt || !_preserveCollinear ||
            InternalClipper.DotProduct(op2.prev.pt, op2.pt, op2.next.pt) < 0))
        {
          if (ReferenceEquals(op2, outrec.pts)) outrec.pts = op2.prev;

          op2 = ClipperEngine.DisposeOutPt(op2)!;
          if (!ClipperEngine.IsValidClosedPath(op2))
          {
            ClipperEngine.DisposeOutPts(outrec);
            return;
          }
          startOp = op2;
          continue;
        }
        op2 = op2.next;
        if (ReferenceEquals(op2, startOp)) break;
      }
      FixSelfIntersects(outrec);
    }

    private void DoSplitOp(OutRec outrec, OutPt splitOp)
    {
      // splitOp.prev -> splitOp &&
      // splitOp.next -> splitOp.next.next are intersecting
      OutPt prevOp = splitOp.prev;
      OutPt nextNextOp = splitOp.next.next;
      outrec.pts = prevOp;

      Point64 ip;
      InternalClipper.GetLineIntersectPt(prevOp.pt, splitOp.pt,
        splitOp.next.pt, nextNextOp.pt, out ip);

#if USINGZ
      if (_zCallback != null) _zCallback(prevOp.pt, splitOp.pt,
        splitOp.next.pt, nextNextOp.pt, ref ip);
#endif
      double area1 = ClipperEngine.Area(outrec.pts);
      double absArea1 = Math.Abs(area1);
      if (absArea1 < 2)
      {
        ClipperEngine.DisposeOutPts(outrec);
        return;
      }

      double area2 = ClipperEngine.AreaTriangle(ip, splitOp.pt, splitOp.next.pt);
      double absArea2 = Math.Abs(area2);

      // de-link splitOp and splitOp.next from the path
      // while inserting the intersection point
      if (ip == prevOp.pt || ip == nextNextOp.pt)
      {
        nextNextOp.prev = prevOp;
        prevOp.next = nextNextOp;
      }
      else
      {
        OutPt newOp2 = _outPtPool.Add(ip, prevOp.outrec);
        newOp2.prev = prevOp;
        newOp2.next = nextNextOp;
        nextNextOp.prev = newOp2;
        prevOp.next = newOp2;
      }

      // area1 is the path's area *before* splitting, whereas area2 is
      // the area of the triangle containing splitOp & splitOp.next.
      // So the only way for these areas to have the same sign is if
      // the split triangle is larger than the path containing prevOp or
      // if there's more than one self-intersection.
      if (absArea2 >= 1 &&
        (absArea2 > absArea1 || (area2 > 0) == (area1 > 0)))
      {
        OutRec newOr = NewOutRec();
        newOr.owner = outrec.owner;

        splitOp.outrec = newOr;
        splitOp.next.outrec = newOr;
        OutPt newOp = _outPtPool.Add(ip, newOr);
        newOp.prev = splitOp.next;
        newOp.next = splitOp;
        newOr.pts = newOp;
        splitOp.prev = newOp;
        splitOp.next.next = newOp;

        if (_usingPolytree)
        {
          if (ClipperEngine.Path2ContainsPath1(prevOp, newOp))
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
      // else: splitOp & splitOp.next are bypassed and become unreachable,
      //       so the GC will reclaim them
    }

    private void FixSelfIntersects(OutRec outrec)
    {
      OutPt op2 = outrec.pts!;
      if (ReferenceEquals(op2.prev, op2.next.next))
        return; // because triangles can't self-intersect
      for (; ; )
      {
        if (InternalClipper.SegsIntersect(op2.prev.pt,
          op2.pt, op2.next.pt, op2.next.next.pt))
        {
          if (ReferenceEquals(op2, outrec.pts) || ReferenceEquals(op2.next, outrec.pts))
            outrec.pts = outrec.pts!.prev;
          DoSplitOp(outrec, op2);
          if (outrec.pts == null) break;
          op2 = outrec.pts;
          if (ReferenceEquals(op2.prev, op2.next.next))
            break; // again, because triangles can't self-intersect
          continue;
        }
        else
          op2 = op2.next;

        if (ReferenceEquals(op2, outrec.pts)) break;
      }
    }

    private static void UpdateOutrecOwner(OutRec outrec)
    {
      OutPt opCurr = outrec.pts!;
      for (; ; )
      {
        opCurr.outrec = outrec;
        opCurr = opCurr.next;
        if (ReferenceEquals(opCurr, outrec.pts)) return;
      }
    }

    private OutPt StartOpenPath(Active e, Point64 pt)
    {
      OutRec outrec = NewOutRec();
      outrec.isOpen = true;

      if (e.windDx > 0)
      {
        outrec.frontEdge = e;
        outrec.backEdge = null;
      }
      else
      {
        outrec.frontEdge = null;
        outrec.backEdge = e;
      }

      e.outrec = outrec;

      OutPt op = _outPtPool.Add(pt, outrec);
      outrec.pts = op;
      return op;
    }

    private static void TrimHorz(Active horzEdge, bool preserveCollinear)
    {
      bool wasTrimmed = false;
      Point64 pt = ClipperEngine.NextVertex(horzEdge).pt;
      while (pt.Y == horzEdge.top.Y)
      {
        // always trim 180 deg. spikes (in closed paths)
        // but otherwise break if preserveCollinear = true
        if (preserveCollinear &&
          ((pt.X < horzEdge.top.X) != (horzEdge.bot.X < horzEdge.top.X)))
          break;

        horzEdge.vertexTop = ClipperEngine.NextVertex(horzEdge);
        horzEdge.top = pt;
        wasTrimmed = true;
        if (ClipperEngine.IsMaxima(horzEdge)) break;
        pt = ClipperEngine.NextVertex(horzEdge).pt;
      }

      if (wasTrimmed) ClipperEngine.SetDx(horzEdge); // +/-infinity
    }

    private void UpdateEdgeIntoAEL(Active e)
    {
      e.bot = e.top;
      e.vertexTop = ClipperEngine.NextVertex(e);
      e.top = e.vertexTop.pt;
      e.currX = e.bot.X;
      ClipperEngine.SetDx(e);

      if (ClipperEngine.IsJoined(e)) Split(e, e.bot);

      if (ClipperEngine.IsHorizontal(e))
      {
        if (!ClipperEngine.IsOpen(e)) TrimHorz(e, _preserveCollinear);
        return;
      }

      InsertScanline(e.top.Y);
      CheckJoinLeft(e, e.bot);
      CheckJoinRight(e, e.bot, true); // (#500)
    }

    private static Active? FindEdgeWithMatchingLocMin(Active e)
    {
      Active? result = e.nextInAEL;
      while (result != null)
      {
        if (ReferenceEquals(result.localMin, e.localMin)) return result;
        else if (!ClipperEngine.IsHorizontal(result) && e.bot != result.bot) result = null;
        else result = result.nextInAEL;
      }
      result = e.prevInAEL;
      while (result != null)
      {
        if (ReferenceEquals(result.localMin, e.localMin)) return result;
        else if (!ClipperEngine.IsHorizontal(result) && e.bot != result.bot) return null;
        else result = result.prevInAEL;
      }
      return result;
    }

    private void IntersectEdges(Active e1, Active e2, Point64 pt)
    {
      // MANAGE OPEN PATH INTERSECTIONS SEPARATELY ...
      if (_hasOpenPaths && (ClipperEngine.IsOpen(e1) || ClipperEngine.IsOpen(e2)))
      {
        if (ClipperEngine.IsOpen(e1) && ClipperEngine.IsOpen(e2)) return;
        Active edgeO, edgeC;
        if (ClipperEngine.IsOpen(e1))
        {
          edgeO = e1;
          edgeC = e2;
        }
        else
        {
          edgeO = e2;
          edgeC = e1;
        }
        if (ClipperEngine.IsJoined(edgeC)) Split(edgeC, pt); // needed for safety

        if (Math.Abs(edgeC.windCount) != 1) return;
        switch (_cliptype)
        {
          case ClipType.Union:
            if (!ClipperEngine.IsHotEdge(edgeC)) return;
            break;
          default:
            if (edgeC.localMin!.polytype == PathType.Subject)
              return;
            break;
        }

        switch (_fillrule)
        {
          case FillRule.Positive:
            if (edgeC.windCount != 1) return;
            break;
          case FillRule.Negative:
            if (edgeC.windCount != -1) return;
            break;
          default:
            if (Math.Abs(edgeC.windCount) != 1) return;
            break;
        }

#if USINGZ
        OutPt? openResultOp;
#endif
        // toggle contribution ...
        if (ClipperEngine.IsHotEdge(edgeO))
        {
#if USINGZ
          openResultOp = AddOutPt(edgeO, pt);
#else
          AddOutPt(edgeO, pt);
#endif
          if (ClipperEngine.IsFront(edgeO)) edgeO.outrec!.frontEdge = null;
          else edgeO.outrec!.backEdge = null;
          edgeO.outrec = null;
        }

        // horizontal edges can pass under open paths at a LocMins
        else if (pt == edgeO.localMin!.vertex.pt &&
          !ClipperEngine.IsOpenEnd(edgeO.localMin.vertex))
        {
          // find the other side of the LocMin and
          // if it's 'hot' join up with it ...
          Active? e3 = FindEdgeWithMatchingLocMin(edgeO);
          if (e3 != null && ClipperEngine.IsHotEdge(e3))
          {
            edgeO.outrec = e3.outrec;
            if (edgeO.windDx > 0)
              ClipperEngine.SetSides(e3.outrec!, edgeO, e3);
            else
              ClipperEngine.SetSides(e3.outrec!, e3, edgeO);
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
        if (_zCallback != null) SetZ(edgeO, edgeC, ref openResultOp!.pt);
#endif
        return;
      } // end of an open path intersection

      // MANAGING CLOSED PATHS FROM HERE ON

      if (ClipperEngine.IsJoined(e1)) Split(e1, pt);
      if (ClipperEngine.IsJoined(e2)) Split(e2, pt);

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
      OutPt? resultOp = null;
#endif
      // if both edges are 'hot' ...
      if (ClipperEngine.IsHotEdge(e1) && ClipperEngine.IsHotEdge(e2))
      {
        if ((oldE1WindCnt != 0 && oldE1WindCnt != 1) || (oldE2WindCnt != 0 && oldE2WindCnt != 1) ||
          (e1.localMin.polytype != e2.localMin.polytype && _cliptype != ClipType.Xor))
        {
#if USINGZ
          resultOp = AddLocalMaxPoly(e1, e2, pt);
          if (_zCallback != null && resultOp != null) SetZ(e1, e2, ref resultOp.pt);
#else
          AddLocalMaxPoly(e1, e2, pt);
#endif
        }
        else if (ClipperEngine.IsFront(e1) || ReferenceEquals(e1.outrec, e2.outrec))
        {
          // this 'else if' condition isn't strictly needed but
          // it's sensible to split polygons that only touch at
          // a common vertex (not at common edges).
#if USINGZ
          resultOp = AddLocalMaxPoly(e1, e2, pt);
          OutPt? op2 = AddLocalMinPoly(e1, e2, pt);
          if (_zCallback != null && resultOp != null) SetZ(e1, e2, ref resultOp.pt);
          if (_zCallback != null) SetZ(e1, e2, ref op2.pt);
#else
          AddLocalMaxPoly(e1, e2, pt);
          AddLocalMinPoly(e1, e2, pt);
#endif
        }
        else
        {
#if USINGZ
          resultOp = AddOutPt(e1, pt);
          OutPt? op2 = AddOutPt(e2, pt);
          if (_zCallback != null)
          {
            SetZ(e1, e2, ref resultOp!.pt);
            SetZ(e1, e2, ref op2!.pt);
          }
#else
          AddOutPt(e1, pt);
          AddOutPt(e2, pt);
#endif
          ClipperEngine.SwapOutrecs(e1, e2);
        }
      }
      else if (ClipperEngine.IsHotEdge(e1))
      {
#if USINGZ
        resultOp = AddOutPt(e1, pt);
        if (_zCallback != null) SetZ(e1, e2, ref resultOp!.pt);
#else
        AddOutPt(e1, pt);
#endif
        ClipperEngine.SwapOutrecs(e1, e2);
      }
      else if (ClipperEngine.IsHotEdge(e2))
      {
#if USINGZ
        resultOp = AddOutPt(e2, pt);
        if (_zCallback != null) SetZ(e1, e2, ref resultOp!.pt);
#else
        AddOutPt(e2, pt);
#endif
        ClipperEngine.SwapOutrecs(e1, e2);
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
          resultOp = AddLocalMinPoly(e1, e2, pt, false);
          if (_zCallback != null) SetZ(e1, e2, ref resultOp.pt);
#else
          AddLocalMinPoly(e1, e2, pt, false);
#endif
        }
        else if (oldE1WindCnt == 1 && oldE2WindCnt == 1)
        {
#if USINGZ
          resultOp = null;
#endif
          switch (_cliptype)
          {
            case ClipType.Union:
              if (e1Wc2 <= 0 && e2Wc2 <= 0)
#if USINGZ
                resultOp = AddLocalMinPoly(e1, e2, pt, false);
#else
                AddLocalMinPoly(e1, e2, pt, false);
#endif
              break;
            case ClipType.Difference:
              if (((ClipperEngine.GetPolyType(e1) == PathType.Clip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                ((ClipperEngine.GetPolyType(e1) == PathType.Subject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
              {
#if USINGZ
                resultOp = AddLocalMinPoly(e1, e2, pt, false);
#else
                AddLocalMinPoly(e1, e2, pt, false);
#endif
              }
              break;
            case ClipType.Xor:
#if USINGZ
              resultOp = AddLocalMinPoly(e1, e2, pt, false);
#else
              AddLocalMinPoly(e1, e2, pt, false);
#endif
              break;
            default:
              if (e1Wc2 > 0 && e2Wc2 > 0)
#if USINGZ
                resultOp = AddLocalMinPoly(e1, e2, pt, false);
#else
                AddLocalMinPoly(e1, e2, pt, false);
#endif
              break;
          }
#if USINGZ
          if (resultOp != null && _zCallback != null) SetZ(e1, e2, ref resultOp.pt);
#endif
        }
      }
    }

    private void DeleteFromAEL(Active e)
    {
      Active? prev = e.prevInAEL;
      Active? next = e.nextInAEL;
      if (prev == null && next == null && !ReferenceEquals(e, _actives)) return; // already deleted
      if (prev != null)
        prev.nextInAEL = next;
      else
        _actives = next;
      if (next != null) next.prevInAEL = prev;
      // nb: the GC reclaims 'e' once it becomes unreferenced
    }

    private void AdjustCurrXAndCopyToSEL(long topY)
    {
      Active? e = _actives;
      _sel = e;
      while (e != null)
      {
        e.prevInSEL = e.prevInAEL;
        e.nextInSEL = e.nextInAEL;
        e.jump = e.nextInSEL;
        // it is safe to ignore 'joined' edges here because
        // if necessary they will be split in IntersectEdges()
        e.currX = ClipperEngine.TopX(e, topY);
        e = e.nextInAEL;
      }
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
        while (PopHorz(out Active? e)) DoHorizontal(e!);
        if (_horzSegList.Count > 0)
        {
          ConvertHorzSegsToJoins();
          _horzSegList.Clear();
        }
        _botY = y; // bot_y_ == bottom of scanbeam
        if (!PopScanline(out y)) break; // y new top of scanbeam
        DoIntersections(y);
        DoTopOfScanbeam(y);
        while (PopHorz(out Active? e2)) DoHorizontal(e2!);
      }
      if (_succeeded) ProcessHorzJoins();
      return _succeeded;
    }

    private static void FixOutRecPts(OutRec outrec)
    {
      OutPt op = outrec.pts!;
      do
      {
        op.outrec = outrec;
        op = op.next;
      } while (!ReferenceEquals(op, outrec.pts));
    }

    private static bool SetHorzSegHeadingForward(HorzSegment hs, OutPt opP, OutPt opN)
    {
      if (opP.pt.X == opN.pt.X) return false;
      if (opP.pt.X < opN.pt.X)
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

    private static bool UpdateHorzSegment(HorzSegment hs)
    {
      OutPt op = hs.leftOp;
      OutRec? outrec = ClipperEngine.GetRealOutRec(op.outrec);
      bool outrecHasEdges = outrec!.frontEdge != null;
      long currY = op.pt.Y;
      OutPt opP = op, opN = op;
      if (outrecHasEdges)
      {
        OutPt opA = outrec.pts!, opZ = opA.next;
        while (!ReferenceEquals(opP, opZ) && opP.prev.pt.Y == currY)
          opP = opP.prev;
        while (!ReferenceEquals(opN, opA) && opN.next.pt.Y == currY)
          opN = opN.next;
      }
      else
      {
        while (!ReferenceEquals(opP.prev, opN) && opP.prev.pt.Y == currY)
          opP = opP.prev;
        while (!ReferenceEquals(opN.next, opP) && opN.next.pt.Y == currY)
          opN = opN.next;
      }
      bool result =
        SetHorzSegHeadingForward(hs, opP, opN) &&
        hs.leftOp.horz == null;

      if (result)
        hs.leftOp.horz = hs;
      else
        hs.rightOp = null; // (for sorting)
      return result;
    }

    private static int HorzSegSorter(HorzSegment hs1, HorzSegment hs2)
    {
      if (hs1.rightOp == null || hs2.rightOp == null) return (hs1.rightOp != null) ? -1 : 1;
      return hs1.leftOp.pt.X.CompareTo(hs2.leftOp.pt.X);
    }

    private void ConvertHorzSegsToJoins()
    {
      int j = 0;
      foreach (HorzSegment hs in _horzSegList)
        if (UpdateHorzSegment(hs)) j++;
      if (j < 2) return;

      // nb: the C++ std::stable_sort places segments with a null right_op *first*
      // (see HorzSegSorter), so 'j' updated segments head the list
      ClipperEngine.StableSort(_horzSegList, HorzSegSorter);

      int hsEnd = j;
      int hsEnd1 = hsEnd - 1;

      for (int i1 = 0; i1 < hsEnd1; i1++)
      {
        HorzSegment hs1 = _horzSegList[i1];
        for (int i2 = i1 + 1; i2 < hsEnd; i2++)
        {
          HorzSegment hs2 = _horzSegList[i2];
          if ((hs2.leftOp.pt.X >= hs1.rightOp!.pt.X) ||
            (hs2.leftToRight == hs1.leftToRight) ||
            (hs2.rightOp!.pt.X <= hs1.leftOp.pt.X)) continue;
          long currY = hs1.leftOp.pt.Y;
          if (hs1.leftToRight)
          {
            while (hs1.leftOp.next.pt.Y == currY &&
              hs1.leftOp.next.pt.X <= hs2.leftOp.pt.X)
              hs1.leftOp = hs1.leftOp.next;
            while (hs2.leftOp.prev.pt.Y == currY &&
              hs2.leftOp.prev.pt.X <= hs1.leftOp.pt.X)
              hs2.leftOp = hs2.leftOp.prev;
            HorzJoin join = new HorzJoin(
              ClipperEngine.DuplicateOp(hs1.leftOp, true, _outPtPool),
              ClipperEngine.DuplicateOp(hs2.leftOp, false, _outPtPool));
            _horzJoinList.Add(join);
          }
          else
          {
            while (hs1.leftOp.prev.pt.Y == currY &&
              hs1.leftOp.prev.pt.X <= hs2.leftOp.pt.X)
              hs1.leftOp = hs1.leftOp.prev;
            while (hs2.leftOp.next.pt.Y == currY &&
              hs2.leftOp.next.pt.X <= hs1.leftOp.pt.X)
              hs2.leftOp = hs2.leftOp.next;
            HorzJoin join = new HorzJoin(
              ClipperEngine.DuplicateOp(hs2.leftOp, true, _outPtPool),
              ClipperEngine.DuplicateOp(hs1.leftOp, false, _outPtPool));
            _horzJoinList.Add(join);
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
        OutRec? or1 = ClipperEngine.GetRealOutRec(j.op1.outrec);
        OutRec? or2 = ClipperEngine.GetRealOutRec(j.op2.outrec);

        OutPt op1b = j.op1.next;
        OutPt op2b = j.op2.prev;
        j.op1.next = j.op2;
        j.op2.prev = j.op1;
        op1b.prev = op2b;
        op2b.next = op1b;

        if (ReferenceEquals(or1, or2)) // 'join' is really a split
        {
          or2 = NewOutRec();
          or2.pts = op1b;
          FixOutRecPts(or2);

          // if or1->pts has moved to or2 then update or1->pts!!
          if (ReferenceEquals(or1!.pts!.outrec, or2))
          {
            or1.pts = j.op1;
            or1.pts.outrec = or1;
          }

          if (_usingPolytree) // #498, #520, #584, D#576, #618
          {
            if (ClipperEngine.Path2ContainsPath1(or1.pts, or2.pts))
            {
              // swap or1's & or2's pts
              OutPt? tmp = or1.pts;
              or1.pts = or2.pts;
              or2.pts = tmp;
              FixOutRecPts(or1);
              FixOutRecPts(or2);
              // or2 is now inside or1
              or2.owner = or1;
            }
            else if (ClipperEngine.Path2ContainsPath1(or2.pts, or1.pts))
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
          or2!.pts = null;
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

    private void AddNewIntersectNode(Active e1, Active e2, long topY)
    {
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
      _intersectNodes.Add(new IntersectNode(e1, e2, ip));
    }

    private bool BuildIntersectList(long topY)
    {
      if (_actives == null || _actives.nextInAEL == null) return false;

      // Calculate edge positions at the top of the current scanbeam, and from this
      // we will determine the intersections required to reach these new positions.
      AdjustCurrXAndCopyToSEL(topY);
      // Find all edge intersections in the current scanbeam using a stable merge
      // sort that ensures only adjacent edges are intersecting. Intersect info is
      // stored in intersect nodes ready to be processed in ProcessIntersectList.
      // Re merge sorts see https://stackoverflow.com/a/46319131/359538

      Active? left = _sel, right, lEnd, rEnd, currBase, tmp;

      while (left != null && left.jump != null)
      {
        Active? prevBase = null;
        while (left != null && left.jump != null)
        {
          currBase = left;
          right = left.jump;
          lEnd = right;
          rEnd = right.jump;
          left.jump = rEnd;
          while (!ReferenceEquals(left, lEnd) && !ReferenceEquals(right, rEnd))
          {
            if (right!.currX < left.currX)
            {
              tmp = right.prevInSEL;
              for (; ; )
              {
                AddNewIntersectNode(tmp!, right, topY);
                if (ReferenceEquals(tmp, left)) break;
                tmp = tmp.prevInSEL;
              }

              tmp = right;
              right = ClipperEngine.ExtractFromSEL(tmp);
              lEnd = right;
              ClipperEngine.Insert1Before2InSEL(tmp, left);
              if (ReferenceEquals(left, currBase))
              {
                currBase = tmp;
                currBase.jump = rEnd;
                if (prevBase == null) _sel = currBase;
                else prevBase.jump = currBase;
              }
            }
            else left = left.nextInSEL;
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
      Span<IntersectNode> nodes = CollectionsMarshal.AsSpan(_intersectNodes);
      IntroSort.Sort(nodes, new IntersectNodeLess());
      // Now as we process these intersections, we must sometimes adjust the order
      // to ensure that intersecting edges are always adjacent ...

      for (int i = 0; i < nodes.Length; i++)
      {
        if (!ClipperEngine.EdgesAdjacentInAEL(nodes[i]))
        {
          int ii = i + 1;
          while (ii < nodes.Length &&
            !ClipperEngine.EdgesAdjacentInAEL(nodes[ii])) ii++;
          if (ii >= nodes.Length) continue;
          (nodes[i], nodes[ii]) = (nodes[ii], nodes[i]);
        }

        IntersectNode node = nodes[i];
        IntersectEdges(node.edge1, node.edge2, node.pt);
        SwapPositionsInAEL(node.edge1, node.edge2);

        node.edge1.currX = node.pt.X;
        node.edge2.currX = node.pt.X;
        CheckJoinLeft(node.edge2, node.pt, true);
        CheckJoinRight(node.edge1, node.pt, true);
      }
    }

    private void SwapPositionsInAEL(Active e1, Active e2)
    {
      // preconditon: e1 must be immediately to the left of e2
      Active? next = e2.nextInAEL;
      if (next != null) next.prevInAEL = e1;
      Active? prev = e1.prevInAEL;
      if (prev != null) prev.nextInAEL = e2;
      e2.prevInAEL = prev;
      e2.nextInAEL = e1;
      e1.prevInAEL = e2;
      e1.nextInAEL = next;
      if (e2.prevInAEL == null) _actives = e2;
    }

    private static OutPt? GetLastOp(Active hotEdge)
    {
      OutRec outrec = hotEdge.outrec!;
      OutPt? result = outrec.pts;
      if (!ReferenceEquals(hotEdge, outrec.frontEdge))
        result = result!.next;
      return result;
    }

    private void AddTrialHorzJoin(OutPt op)
    {
      if (op.outrec.isOpen) return;
      _horzSegList.Add(new HorzSegment(op));
    }

    private bool ResetHorzDirection(Active horz, Vertex? maxVertex,
      out long horzLeft, out long horzRight)
    {
      if (horz.bot.X == horz.top.X)
      {
        // the horizontal edge is going nowhere ...
        horzLeft = horz.currX;
        horzRight = horz.currX;
        Active? e = horz.nextInAEL;
        while (e != null && !ReferenceEquals(e.vertexTop, maxVertex)) e = e.nextInAEL;
        return e != null;
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

    private void DoHorizontal(Active horz)
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
      Point64 pt;
      bool horzIsOpen = ClipperEngine.IsOpen(horz);
      long y = horz.bot.Y;
      Vertex? vertexMax;
      if (horzIsOpen)
        vertexMax = ClipperEngine.GetCurrYMaximaVertex_Open(horz);
      else
        vertexMax = ClipperEngine.GetCurrYMaximaVertex(horz);

      bool isLeftToRight =
        ResetHorzDirection(horz, vertexMax, out long horzLeft, out long horzRight);

      if (ClipperEngine.IsHotEdge(horz))
      {
#if USINGZ
        OutPt op = AddOutPt(horz, new Point64(horz.currX, y, horz.bot.Z))!;
#else
        OutPt op = AddOutPt(horz, new Point64(horz.currX, y))!;
#endif
        AddTrialHorzJoin(op);
      }

      while (true) // loop through consec. horizontal edges
      {
        Active? e;
        if (isLeftToRight) e = horz.nextInAEL;
        else e = horz.prevInAEL;

        while (e != null)
        {
          if (ReferenceEquals(e.vertexTop, vertexMax))
          {
            if (ClipperEngine.IsHotEdge(horz) && ClipperEngine.IsJoined(e))
              Split(e, e.top);

            if (ClipperEngine.IsHotEdge(horz))
            {
              while (!ReferenceEquals(horz.vertexTop, vertexMax))
              {
                AddOutPt(horz, horz.top);
                UpdateEdgeIntoAEL(horz);
              }
              if (isLeftToRight)
                AddLocalMaxPoly(horz, e, horz.top);
              else
                AddLocalMaxPoly(e, horz, horz.top);
            }
            DeleteFromAEL(e);
            DeleteFromAEL(horz);
            return;
          }

          // if horzEdge is a maxima, keep going until we reach
          // its maxima pair, otherwise check for break conditions
          if (!ReferenceEquals(vertexMax, horz.vertexTop) || ClipperEngine.IsOpenEnd(horz))
          {
            // otherwise stop when 'ae' is beyond the end of the horizontal line
            if ((isLeftToRight && e.currX > horzRight) ||
              (!isLeftToRight && e.currX < horzLeft)) break;

            if (e.currX == horz.top.X && !ClipperEngine.IsHorizontal(e))
            {
              pt = ClipperEngine.NextVertex(horz).pt;
              if (isLeftToRight)
              {
                // with open paths we'll only break once past horz's end
                if (ClipperEngine.IsOpen(e) && !ClipperEngine.IsSamePolyType(e, horz) &&
                  !ClipperEngine.IsHotEdge(e))
                {
                  if (ClipperEngine.TopX(e, pt.Y) > pt.X) break;
                }
                // otherwise we'll only break when horz's outslope is greater than e's
                else if (ClipperEngine.TopX(e, pt.Y) >= pt.X) break;
              }
              else
              {
                if (ClipperEngine.IsOpen(e) && !ClipperEngine.IsSamePolyType(e, horz) &&
                  !ClipperEngine.IsHotEdge(e))
                {
                  if (ClipperEngine.TopX(e, pt.Y) < pt.X) break;
                }
                else if (ClipperEngine.TopX(e, pt.Y) <= pt.X) break;
              }
            }
          }

          pt = new Point64(e.currX, horz.bot.Y);
          if (isLeftToRight)
          {
            IntersectEdges(horz, e, pt);
            SwapPositionsInAEL(horz, e);
            CheckJoinLeft(e, pt);
            horz.currX = e.currX;
            e = horz.nextInAEL;
          }
          else
          {
            IntersectEdges(e, horz, pt);
            SwapPositionsInAEL(e, horz);
            CheckJoinRight(e, pt);
            horz.currX = e.currX;
            e = horz.prevInAEL;
          }

          if (horz.outrec != null)
          {
            // nb: The outrec containing the op returned by IntersectEdges
            // above may no longer be associated with horzEdge.
            AddTrialHorzJoin(GetLastOp(horz)!);
          }
        }

        // check if we've finished with (consecutive) horizontals ...
        if (horzIsOpen && ClipperEngine.IsOpenEnd(horz)) // ie open at top
        {
          if (ClipperEngine.IsHotEdge(horz))
          {
            AddOutPt(horz, horz.top);
            if (ClipperEngine.IsFront(horz))
              horz.outrec!.frontEdge = null;
            else
              horz.outrec!.backEdge = null;
            horz.outrec = null;
          }
          DeleteFromAEL(horz);
          return;
        }
        else if (ClipperEngine.NextVertex(horz).pt.Y != horz.top.Y)
          break;

        // still more horizontals in bound to process ...
        if (ClipperEngine.IsHotEdge(horz))
          AddOutPt(horz, horz.top);
        UpdateEdgeIntoAEL(horz);

        isLeftToRight =
          ResetHorzDirection(horz, vertexMax, out horzLeft, out horzRight);
      }

      if (ClipperEngine.IsHotEdge(horz))
      {
        OutPt op = AddOutPt(horz, horz.top)!;
        AddTrialHorzJoin(op);
      }

      UpdateEdgeIntoAEL(horz); // end of an intermediate horiz.
    }

    private void DoTopOfScanbeam(long y)
    {
      _sel = null; // sel_ is reused to flag horizontals (see PushHorz below)
      Active? e = _actives;
      while (e != null)
      {
        // nb: 'e' will never be horizontal here
        if (e.top.Y == y)
        {
          e.currX = e.top.X;
          if (ClipperEngine.IsMaxima(e))
          {
            e = DoMaxima(e); // TOP OF BOUND (MAXIMA)
            continue;
          }
          else
          {
            // INTERMEDIATE VERTEX ...
            if (ClipperEngine.IsHotEdge(e)) AddOutPt(e, e.top);
            UpdateEdgeIntoAEL(e);
            if (ClipperEngine.IsHorizontal(e))
              PushHorz(e); // horizontals are processed later
          }
        }
        else // i.e. not the top of the edge
          e.currX = ClipperEngine.TopX(e, y);

        e = e.nextInAEL;
      }
    }

    private Active? DoMaxima(Active e)
    {
      Active? nextE, prevE, maxPair;
      prevE = e.prevInAEL;
      nextE = e.nextInAEL;
      if (ClipperEngine.IsOpenEnd(e))
      {
        if (ClipperEngine.IsHotEdge(e)) AddOutPt(e, e.top);
        if (!ClipperEngine.IsHorizontal(e))
        {
          if (ClipperEngine.IsHotEdge(e))
          {
            if (ClipperEngine.IsFront(e))
              e.outrec!.frontEdge = null;
            else
              e.outrec!.backEdge = null;
            e.outrec = null;
          }
          DeleteFromAEL(e);
        }
        return nextE;
      }

      maxPair = ClipperEngine.GetMaximaPair(e);
      if (maxPair == null) return nextE; // eMaxPair is horizontal

      if (ClipperEngine.IsJoined(e)) Split(e, e.top);
      if (ClipperEngine.IsJoined(maxPair)) Split(maxPair, maxPair.top);

      // only non-horizontal maxima here.
      // process any edges between maxima pair ...
      while (!ReferenceEquals(nextE, maxPair))
      {
        IntersectEdges(e, nextE!, e.top);
        SwapPositionsInAEL(e, nextE!);
        nextE = e.nextInAEL;
      }

      if (ClipperEngine.IsOpen(e))
      {
        if (ClipperEngine.IsHotEdge(e))
          AddLocalMaxPoly(e, maxPair, e.top);
        DeleteFromAEL(maxPair);
        DeleteFromAEL(e);
        return (prevE != null ? prevE.nextInAEL : _actives);
      }

      // e.next_in_ael == max_pair ...
      if (ClipperEngine.IsHotEdge(e))
        AddLocalMaxPoly(e, maxPair, e.top);

      DeleteFromAEL(e);
      DeleteFromAEL(maxPair);
      return (prevE != null ? prevE.nextInAEL : _actives);
    }

    private void Split(Active e, Point64 pt)
    {
      if (e.joinWith == JoinWith.Right)
      {
        e.joinWith = JoinWith.NoJoin;
        e.nextInAEL!.joinWith = JoinWith.NoJoin;
        AddLocalMinPoly(e, e.nextInAEL, pt, true);
      }
      else
      {
        e.joinWith = JoinWith.NoJoin;
        e.prevInAEL!.joinWith = JoinWith.NoJoin;
        AddLocalMinPoly(e.prevInAEL, e, pt, true);
      }
    }

    private void CheckJoinLeft(Active e, Point64 pt, bool checkCurrX = false)
    {
      Active? prev = e.prevInAEL;
      if (prev == null ||
        !ClipperEngine.IsHotEdge(e) || !ClipperEngine.IsHotEdge(prev) ||
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
        AddLocalMaxPoly(prev, e, pt);
      else if (e.outrec.idx < prev.outrec.idx)
        JoinOutrecPaths(e, prev);
      else
        JoinOutrecPaths(prev, e);
      prev.joinWith = JoinWith.Right;
      e.joinWith = JoinWith.Left;
    }

    private void CheckJoinRight(Active e, Point64 pt, bool checkCurrX = false)
    {
      Active? next = e.nextInAEL;
      if (next == null ||
        !ClipperEngine.IsHotEdge(e) || !ClipperEngine.IsHotEdge(next) ||
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
        AddLocalMaxPoly(e, next, pt);
      else if (e.outrec.idx < next.outrec.idx)
        JoinOutrecPaths(e, next);
      else
        JoinOutrecPaths(next, e);

      e.joinWith = JoinWith.Right;
      next.joinWith = JoinWith.Left;
    }

    private bool CheckBounds(OutRec outrec)
    {
      if (outrec.pts == null) return false;
      if (!outrec.bounds.IsEmpty()) return true;
      // nb: CleanCollinear (called below) can add OutRecs to the list
      CleanCollinear(outrec);
      if (outrec.pts == null ||
        !ClipperEngine.BuildPath64(outrec.pts, _reverseSolution, false, outrec.path))
        return false;
      outrec.bounds = InternalClipper.GetBounds(outrec.path);
      return true;
    }

    private bool CheckSplitOwner(OutRec outrec, List<OutRec> splits)
    {
      // nb: use indexing (not an iterator) in case 'splits' is modified inside this loop (#1029)
      for (int idx = 0; idx < splits.Count; ++idx)
      {
        OutRec split = splits[idx];
        if (split.pts == null && split.splits != null &&
          CheckSplitOwner(outrec, split.splits)) return true; // #942
        OutRec? realSplit = ClipperEngine.GetRealOutRec(split);
        if (realSplit == null || ReferenceEquals(realSplit, outrec) ||
          ReferenceEquals(realSplit.recursiveSplit, outrec)) continue;
        realSplit.recursiveSplit = outrec; // prevent infinite loops

        if (realSplit.splits != null && CheckSplitOwner(outrec, realSplit.splits))
          return true;

        if (!CheckBounds(realSplit) || !realSplit.bounds.Contains(outrec.bounds) ||
          !ClipperEngine.Path2ContainsPath1(outrec.pts!, realSplit.pts!)) continue;

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
        if (outrec.owner.pts != null && CheckBounds(outrec.owner) &&
          outrec.owner.bounds.Contains(outrec.bounds) &&
          ClipperEngine.Path2ContainsPath1(outrec.pts!, outrec.owner.pts)) break;
        outrec.owner = outrec.owner.owner;
      }

      if (outrec.owner != null)
      {
        if (outrec.owner.polypath == null)
          RecursiveCheckOwners(outrec.owner, polypath);
        outrec.polypath = outrec.owner.polypath!.AddChild(outrec.path);
      }
      else
        outrec.polypath = polypath.AddChild(outrec.path);
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
        if (outrec.pts == null) continue;

        Path64 path = new Path64();
        if (solutionOpen != null && outrec.isOpen)
        {
          if (ClipperEngine.BuildPath64(outrec.pts, _reverseSolution, true, path))
            solutionOpen.Add(path);
        }
        else
        {
          // nb: CleanCollinear can add to outrec_list_
          CleanCollinear(outrec);
          // closed paths should always return a Positive orientation
          if (ClipperEngine.BuildPath64(outrec.pts, _reverseSolution, false, path))
            solutionClosed.Add(path);
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
        if (outrec.pts == null) continue;

        if (outrec.isOpen)
        {
          Path64 path = new Path64();
          if (ClipperEngine.BuildPath64(outrec.pts, _reverseSolution, true, path))
            openPaths.Add(path);
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
        if (outrec.pts == null) continue;

        PathD path = new PathD();
        if (solutionOpen != null && outrec.isOpen)
        {
          if (ClipperEngine.BuildPathD(outrec.pts, _reverseSolution, true, path, invScale))
            solutionOpen.Add(path);
        }
        else
        {
          CleanCollinear(outrec);
          // closed paths should always return a Positive orientation
          if (ClipperEngine.BuildPathD(outrec.pts, _reverseSolution, false, path, invScale))
            solutionClosed.Add(path);
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
        if (outrec.pts == null) continue;

        if (outrec.isOpen)
        {
          PathD path = new PathD();
          if (ClipperEngine.BuildPathD(outrec.pts, _reverseSolution, true, path, invScale))
            openPaths.Add(path);
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
      newChild.Polygon = Clipper.ScalePathD(p, 1 / Scale);
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
