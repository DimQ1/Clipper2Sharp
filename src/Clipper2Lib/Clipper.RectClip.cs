/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  11 October 2025                                                 *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  FAST rectangular clipping                                       *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.rectclip.h + src/clipper.rectclip.cpp (2.0.1)    *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  public class OutPt2
  {
    public Point64 pt;
    public int ownerIdx = 0;
    public List<OutPt2?>? edge = null;
    public OutPt2? next = null;
    public OutPt2? prev = null;
  }

  //------------------------------------------------------------------------------
  // RectClip64
  //------------------------------------------------------------------------------

  public class RectClip64
  {
    // Location: the order is important here, see StartLocsAreClockwise()
    protected enum Location { Left, Top, Right, Bottom, Inside }

    protected readonly Rect64 rect_;
    protected readonly Path64 rect_as_path_;
    protected readonly Point64 rect_mp_;
    protected Rect64 path_bounds_;
    protected readonly List<OutPt2?> results_ = new List<OutPt2?>(); // each path can be broken into multiples
    protected readonly List<OutPt2?>[] edges_ = new List<OutPt2?>[8]; // clockwise and counter-clockwise
    protected readonly List<Location> start_locs_ = new List<Location>();

    /// <summary>
    /// Working storage of a clipping batch: the out-points of one path are all
    /// dead once its result paths have been built, so they are recycled for the
    /// next path, and result paths are gathered in a scratch path and copied out
    /// at their exact size. One instance per thread is kept (rented for the
    /// duration of an Execute, so a re-entrant or parallel use gets its own).
    /// </summary>
    private sealed class Work
    {
      public readonly List<OutPt2> pool = new List<OutPt2>();
      public int used;
      public int highWater;   // out-points handed out since the last Return
      public readonly Path64 scratch = new Path64();

      [ThreadStatic] private static Work? t_cached;

      public static Work Rent()
      {
        Work? w = t_cached;
        if (w == null) return new Work();
        t_cached = null;
        return w;
      }

      public static void Return(Work w)
      {
        if (w.used > w.highWater) w.highWater = w.used;
        w.used = 0;
        w.scratch.Clear();
        // don't keep a huge pool alive
        if (w.pool.Count > 1 << 16) return;
        // drop the references into this batch's (now dead) state
        for (int i = 0; i < w.highWater; i++)
        {
          OutPt2 op = w.pool[i];
          op.next = null; op.prev = null; op.edge = null;
        }
        w.highWater = 0;
        t_cached = w;
      }
    }

    private Work? work_;

    private protected void RentWork() { work_ ??= Work.Rent(); }

    private protected void ReturnWork()
    {
      if (work_ == null) return;
      Work.Return(work_);
      work_ = null;
    }

    private OutPt2 NewOutPt2(Point64 pt)
    {
      OutPt2 op;
      Work? w = work_;
      if (w == null)
        op = new OutPt2();
      else if (w.used < w.pool.Count)
      {
        op = w.pool[w.used++];
        op.ownerIdx = 0;
        op.edge = null;
      }
      else
      {
        op = new OutPt2();
        w.pool.Add(op);
        w.used++;
      }
      op.pt = pt;
      return op;
    }

    /// <summary>Makes the out-points of the previous path available again.</summary>
    private protected void RecycleOutPts()
    {
      if (work_ == null) return;
      if (work_.used > work_.highWater) work_.highWater = work_.used;
      work_.used = 0;
    }

    private protected Path64 PathScratch
    {
      get
      {
        if (work_ == null) return new Path64();
        work_.scratch.Clear();
        return work_.scratch;
      }
    }

    /// <summary>Returns the gathered path at its exact size.</summary>
    private protected Path64 TakeScratchPath(Path64 gathered)
    {
      if (work_ == null || !ReferenceEquals(gathered, work_.scratch)) return gathered;
      Path64 result = new Path64(gathered.Count);
      result.AddRange(gathered);
      gathered.Clear();
      return result;
    }

    public RectClip64(Rect64 rect)
    {
      rect_ = rect;
      rect_as_path_ = rect.AsPath();
      rect_mp_ = rect.MidPoint();
      for (int i = 0; i < 8; i++)
        edges_[i] = new List<OutPt2?>();
    }

    // Miscellaneous helpers -----------------------------------------------------

    protected static bool Path1ContainsPath2(Path64 path1, Path64 path2)
    {
      int ioCount = 0;
      // precondition: no (significant) overlap
      foreach (Point64 pt in path2)
      {
        PointInPolygonResult pip = InternalClipper.PointInPolygon(pt, path1);
        switch (pip)
        {
          case PointInPolygonResult.IsOutside: ++ioCount; break;
          case PointInPolygonResult.IsInside: --ioCount; break;
          default: continue;
        }
        if (Math.Abs(ioCount) > 1) break;
      }
      return ioCount <= 0;
    }

    protected static bool GetLocation(Rect64 rec, Point64 pt, out Location loc)
    {
      if (pt.X == rec.left && pt.Y >= rec.top && pt.Y <= rec.bottom)
      {
        loc = Location.Left;
        return false;
      }
      else if (pt.X == rec.right && pt.Y >= rec.top && pt.Y <= rec.bottom)
      {
        loc = Location.Right;
        return false;
      }
      else if (pt.Y == rec.top && pt.X >= rec.left && pt.X <= rec.right)
      {
        loc = Location.Top;
        return false;
      }
      else if (pt.Y == rec.bottom && pt.X >= rec.left && pt.X <= rec.right)
      {
        loc = Location.Bottom;
        return false;
      }
      else if (pt.X < rec.left) loc = Location.Left;
      else if (pt.X > rec.right) loc = Location.Right;
      else if (pt.Y < rec.top) loc = Location.Top;
      else if (pt.Y > rec.bottom) loc = Location.Bottom;
      else loc = Location.Inside;
      return true;
    }

    protected static bool IsHorizontal(Point64 pt1, Point64 pt2)
    {
      return pt1.Y == pt2.Y;
    }

    protected static bool GetSegmentIntersection(Point64 p1,
      Point64 p2, Point64 p3, Point64 p4, out Point64 ip)
    {
      ip = new Point64();
      int res1 = InternalClipper.CrossProductSign(p1, p3, p4);
      int res2 = InternalClipper.CrossProductSign(p2, p3, p4);
      if (res1 == 0)
      {
        ip = p1;
        if (res2 == 0) return false; // segments are collinear
        else if (p1 == p3 || p1 == p4) return true;
        else if (IsHorizontal(p3, p4)) return ((p1.X > p3.X) == (p1.X < p4.X));
        else return ((p1.Y > p3.Y) == (p1.Y < p4.Y));
      }
      else if (res2 == 0)
      {
        ip = p2;
        if (p2 == p3 || p2 == p4) return true;
        else if (IsHorizontal(p3, p4)) return ((p2.X > p3.X) == (p2.X < p4.X));
        else return ((p2.Y > p3.Y) == (p2.Y < p4.Y));
      }
      if ((res1 > 0) == (res2 > 0)) return false;

      int res3 = InternalClipper.CrossProductSign(p3, p1, p2);
      int res4 = InternalClipper.CrossProductSign(p4, p1, p2);
      if (res3 == 0)
      {
        ip = p3;
        if (p3 == p1 || p3 == p2) return true;
        else if (IsHorizontal(p1, p2)) return ((p3.X > p1.X) == (p3.X < p2.X));
        else return ((p3.Y > p1.Y) == (p3.Y < p2.Y));
      }
      else if (res4 == 0)
      {
        ip = p4;
        if (p4 == p1 || p4 == p2) return true;
        else if (IsHorizontal(p1, p2)) return ((p4.X > p1.X) == (p4.X < p2.X));
        else return ((p4.Y > p1.Y) == (p4.Y < p2.Y));
      }
      if ((res3 > 0) == (res4 > 0)) return false;

      // segments must intersect to get here
      return InternalClipper.GetLineIntersectPt(p1, p2, p3, p4, out ip);
    }

    protected static bool GetIntersection(Path64 rectPath,
      Point64 p, Point64 p2, ref Location loc, out Point64 ip)
    {
      // gets the intersection closest to 'p'
      // when Result = false, loc will remain unchanged
      switch (loc)
      {
        case Location.Left:
          if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip)) return true;
          else if ((p.Y < rectPath[0].Y) &&
            GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
          {
            loc = Location.Top;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
          {
            loc = Location.Bottom;
            return true;
          }
          else return false;

        case Location.Top:
          if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip)) return true;
          else if ((p.X < rectPath[0].X) &&
            GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
          {
            loc = Location.Left;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
          {
            loc = Location.Right;
            return true;
          }
          else return false;

        case Location.Right:
          if (GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip)) return true;
          else if ((p.Y < rectPath[1].Y) &&
            GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
          {
            loc = Location.Top;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
          {
            loc = Location.Bottom;
            return true;
          }
          else return false;

        case Location.Bottom:
          if (GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip)) return true;
          else if ((p.X < rectPath[3].X) &&
            GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
          {
            loc = Location.Left;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
          {
            loc = Location.Right;
            return true;
          }
          else return false;

        default: // loc == Inside
          if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
          {
            loc = Location.Left;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
          {
            loc = Location.Top;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
          {
            loc = Location.Right;
            return true;
          }
          else if (GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
          {
            loc = Location.Bottom;
            return true;
          }
          else return false;
      }
    }

    protected static Location GetAdjacentLocation(Location loc, bool isClockwise)
    {
      int delta = (isClockwise) ? 1 : 3;
      return (Location) (((int) loc + delta) % 4);
    }

    protected static bool HeadingClockwise(Location prev, Location curr)
    {
      return ((int) prev + 1) % 4 == (int) curr;
    }

    protected static bool AreOpposites(Location prev, Location curr)
    {
      return Math.Abs((int) prev - (int) curr) == 2;
    }

    protected static bool IsClockwise(Location prev, Location curr,
      Point64 prevPt, Point64 currPt, Point64 rectMp)
    {
      if (AreOpposites(prev, curr))
        return InternalClipper.CrossProductSign(prevPt, rectMp, currPt) < 0;
      else
        return HeadingClockwise(prev, curr);
    }

    protected static OutPt2? UnlinkOp(OutPt2 op)
    {
      if (ReferenceEquals(op.next, op)) return null;
      op.prev!.next = op.next;
      op.next!.prev = op.prev;
      return op.next;
    }

    protected static OutPt2? UnlinkOpBack(OutPt2 op)
    {
      if (ReferenceEquals(op.next, op)) return null;
      op.prev!.next = op.next;
      op.next!.prev = op.prev;
      return op.prev;
    }

    protected static uint GetEdgesForPt(Point64 pt, Rect64 rec)
    {
      uint result = 0;
      if (pt.X == rec.left) result = 1;
      else if (pt.X == rec.right) result = 4;
      if (pt.Y == rec.top) result += 2;
      else if (pt.Y == rec.bottom) result += 8;
      return result;
    }

    protected static bool IsHeadingClockwise(Point64 pt1, Point64 pt2, int edgeIdx)
    {
      switch (edgeIdx)
      {
        case 0: return pt2.Y < pt1.Y;
        case 1: return pt2.X > pt1.X;
        case 2: return pt2.Y > pt1.Y;
        default: return pt2.X < pt1.X;
      }
    }

    protected static bool HasHorzOverlap(Point64 left1, Point64 right1,
      Point64 left2, Point64 right2)
    {
      return (left1.X < right2.X) && (right1.X > left2.X);
    }

    protected static bool HasVertOverlap(Point64 top1, Point64 bottom1,
      Point64 top2, Point64 bottom2)
    {
      return (top1.Y < bottom2.Y) && (bottom1.Y > top2.Y);
    }

    protected static void AddToEdge(List<OutPt2?> edge, OutPt2 op)
    {
      if (op.edge != null) return;
      op.edge = edge;
      edge.Add(op);
    }

    protected static void UncoupleEdge(OutPt2 op)
    {
      if (op.edge == null) return;
      for (int i = 0; i < op.edge.Count; ++i)
      {
        OutPt2? op2 = op.edge[i];
        if (ReferenceEquals(op2, op))
        {
          op.edge[i] = null;
          break;
        }
      }
      op.edge = null;
    }

    protected static void SetNewOwner(OutPt2 op, int newIdx)
    {
      op.ownerIdx = newIdx;
      OutPt2 op2 = op.next!;
      while (!ReferenceEquals(op2, op))
      {
        op2.ownerIdx = newIdx;
        op2 = op2.next!;
      }
    }

    protected OutPt2 Add(Point64 pt, bool startNew = false)
    {
      // this method is only called by ExecuteInternal.
      // Later splitting & rejoining won't create additional op's,
      // though they will change the (non-storage) results_ count.
      int currIdx = results_.Count;
      OutPt2 result;
      if (currIdx == 0 || startNew)
      {
        result = NewOutPt2(pt);
        result.next = result;
        result.prev = result;
        results_.Add(result);
      }
      else
      {
        --currIdx;
        OutPt2 prevOp = results_[currIdx]!;
        if (prevOp.pt == pt) return prevOp;
        result = NewOutPt2(pt);
        result.ownerIdx = currIdx;
        result.next = prevOp.next;
        prevOp.next!.prev = result;
        prevOp.next = result;
        result.prev = prevOp;
        results_[currIdx] = result;
      }
      return result;
    }

    private void AddCorner(Location prev, Location curr)
    {
      if (HeadingClockwise(prev, curr))
        Add(rect_as_path_[(int) prev]);
      else
        Add(rect_as_path_[(int) curr]);
    }

    private void AddCorner(ref Location loc, bool isClockwise)
    {
      if (isClockwise)
      {
        Add(rect_as_path_[(int) loc]);
        loc = GetAdjacentLocation(loc, true);
      }
      else
      {
        loc = GetAdjacentLocation(loc, false);
        Add(rect_as_path_[(int) loc]);
      }
    }

    protected void GetNextLocation(Path64 path, ref Location loc, ref int i, int highI)
    {
      switch (loc)
      {
        case Location.Left:
          while (i <= highI && path[i].X <= rect_.left) ++i;
          if (i > highI) break;
          else if (path[i].X >= rect_.right) loc = Location.Right;
          else if (path[i].Y <= rect_.top) loc = Location.Top;
          else if (path[i].Y >= rect_.bottom) loc = Location.Bottom;
          else loc = Location.Inside;
          break;

        case Location.Top:
          while (i <= highI && path[i].Y <= rect_.top) ++i;
          if (i > highI) break;
          else if (path[i].Y >= rect_.bottom) loc = Location.Bottom;
          else if (path[i].X <= rect_.left) loc = Location.Left;
          else if (path[i].X >= rect_.right) loc = Location.Right;
          else loc = Location.Inside;
          break;

        case Location.Right:
          while (i <= highI && path[i].X >= rect_.right) ++i;
          if (i > highI) break;
          else if (path[i].X <= rect_.left) loc = Location.Left;
          else if (path[i].Y <= rect_.top) loc = Location.Top;
          else if (path[i].Y >= rect_.bottom) loc = Location.Bottom;
          else loc = Location.Inside;
          break;

        case Location.Bottom:
          while (i <= highI && path[i].Y >= rect_.bottom) ++i;
          if (i > highI) break;
          else if (path[i].Y <= rect_.top) loc = Location.Top;
          else if (path[i].X <= rect_.left) loc = Location.Left;
          else if (path[i].X >= rect_.right) loc = Location.Right;
          else loc = Location.Inside;
          break;

        case Location.Inside:
          while (i <= highI)
          {
            if (path[i].X < rect_.left) loc = Location.Left;
            else if (path[i].X > rect_.right) loc = Location.Right;
            else if (path[i].Y > rect_.bottom) loc = Location.Bottom;
            else if (path[i].Y < rect_.top) loc = Location.Top;
            else { Add(path[i]); ++i; continue; }
            break; // inner loop
          }
          break;
      } // switch
    }

    protected static bool StartLocsAreClockwise(List<Location> startLocs)
    {
      int result = 0;
      for (int i = 1; i < startLocs.Count; ++i)
      {
        int d = (int) startLocs[i] - (int) startLocs[i - 1];
        switch (d)
        {
          case -1: result -= 1; break;
          case 1: result += 1; break;
          case -3: result += 1; break;
          case 3: result -= 1; break;
        }
      }
      return result > 0;
    }

    protected virtual void ExecuteInternal(Path64 path)
    {
      if (path.Count < 1)
        return;

      int highI = path.Count - 1;
      Location prev = Location.Inside, loc;
      Location crossingLoc = Location.Inside;
      Location firstCross = Location.Inside;
      if (!GetLocation(rect_, path[highI], out loc))
      {
        int i = highI;
        while (i > 0 && !GetLocation(rect_, path[i - 1], out prev))
          --i;
        if (i == 0)
        {
          // all of path must be inside fRect
          foreach (Point64 pt in path) Add(pt);
          return;
        }
        if (prev == Location.Inside) loc = Location.Inside;
      }
      Location startingLoc = loc;

      ///////////////////////////////////////////////////
      int ii = 0;
      while (ii <= highI)
      {
        prev = loc;
        Location crossingPrev = crossingLoc;

        GetNextLocation(path, ref loc, ref ii, highI);

        if (ii > highI) break;
        Point64 ip, ip2;
        Point64 prevPt = (ii > 0) ?
          path[ii - 1] :
          path[highI];

        crossingLoc = loc;
        if (!GetIntersection(rect_as_path_,
          path[ii], prevPt, ref crossingLoc, out ip))
        {
          // ie remaining outside
          if (crossingPrev == Location.Inside)
          {
            bool isClockw = IsClockwise(prev, loc, prevPt, path[ii], rect_mp_);
            do
            {
              start_locs_.Add(prev);
              prev = GetAdjacentLocation(prev, isClockw);
            } while (prev != loc);
            crossingLoc = crossingPrev; // still not crossed
          }
          else if (prev != Location.Inside && prev != loc)
          {
            bool isClockw = IsClockwise(prev, loc, prevPt, path[ii], rect_mp_);
            do
            {
              AddCorner(ref prev, isClockw);
            } while (prev != loc);
          }          ++ii;
          continue;
        }

        ////////////////////////////////////////////////////
        // we must be crossing the rect boundary to get here
        ////////////////////////////////////////////////////

        if (loc == Location.Inside) // path must be entering rect
        {
          if (firstCross == Location.Inside)
          {
            firstCross = crossingLoc;
            start_locs_.Add(prev);
          }
          else if (prev != crossingLoc)
          {
            bool isClockw = IsClockwise(prev, crossingLoc, prevPt, path[ii], rect_mp_);
            do
            {
              AddCorner(ref prev, isClockw);
            } while (prev != crossingLoc);
          }
        }
        else if (prev != Location.Inside)
        {
          // passing right through rect. 'ip' here will be the second
          // intersect pt but we'll also need the first intersect pt (ip2)
          loc = prev;
          GetIntersection(rect_as_path_, prevPt, path[ii], ref loc, out ip2);
          if (crossingPrev != Location.Inside && crossingPrev != loc) //579
            AddCorner(crossingPrev, loc);

          if (firstCross == Location.Inside)
          {
            firstCross = loc;
            start_locs_.Add(prev);
          }

          loc = crossingLoc;
          Add(ip2);
          if (ip == ip2)
          {
            // it's very likely that path[i] is on rect
            GetLocation(rect_, path[ii], out loc);
            AddCorner(crossingLoc, loc);
            crossingLoc = loc;
            continue;
          }
        }
        else // path must be exiting rect
        {
          loc = crossingLoc;
          if (firstCross == Location.Inside)
            firstCross = crossingLoc;
        }

        Add(ip);

      } // while i <= highI
      ///////////////////////////////////////////////////

      if (firstCross == Location.Inside)
      {
        // path never intersects
        if (startingLoc != Location.Inside)
        {
          // path is outside rect
          // but being outside, it still may not contain rect
          if (path_bounds_.Contains(rect_) &&
            Path1ContainsPath2(path, rect_as_path_))
          {
            // yep, the path does fully contain rect
            // so add rect to the solution
            bool isClockwisePath = StartLocsAreClockwise(start_locs_);
            for (int j = 0; j < 4; ++j)
            {
              int k = isClockwisePath ? j : 3 - j; // reverses result path
              Add(rect_as_path_[k]);
              // we may well need to do some splitting later, so
              AddToEdge(edges_[k * 2], results_[0]!);
            }
          }
        }
      }
      else if (loc != Location.Inside &&
        (loc != firstCross || start_locs_.Count > 2))
      {
        if (start_locs_.Count > 0)
        {
          prev = loc;
          foreach (Location loc2 in start_locs_)
          {
            if (prev == loc2) continue;
            AddCorner(ref prev, HeadingClockwise(prev, loc2));
            prev = loc2;
          }
          loc = prev;
        }
        if (loc != firstCross)
          AddCorner(ref loc, HeadingClockwise(loc, firstCross));
      }
    }

    private void CheckEdges()
    {
      for (int i = 0; i < results_.Count; ++i)
      {
        OutPt2? op = results_[i];
        if (op == null) continue;
        OutPt2? op2 = op;
        do
        {
          if (InternalClipper.IsCollinear(op2!.prev!.pt, op2.pt, op2.next!.pt))
          {
            if (ReferenceEquals(op2, op))
            {
              op2 = UnlinkOpBack(op2);
              if (op2 == null) break;
              op = op2.prev;
            }
            else
            {
              op2 = UnlinkOpBack(op2);
              if (op2 == null) break;
            }
          }
          else
            op2 = op2.next;
        } while (!ReferenceEquals(op2, op));

        if (op2 == null)
        {
          results_[i] = null;
          continue;
        }
        results_[i] = op; // safety first

        uint edgeSet1 = GetEdgesForPt(op!.prev!.pt, rect_);
        op2 = op;
        do
        {
          uint edgeSet2 = GetEdgesForPt(op2!.pt, rect_);
          if (edgeSet2 != 0 && op2.edge == null)
          {
            uint combinedSet = (edgeSet1 & edgeSet2);
            for (int j = 0; j < 4; ++j)
            {
              if ((combinedSet & (1u << j)) != 0)
              {
                if (IsHeadingClockwise(op2.prev!.pt, op2.pt, j))
                  AddToEdge(edges_[j * 2], op2);
                else
                  AddToEdge(edges_[j * 2 + 1], op2);
              }
            }
          }
          edgeSet1 = edgeSet2;
          op2 = op2.next;
        } while (!ReferenceEquals(op2, op));
      }
    }

    private void TidyEdges(int idx, List<OutPt2?> cw, List<OutPt2?> ccw)
    {
      if (ccw.Count == 0) return;
      bool isHorz = ((idx == 1) || (idx == 3));
      bool cwIsTowardLarger = ((idx == 1) || (idx == 2));
      int i = 0, j = 0;
      OutPt2 p1, p2, p1a, p2a, op, op2;

      while (i < cw.Count)
      {
        p1 = cw[i]!;
        if (p1 == null || ReferenceEquals(p1.next, p1.prev))
        {
          cw[i++] = null;
          j = 0;
          continue;
        }

        int jLim = ccw.Count;
        while (j < jLim && (ccw[j] == null || ReferenceEquals(ccw[j]!.next, ccw[j]!.prev))) ++j;

        if (j == jLim)
        {
          ++i;
          j = 0;
          continue;
        }

        if (cwIsTowardLarger)
        {
          // p1 >>>> p1a;
          // p2 <<<< p2a;
          p1 = cw[i]!.prev!;
          p1a = cw[i]!;
          p2 = ccw[j]!;
          p2a = ccw[j]!.prev!;
        }
        else
        {
          // p1 <<<< p1a;
          // p2 >>>> p2a;
          p1 = cw[i]!;
          p1a = cw[i]!.prev!;
          p2 = ccw[j]!.prev!;
          p2a = ccw[j]!;
        }

        if ((isHorz && !HasHorzOverlap(p1.pt, p1a.pt, p2.pt, p2a.pt)) ||
          (!isHorz && !HasVertOverlap(p1.pt, p1a.pt, p2.pt, p2a.pt)))
        {
          ++j;
          continue;
        }

        // to get here we're either splitting or rejoining
        bool isRejoining = cw[i]!.ownerIdx != ccw[j]!.ownerIdx;

        if (isRejoining)
        {
          results_[p2.ownerIdx] = null;
          SetNewOwner(p2, p1.ownerIdx);
        }

        // do the split or re-join
        if (cwIsTowardLarger)
        {
          // p1 >> | >> p1a;
          // p2 << | << p2a;
          p1.next = p2;
          p2.prev = p1;
          p1a.prev = p2a;
          p2a.next = p1a;
        }
        else
        {
          // p1 << | << p1a;
          // p2 >> | >> p2a;
          p1.prev = p2;
          p2.next = p1;
          p1a.next = p2a;
          p2a.prev = p1a;
        }

        if (!isRejoining)
        {
          int newIdx = results_.Count;
          results_.Add(p1a);
          SetNewOwner(p1a, newIdx);
        }

        if (cwIsTowardLarger)
        {
          op = p2;
          op2 = p1a;
        }
        else
        {
          op = p1;
          op2 = p2a;
        }
        results_[op.ownerIdx] = op;
        results_[op2.ownerIdx] = op2;

        // and now lots of work to get ready for the next loop

        bool opIsLarger, op2IsLarger;
        if (isHorz) // X
        {
          opIsLarger = op.pt.X > op.prev!.pt.X;
          op2IsLarger = op2.pt.X > op2.prev!.pt.X;
        }
        else       // Y
        {
          opIsLarger = op.pt.Y > op.prev!.pt.Y;
          op2IsLarger = op2.pt.Y > op2.prev!.pt.Y;
        }

        if (ReferenceEquals(op.next, op.prev) || (op.pt == op.prev.pt))
        {
          if (op2IsLarger == cwIsTowardLarger)
          {
            cw[i] = op2;
            ccw[j++] = null;
          }
          else
          {
            ccw[j] = op2;
            cw[i++] = null;
          }
        }
        else if (ReferenceEquals(op2.next, op2.prev) || (op2.pt == op2.prev.pt))
        {
          if (opIsLarger == cwIsTowardLarger)
          {
            cw[i] = op;
            ccw[j++] = null;
          }
          else
          {
            ccw[j] = op;
            cw[i++] = null;
          }
        }
        else if (opIsLarger == op2IsLarger)
        {
          if (opIsLarger == cwIsTowardLarger)
          {
            cw[i] = op;
            UncoupleEdge(op2);
            AddToEdge(cw, op2);
            ccw[j++] = null;
          }
          else
          {
            cw[i++] = null;
            ccw[j] = op2;
            UncoupleEdge(op);
            AddToEdge(ccw, op);
            j = 0;
          }
        }
        else
        {
          if (opIsLarger == cwIsTowardLarger)
            cw[i] = op;
          else
            ccw[j] = op;
          if (op2IsLarger == cwIsTowardLarger)
            cw[i] = op2;
          else
            ccw[j] = op2;
        }
      }
    }

    protected virtual Path64 GetPath(ref OutPt2? op)
    {
      if (op == null || ReferenceEquals(op.next, op.prev)) return new Path64();

      OutPt2? op2 = op.next;
      while (op2 != null && !ReferenceEquals(op2, op))
      {
        if (InternalClipper.IsCollinear(op2.prev!.pt, op2.pt, op2.next!.pt))
        {
          op = op2.prev;
          op2 = UnlinkOp(op2);
        }
        else
          op2 = op2.next;
      }
      op = op2; // needed for op cleanup
      if (op2 == null) return new Path64();

      Path64 result = PathScratch;
      result.Add(op.pt);
      op2 = op.next;
      while (!ReferenceEquals(op2, op))
      {
        result.Add(op2!.pt);
        op2 = op2.next;
      }
      return TakeScratchPath(result);
    }

    public Paths64 Execute(Paths64 paths)
    {
      if (rect_.IsEmpty()) return new Paths64();

      // nb: each path is clipped independently, so a batch of paths can be
      // split over several workers (each worker owns a private RectClip64)
      long total = 0;
      foreach (Path64 p in paths) total += p.Count;
      if (!BulkOps.ShouldParallelize(total) || paths.Count < 4)
        return ExecuteSerial(paths);

      int threads = Math.Min(BulkOps.MaxThreads, paths.Count);
      int chunk = (paths.Count + threads - 1) / threads;
      Paths64[] parts = new Paths64[threads];
      System.Threading.Tasks.Parallel.For(0, threads, t =>
      {
        int start = t * chunk;
        int end = Math.Min(start + chunk, paths.Count);
        if (start >= end) { parts[t] = new Paths64(); return; }
        Paths64 slice = new Paths64(end - start);
        for (int i = start; i < end; i++) slice.Add(paths[i]);
        parts[t] = new RectClip64(rect_).ExecuteSerial(slice);
      });

      Paths64 result = new Paths64();
      foreach (Paths64 part in parts) result.AddRange(part);
      return result;
    }

    internal Paths64 ExecuteSerial(Paths64 paths)
    {
      Paths64 result = new Paths64();
      if (rect_.IsEmpty()) return result;
      RentWork();
      try
      {
        ExecuteSerialCore(paths, result);
      }
      finally
      {
        ReturnWork();
      }
      return result;
    }

    private void ExecuteSerialCore(Paths64 paths, Paths64 result)
    {
      foreach (Path64 path in paths)
      {
        if (path.Count < 3) continue;
        path_bounds_ = InternalClipper.GetBounds(path);
        if (!rect_.Intersects(path_bounds_))
          continue; // the path must be completely outside rect_
        else if (rect_.Contains(path_bounds_))
        {
          // the path must be completely inside rect_
          result.Add(path);
          continue;
        }

        ExecuteInternal(path);
        CheckEdges();
        for (int i = 0; i < 4; ++i)
          TidyEdges(i, edges_[i * 2], edges_[i * 2 + 1]);

        for (int i = 0; i < results_.Count; i++)
        {
          OutPt2? op = results_[i];
          Path64 tmp = GetPath(ref op);
          if (tmp.Count > 0)
            result.Add(tmp);
        }

        // clean up after every loop
        results_.Clear();
        foreach (List<OutPt2?> edge in edges_) edge.Clear();
        start_locs_.Clear();
        RecycleOutPts();
      }
    }
  }

  //------------------------------------------------------------------------------
  // RectClipLines64
  //------------------------------------------------------------------------------

  public class RectClipLines64 : RectClip64
  {
    public RectClipLines64(Rect64 rect) : base(rect) { }

    private void ExecuteInternalLines(Path64 path)
    {
      if (rect_.IsEmpty() || path.Count < 2) return;

      results_.Clear();
      start_locs_.Clear();

      int i = 1, highI = path.Count - 1;

      Location prev = Location.Inside, loc;
      Location crossingLoc;
      if (!GetLocation(rect_, path[0], out loc))
      {
        while (i <= highI && !GetLocation(rect_, path[i], out prev)) ++i;
        if (i > highI)
        {
          // all of path must be inside fRect
          foreach (Point64 pt in path) Add(pt);
          return;
        }
        if (prev == Location.Inside) loc = Location.Inside;
        i = 1;
      }
      if (loc == Location.Inside) Add(path[0]);

      ///////////////////////////////////////////////////
      while (i <= highI)
      {
        prev = loc;
        GetNextLocation(path, ref loc, ref i, highI);
        if (i > highI) break;
        Point64 ip, ip2;
        Point64 prevPt = path[i - 1];

        crossingLoc = loc;
        if (!GetIntersection(rect_as_path_,
          path[i], prevPt, ref crossingLoc, out ip))
        {
          // ie remaining outside
          ++i;
          continue;
        }

        ////////////////////////////////////////////////////
        // we must be crossing the rect boundary to get here
        ////////////////////////////////////////////////////

        if (loc == Location.Inside) // path must be entering rect
        {
          Add(ip, true);
        }
        else if (prev != Location.Inside)
        {
          // passing right through rect. 'ip' here will be the second
          // intersect pt but we'll also need the first intersect pt (ip2)
          crossingLoc = prev;
          GetIntersection(rect_as_path_, prevPt, path[i], ref crossingLoc, out ip2);
          Add(ip2, true);
          Add(ip);
        }
        else // path must be exiting rect
        {
          Add(ip);
        }
      } // while i <= highI
      ///////////////////////////////////////////////////
    }

    private Path64 GetPathLines(ref OutPt2? op)
    {
      if (op == null || ReferenceEquals(op, op.next)) return new Path64();
      Path64 result = PathScratch;
      op = op.next; // starting at path beginning
      result.Add(op!.pt);
      OutPt2? op2 = op.next;
      while (!ReferenceEquals(op2, op))
      {
        result.Add(op2!.pt);
        op2 = op2.next;
      }
      return TakeScratchPath(result);
    }

    public new Paths64 Execute(Paths64 paths)
    {
      if (rect_.IsEmpty()) return new Paths64();

      long total = 0;
      foreach (Path64 p in paths) total += p.Count;
      if (!BulkOps.ShouldParallelize(total) || paths.Count < 4)
        return ExecuteSerialLines(paths);

      int threads = Math.Min(BulkOps.MaxThreads, paths.Count);
      int chunk = (paths.Count + threads - 1) / threads;
      Paths64[] parts = new Paths64[threads];
      System.Threading.Tasks.Parallel.For(0, threads, t =>
      {
        int start = t * chunk;
        int end = Math.Min(start + chunk, paths.Count);
        if (start >= end) { parts[t] = new Paths64(); return; }
        Paths64 slice = new Paths64(end - start);
        for (int i = start; i < end; i++) slice.Add(paths[i]);
        parts[t] = new RectClipLines64(rect_).ExecuteSerialLines(slice);
      });

      Paths64 result = new Paths64();
      foreach (Paths64 part in parts) result.AddRange(part);
      return result;
    }

    internal Paths64 ExecuteSerialLines(Paths64 paths)
    {
      Paths64 result = new Paths64();
      if (rect_.IsEmpty()) return result;
      RentWork();
      try
      {
        ExecuteSerialLinesCore(paths, result);
      }
      finally
      {
        ReturnWork();
      }
      return result;
    }

    private void ExecuteSerialLinesCore(Paths64 paths, Paths64 result)
    {
      foreach (Path64 path in paths)
      {
        Rect64 pathrec = InternalClipper.GetBounds(path);
        if (!rect_.Intersects(pathrec)) continue;

        ExecuteInternalLines(path);

        for (int i = 0; i < results_.Count; i++)
        {
          OutPt2? op = results_[i];
          Path64 tmp = GetPathLines(ref op);
          if (tmp.Count > 0)
            result.Add(tmp);
        }
        results_.Clear();
        start_locs_.Clear();
        RecycleOutPts();
      }
    }
  }
}
