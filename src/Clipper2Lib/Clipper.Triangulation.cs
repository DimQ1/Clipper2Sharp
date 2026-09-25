/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  21 February 2026                                                *
* Release   :  BETA RELEASE                                                    *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2026                                         *
* Purpose   :  Constrained Delaunay Triangulation                              *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.triangulation.h + .cpp (Clipper2 ver. 2.0.1)     *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  public enum TriangulateResult { success, fail, noPolygons, pathsIntersect }

  internal enum EdgeKind { loose, ascend, descend } // ascend & descend are 'fixed' edges

  internal enum IntersectKind { none, collinear, intersect }

  internal enum EdgeContainsResult { neither, left, right }

  internal class Vertex2
  {
    public Point64 pt;
    public List<Edge> edges = new List<Edge>(2);
    public bool innerLM = false;

    public Vertex2(Point64 p64) { pt = p64; }
  }

  internal class Edge
  {
    public Vertex2? vL = null;
    public Vertex2? vR = null;
    public Vertex2? vB = null;
    public Vertex2? vT = null;
    public EdgeKind kind = EdgeKind.loose;
    public Triangle? triA = null;
    public Triangle? triB = null;
    public bool isActive = false;
    public Edge? nextE = null;
    public Edge? prevE = null;
  }

  internal class Triangle
  {
    public Edge[] edges = new Edge[3];

    public Triangle(Edge e1, Edge e2, Edge e3)
    {
      edges[0] = e1;
      edges[1] = e2;
      edges[2] = e3;
    }
  }

  /////////////////////////////////////////////////////////////////////////////
  // Delaunay class
  /////////////////////////////////////////////////////////////////////////////

  internal class Delaunay
  {
    private readonly List<Vertex2> allVertices = new List<Vertex2>();
    private readonly List<Edge> allEdges = new List<Edge>();
    private readonly List<Triangle> allTriangles = new List<Triangle>();
    private readonly Stack<Edge> pendingDelaunayStack = new Stack<Edge>();
    private readonly Stack<Edge> horzEdgeStack = new Stack<Edge>();
    private readonly Stack<Vertex2> locMinStack = new Stack<Vertex2>();
    private readonly bool useDelaunay;
    private Vertex2? lowermostVertex = null;
    private Edge? firstActive = null;

    public Delaunay(bool delaunay = true)
    {
      useDelaunay = delaunay;
    }

    // Miscellaneous functions --------------------------------------------------

    private static bool IsLooseEdge(Edge e) => e.kind == EdgeKind.loose;

    private static bool IsLeftEdge(Edge e)
    {
      // left edge (bound) of a fill region
      // ie fills on the **right** side of the edge
      // precondition - e is never a 'loose' edge
      return e.kind == EdgeKind.ascend;
    }

    private static bool IsRightEdge(Edge e)
    {
      // right edge (bound) of a fill region
      // but still fills on the **right** side of the edge
      // precondition - e is never a 'loose' edge
      return e.kind == EdgeKind.descend;
    }

    private static bool IsHorizontal(Edge e) => e.vB!.pt.Y == e.vT!.pt.Y;

    private static bool LeftTurning(Point64 p1, Point64 p2, Point64 p3)
    {
      return InternalClipper.CrossProductSign(p1, p2, p3) < 0;
    }

    private static bool RightTurning(Point64 p1, Point64 p2, Point64 p3)
    {
      return InternalClipper.CrossProductSign(p1, p2, p3) > 0;
    }

    private static bool EdgeCompleted(Edge edge)
    {
      if (edge.triA == null) return false;
      if (edge.triB != null) return true;
      return edge.kind != EdgeKind.loose;
    }

    private static EdgeContainsResult EdgeContains(Edge edge, Vertex2 v)
    {
      if (ReferenceEquals(edge.vL, v)) return EdgeContainsResult.left;
      else if (ReferenceEquals(edge.vR, v)) return EdgeContainsResult.right;
      else return EdgeContainsResult.neither;
    }

    private static double GetAngle(Point64 a, Point64 b, Point64 c)
    {
      // https://stackoverflow.com/a/3487062/359538
      double abx = (double) (b.X - a.X);
      double aby = (double) (b.Y - a.Y);
      double bcx = (double) (b.X - c.X);
      double bcy = (double) (b.Y - c.Y);
      double dp = (abx * bcx + aby * bcy);
      double cp = (abx * bcy - aby * bcx);
      return Math.Atan2(cp, dp); // range between -Pi and Pi
    }

    private static double GetLocMinAngle(Vertex2 v)
    {
      // precondition - this function is called before processing locMin.
      int asc, des;
      if (v.edges[0].kind == EdgeKind.ascend)
      {
        asc = 0;
        des = 1;
      }
      else
      {
        des = 0;
        asc = 1;
      }
      // winding direction - descending to ascending
      return GetAngle(v.edges[des].vT!.pt, v.pt, v.edges[asc].vT!.pt);
    }

    private static void RemoveEdgeFromVertex(Vertex2 vert, Edge edge)
    {
      int idx = vert.edges.IndexOf(edge);
      if (idx < 0) InternalClipper.DoError(Clipper2Error.UndefinedError);
      vert.edges.RemoveAt(idx);
    }

    private static bool FindLocMinIdx(Path64 path, int len, ref int idx)
    {
      if (len < 3) return false;
      int i0 = idx, n = (idx + 1) % len;
      while (path[n].Y <= path[idx].Y)
      {
        idx = n; n = (n + 1) % len;
        if (idx == i0) return false; // fails if the path is completely horizontal
      }
      while (path[n].Y >= path[idx].Y)
      {
        idx = n; n = (n + 1) % len;
      }
      return true;
    }

    private static int Prev(int idx, int len) => (idx == 0) ? len - 1 : idx - 1;

    private static int Next(int idx, int len) => (idx + 1) % len;

    private static Edge? FindLinkingEdge(Vertex2 vert1, Vertex2 vert2, bool preferAscending)
    {
      Edge? res = null;
      foreach (Edge e in vert1.edges)
      {
        if (ReferenceEquals(e.vL, vert2) || ReferenceEquals(e.vR, vert2))
        {
          if (e.kind == EdgeKind.loose ||
            ((e.kind == EdgeKind.ascend) == preferAscending)) return e;
          res = e;
        }
      }
      return res;
    }

    private static Path64 PathFromTriangle(Triangle tri)
    {
      Path64 res = new Path64(3);
      res.Add(tri.edges[0].vL!.pt);
      res.Add(tri.edges[0].vR!.pt);
      Edge e = tri.edges[1];
      if (e.vL!.pt == res[0] || e.vL.pt == res[1])
        res.Add(e.vR!.pt);
      else
        res.Add(e.vL.pt);
      return res;
    }

    private static double SqrD(double val) => val * val;

    private static double InCircleTest(Point64 ptA, Point64 ptB,
      Point64 ptC, Point64 ptD)
    {
      // Return the determinant value of 3 x 3 matrix ...
      // | ax-dx    ay-dy    Sqr(ax-dx)+Sqr(ay-dy) |
      // | bx-dx    by-dy    Sqr(bx-dx)+Sqr(by-dy) |
      // | cx-dx    cy-dy    Sqr(cx-dx)+Sqr(cy-dy) |

      // The *sign* of the return value is determined by
      // the orientation (CW vs CCW) of ptA, ptB & ptC.

      // precondition - ptA, ptB & ptC make a *non-empty* triangle
      double m00 = (double) (ptA.X - ptD.X);
      double m01 = (double) (ptA.Y - ptD.Y);
      double m02 = (SqrD(m00) + SqrD(m01));
      double m10 = (double) (ptB.X - ptD.X);
      double m11 = (double) (ptB.Y - ptD.Y);
      double m12 = (SqrD(m10) + SqrD(m11));
      double m20 = (double) (ptC.X - ptD.X);
      double m21 = (double) (ptC.Y - ptD.Y);
      double m22 = (SqrD(m20) + SqrD(m21));
      return m00 * (m11 * m22 - m21 * m12) -
        m10 * (m01 * m22 - m21 * m02) +
        m20 * (m01 * m12 - m11 * m02);
    }

    private static double ShortestDistFromSegment(Point64 pt, Point64 segPt1, Point64 segPt2)
    {
      // precondition: segPt1 <> segPt2
      double dx = (double) (segPt2.X - segPt1.X);
      double dy = (double) (segPt2.Y - segPt1.Y);

      double ax = (double) (pt.X - segPt1.X);
      double ay = (double) (pt.Y - segPt1.Y);
      double qNum = ax * dx + ay * dy;
      if (qNum < 0) // pt is closest to seg1
        return InternalClipper.DistanceSqr(pt, segPt1);
      else if (qNum > (SqrD(dx) + SqrD(dy))) // pt is closest to seg2
        return InternalClipper.DistanceSqr(pt, segPt2);
      else
        return SqrD(ax * dy - dx * ay) / (dx * dx + dy * dy);
    }

    private static IntersectKind SegsIntersect(Point64 s1a, Point64 s1b,
      Point64 s2a, Point64 s2b)
    {
      // ignore segments sharing an end-point
      if (s1a == s2a || s1b == s2a || s1b == s2b) return IntersectKind.none;

      double dy1 = (double) (s1b.Y - s1a.Y);
      double dx1 = (double) (s1b.X - s1a.X);
      double dy2 = (double) (s2b.Y - s2a.Y);
      double dx2 = (double) (s2b.X - s2a.X);
      double cp = dy1 * dx2 - dy2 * dx1;
      if (cp == 0) return IntersectKind.collinear;

      double t = ((double) (s1a.X - s2a.X) * dy2 - (double) (s1a.Y - s2a.Y) * dx2);

      // nb: testing for t == 0 is unreliable due to float imprecision
      if (t >= 0)
      {
        if (cp < 0 || t >= cp) return IntersectKind.none;
      }
      else
      {
        if (cp > 0 || t <= cp) return IntersectKind.none;
      }

      // so far, the *segment* 's1' intersects the *line* through 's2',
      // but now make sure it also intersects the *segment* 's2'
      t = ((s1a.X - s2a.X) * dy1 - (s1a.Y - s2a.Y) * dx1);
      if (t >= 0)
      {
        if (cp > 0 && t < cp) return IntersectKind.intersect;
      }
      else
      {
        if (cp < 0 && t > cp) return IntersectKind.intersect;
      }
      return IntersectKind.none;
    }

    // Delaunay class methods ---------------------------------------------------

    private void CleanUp()
    {
      allVertices.Clear();
      allEdges.Clear();
      allTriangles.Clear();

      firstActive = null;
      lowermostVertex = null;
    }

    private void ForceLegal(Edge edge)
    {
      // don't try to make empty triangles legal
      if (edge.triA == null || edge.triB == null) return;

      // vertA will be assigned the vertex in edge's triangleA
      // that is NOT an end vertex of edge
      // Likewise, vertB will be assigned the vertex in edge's
      // triangleB that is NOT an end vertex of edge
      // If edge is rotated, vertA & vertB will become its end vertices.
      Vertex2? vertA = null;
      Vertex2? vertB = null;

      // Excluding 'edge', edgesA will contain two edges (one from
      // triangleA and one from triangleB) that touch edge.vL.
      // And edgesB will contain the two edges that touch edge.vR.

      Edge?[] edgesA = new Edge?[3];
      Edge?[] edgesB = new Edge?[3];
      for (int i = 0; i < 3; ++i)
      {
        if (ReferenceEquals(edge.triA.edges[i], edge)) continue;
        switch (EdgeContains(edge.triA.edges[i], edge.vL!))
        {
          case EdgeContainsResult.left:
            edgesA[1] = edge.triA.edges[i];
            vertA = edge.triA.edges[i].vR!;
            break;
          case EdgeContainsResult.right:
            edgesA[1] = edge.triA.edges[i];
            vertA = edge.triA.edges[i].vL!;
            break;
          default:
            edgesB[1] = edge.triA.edges[i];
            break;
        }
      }

      for (int i = 0; i < 3; ++i)
      {
        if (ReferenceEquals(edge.triB.edges[i], edge)) continue;
        switch (EdgeContains(edge.triB.edges[i], edge.vL!))
        {
          case EdgeContainsResult.left:
            edgesA[2] = edge.triB.edges[i];
            vertB = edge.triB.edges[i].vR!;
            break;
          case EdgeContainsResult.right:
            edgesA[2] = edge.triB.edges[i];
            vertB = edge.triB.edges[i].vL!;
            break;
          default:
            edgesB[2] = edge.triB.edges[i];
            break;
        }
      }

      // InCircleTest requires edge.triangleA to be a valid triangle
      if (InternalClipper.CrossProductSign(vertA!.pt, edge.vL!.pt, edge.vR!.pt) == 0) return;

      // ictResult - result sign is dependant on triangleA's orientation
      double ictResult = InCircleTest(vertA.pt, edge.vL.pt, edge.vR.pt, vertB!.pt);
      if (ictResult == 0 || // if on or out of circle then exit
        (RightTurning(vertA.pt, edge.vL.pt, edge.vR.pt) == (ictResult < 0))) return;

      // TRIANGLES HERE ARE **NOT** DELAUNAY COMPLIANT, SO MAKE THEM SO.

      // NOTE: ONCE WE BEGIN DELAUNAY COMPLIANCE, vL & vR WILL
      // NO LONGER REPRESENT LEFT AND RIGHT VERTEX ORIENTATION.
      // THIS IS MINOR PERFORMANCE EFFICIENCY IS SAFE AS LONG AS
      // THE TRIANGULATE() METHOD IS CALLED ONCE ONLY ON A GIVEN
      // SET OF PATHS

      edge.vL = vertA;
      edge.vR = vertB;

      edge.triA.edges[0] = edge;
      for (int i = 1; i < 3; ++i)
      {
        edge.triA.edges[i] = edgesA[i]!;
        if (edgesA[i] == null) InternalClipper.DoError(Clipper2Error.UndefinedError);
        if (IsLooseEdge(edgesA[i]!))
          pendingDelaunayStack.Push(edgesA[i]!);
        // since each edge has its own triangleA and triangleB, we have to be careful
        // to update the correct one ...
        if (ReferenceEquals(edgesA[i]!.triA, edge.triA) ||
          ReferenceEquals(edgesA[i]!.triB, edge.triA)) continue;

        if (ReferenceEquals(edgesA[i]!.triA, edge.triB))
          edgesA[i]!.triA = edge.triA;
        else if (ReferenceEquals(edgesA[i]!.triB, edge.triB))
          edgesA[i]!.triB = edge.triA;
        else InternalClipper.DoError(Clipper2Error.UndefinedError);
      }

      edge.triB.edges[0] = edge;
      for (int i = 1; i < 3; ++i)
      {
        edge.triB.edges[i] = edgesB[i]!;
        if (edgesB[i] == null) InternalClipper.DoError(Clipper2Error.UndefinedError);
        if (IsLooseEdge(edgesB[i]!))
          pendingDelaunayStack.Push(edgesB[i]!);
        // since each edge has its own triangleA and triangleB, we have to be careful
        // to update the correct one ...
        if (ReferenceEquals(edgesB[i]!.triA, edge.triB) ||
          ReferenceEquals(edgesB[i]!.triB, edge.triB)) continue;

        if (ReferenceEquals(edgesB[i]!.triA, edge.triA))
          edgesB[i]!.triA = edge.triB;
        else if (ReferenceEquals(edgesB[i]!.triB, edge.triA))
          edgesB[i]!.triB = edge.triB;
        else InternalClipper.DoError(Clipper2Error.UndefinedError);
      }
    }

    private Edge CreateEdge(Vertex2 v1, Vertex2 v2, EdgeKind k)
    {
      Edge res = new Edge();
      allEdges.Add(res);
      if (v1.pt.Y == v2.pt.Y)
      {
        res.vB = v1; res.vT = v2;
      }
      else if (v1.pt.Y < v2.pt.Y)
      {
        res.vB = v2; res.vT = v1;
      }
      else
      {
        res.vB = v1; res.vT = v2;
      }

      if (v1.pt.X <= v2.pt.X)
      {
        res.vL = v1; res.vR = v2;
      }
      else
      {
        res.vL = v2; res.vR = v1;
      }
      res.kind = k;
      v1.edges.Add(res);
      v2.edges.Add(res);

      if (k == EdgeKind.loose)
      {
        pendingDelaunayStack.Push(res);
        AddEdgeToActives(res);
      }
      return res;
    }

    private Triangle CreateTriangle(Edge e1, Edge e2, Edge e3)
    {
      Triangle res = new Triangle(e1, e2, e3);
      allTriangles.Add(res);
      // nb: only expire loose edges when both sides of these edges have triangles.
      for (int i = 0; i < 3; ++i)
      {
        if (res.edges[i].triA != null)
        {
          res.edges[i].triB = res;
          // this is the edge's second triangle hence no longer active
          RemoveEdgeFromActives(res.edges[i]);
        }
        else
        {
          res.edges[i].triA = res;
          // this is the edge's first triangle, so only remove
          // this edge from actives if it's a fixed edge.
          if (!IsLooseEdge(res.edges[i]))
            RemoveEdgeFromActives(res.edges[i]);
        }
      }
      return res;
    }

    private bool RemoveIntersection(Edge e1, Edge e2)
    {
      // find which vertex is closest to the other segment
      // (ie not the vertex closest to the intersection point) ...

      Vertex2 v = e1.vL!;
      Edge tmpE = e2;
      double d = ShortestDistFromSegment(e1.vL!.pt, e2.vL!.pt, e2.vR!.pt);
      double d2 = ShortestDistFromSegment(e1.vR!.pt, e2.vL.pt, e2.vR.pt);
      if (d2 < d) { d = d2; v = e1.vR; }
      d2 = ShortestDistFromSegment(e2.vL.pt, e1.vL.pt, e1.vR.pt);
      if (d2 < d) { d = d2; tmpE = e1; v = e2.vL; }
      d2 = ShortestDistFromSegment(e2.vR!.pt, e1.vL.pt, e1.vR.pt);
      if (d2 < d) { d = d2; tmpE = e1; v = e2.vR; }
      if (d > 1.000)
        return false; // Oops - this is not just a simple 'rounding' intersection

      // split 'tmpE' into 2 edges at 'v'
      Vertex2 v2 = tmpE.vT!;
      RemoveEdgeFromVertex(v2, tmpE);
      // replace v2 in tmpE with v
      if (ReferenceEquals(tmpE.vL, v2)) tmpE.vL = v;
      else tmpE.vR = v;
      tmpE.vT = v;
      v.edges.Add(tmpE);
      v.innerLM = false; // #47
      // left turning is angle positive
      if (tmpE.vB!.innerLM && GetLocMinAngle(tmpE.vB) <= 0)
        tmpE.vB.innerLM = false; // #44, 52
      // finally create a new edge between v and v2 ...
      CreateEdge(v, v2, tmpE.kind);
      return true;
    }

    private bool FixupEdgeIntersects()
    {
      // precondition - edgeList must be sorted - ascending on edge.vL.pt.x

      for (int i1 = 0; i1 < allEdges.Count; ++i1)
      {
        Edge e1 = allEdges[i1];
        // nb: we can safely ignore edges newly created inside this for loop
        for (int i2 = i1 + 1; i2 < allEdges.Count; ++i2)
        {
          Edge e2 = allEdges[i2];
          if (e2.vL!.pt.X >= e1.vR!.pt.X)
            break; // all 'e' from now on are too far right

          // 'e2' is inside e1's horizontal region. If 'e2' is also inside
          // e1's vertical region, only then check for an intersection ...
          if (e2.vT!.pt.Y < e1.vB!.pt.Y && e2.vB!.pt.Y > e1.vT!.pt.Y &&
            (SegsIntersect(e2.vL.pt, e2.vR.pt,
              e1.vL.pt, e1.vR.pt) == IntersectKind.intersect))
          {
            if (!RemoveIntersection(e2, e1))
              return false; // oops!!
          }
          // nb: collinear edges are managed in MergeDupOrCollinearVertices below
        }
      }
      return true;
    }

    private void SplitEdge(Edge longE, Edge shortE)
    {
      Vertex2 oldT = longE.vT!, newT = shortE.vT!;
      // remove longEdge from longEdge.vT.edges
      RemoveEdgeFromVertex(oldT, longE);
      // shorten longEdge
      longE.vT = newT;
      if (ReferenceEquals(longE.vL, oldT)) longE.vL = newT;
      else longE.vR = newT;
      // add shortened longEdge to newT.edges
      newT.edges.Add(longE);
      // and create a new edge between newV, oldT
      CreateEdge(newT, oldT, longE.kind);
    }

    private void MergeDupOrCollinearVertices()
    {
      // note: this procedure may add new edges and change the
      // number of edges connected with a given vertex, but it
      // won't add or delete vertices (so it's safe to use iterators)
      int vIter1 = 0;
      for (int vIter2 = 1; vIter2 < allVertices.Count; ++vIter2)
      {
        if (allVertices[vIter1].pt != allVertices[vIter2].pt)
        {
          vIter1 = vIter2;
          continue;
        }

        // merge v1 & v2
        Vertex2 v1 = allVertices[vIter1], v2 = allVertices[vIter2];
        if (!v1.innerLM || !v2.innerLM)
          v1.innerLM = false;

        // in all of v2's edges, replace links to v2 with links to v1
        foreach (Edge e in v2.edges)
        {
          if (ReferenceEquals(e.vB, v2)) e.vB = v1;
          else e.vT = v1;
          if (ReferenceEquals(e.vL, v2)) e.vL = v1;
          else e.vR = v1;
        }
        // move all of v2's edges to v1
        v1.edges.AddRange(v2.edges);
        v2.edges.Clear();

        // excluding horizontals, if pv.edges contains two edges
        // that are *collinear* and share the same bottom coords
        // but have different lengths, split the longer edge at
        // the top of the shorter edge ...
        int eCount = v1.edges.Count;
        for (int ie = 0; ie < eCount; ++ie)
        {
          if (IsHorizontal(v1.edges[ie]) || !ReferenceEquals(v1.edges[ie].vB, v1)) continue;
          for (int ie2 = ie + 1; ie2 < eCount; ++ie2)
          {
            Edge e1 = v1.edges[ie], e2 = v1.edges[ie2];
            if (!ReferenceEquals(e2.vB, v1) || e1.vT!.pt.Y == e2.vT!.pt.Y ||
              (InternalClipper.CrossProductSign(e1.vT.pt, v1.pt, e2.vT.pt) != 0)) continue;
            // we have parallel edges, both heading up from v1.pt.
            // split the longer edge at the top of the shorter edge.
            if (e1.vT.pt.Y < e2.vT.pt.Y) SplitEdge(e1, e2);
            else SplitEdge(e2, e1);
            break; // because only two edges can be collinear
          }
        }
      }
    }

    private Edge? CreateInnerLocMinLooseEdge(Vertex2 vAbove)
    {
      if (firstActive == null) return null; // oops!!

      long xAbove = vAbove.pt.X;
      long yAbove = vAbove.pt.Y;

      // find the closest 'active' edge with a vertex that's not above vAbove
      Edge? e = firstActive;
      Edge? eBelow = null;
      double bestD = -1.0;
      while (e != null)
      {
        if (e.vL!.pt.X <= xAbove && e.vR!.pt.X >= xAbove &&
          e.vB!.pt.Y >= yAbove && !ReferenceEquals(e.vB, vAbove) && !ReferenceEquals(e.vT, vAbove) &&
          !LeftTurning(e.vL.pt, vAbove.pt, e.vR.pt))
        {
          double d = ShortestDistFromSegment(vAbove.pt, e.vL.pt, e.vR.pt);
          if (eBelow == null || d < bestD) // compare e with eBelow
          {
            eBelow = e;
            bestD = d;
          }
        }
        e = e.nextE;
      }
      if (eBelow == null) return null; // oops!!

      // get the best vertex from 'eBelow'
      Vertex2 vBest = (eBelow.vT!.pt.Y <= yAbove) ? eBelow.vB! : eBelow.vT!;
      long xBest = vBest.pt.X;
      long yBest = vBest.pt.Y;

      // make sure no edges intersect 'vAbove' and 'vBest' ...
      e = firstActive;
      if (xBest < xAbove)
      {
        while (e != null)
        {
          if (e.vR!.pt.X > xBest && e.vL!.pt.X < xAbove &&
            e.vB!.pt.Y > yAbove && e.vT!.pt.Y < yBest &&
            (SegsIntersect(e.vB.pt, e.vT.pt,
              vBest.pt, vAbove.pt) == IntersectKind.intersect))
          {
            vBest = (e.vT.pt.Y > yAbove) ? e.vT : e.vB;
            xBest = vBest.pt.X;
            yBest = vBest.pt.Y;
          }
          e = e.nextE;
        }
      }
      else
      {
        while (e != null)
        {
          if (e.vR!.pt.X < xBest && e.vL!.pt.X > xAbove &&
            e.vB!.pt.Y > yAbove && e.vT!.pt.Y < yBest &&
            (SegsIntersect(e.vB.pt, e.vT.pt,
              vBest.pt, vAbove.pt) == IntersectKind.intersect))
          {
            vBest = e.vT.pt.Y > yAbove ? e.vT : e.vB;
            xBest = vBest.pt.X;
            yBest = vBest.pt.Y;
          }
          e = e.nextE;
        }
      }
      return CreateEdge(vBest, vAbove, EdgeKind.loose);
    }

    private Edge? HorizontalBetween(Vertex2 v1, Vertex2 v2)
    {
      long y = v1.pt.Y, l, r;
      if (v1.pt.X > v2.pt.X)
      {
        l = v2.pt.X;
        r = v1.pt.X;
      }
      else
      {
        l = v1.pt.X;
        r = v2.pt.X;
      }

      Edge? res = firstActive;
      while (res != null)
      {
        if (res.vL!.pt.Y == y && res.vR!.pt.Y == y &&
          res.vL.pt.X >= l && res.vR.pt.X <= r &&
          (res.vL.pt.X != l || res.vL.pt.X != r)) break;
        res = res.nextE;
      }
      return res;
    }

    private void DoTriangulateLeft(Edge edge, Vertex2 pivot, long minY)
    {
      // precondition - pivot must be one end of edge (Usually .vB)
      Vertex2? vAlt = null;
      Edge? eAlt = null;
      Vertex2 v = ReferenceEquals(edge.vB, pivot) ? edge.vT! : edge.vB!;

      foreach (Edge e in pivot.edges)
      {
        if (ReferenceEquals(e, edge) || !e.isActive) continue;
        Vertex2 vX = ReferenceEquals(e.vT, pivot) ? e.vB! : e.vT!;
        if (ReferenceEquals(vX, v)) continue;

        int cps = InternalClipper.CrossProductSign(v.pt, pivot.pt, vX.pt);
        if (cps == 0) // collinear paths
        {
          // if pivot is between v and vX then continue;
          // nb: this is important for both horiz and non-horiz collinear
          if ((v.pt.X > pivot.pt.X) == (pivot.pt.X > vX.pt.X)) continue;
        }
        // else if right-turning or not the best edge, then continue
        else if (cps > 0 || (vAlt != null && !LeftTurning(vX.pt, pivot.pt, vAlt.pt)))
          continue;
        vAlt = vX;
        eAlt = e;
      }

      if (vAlt == null || vAlt.pt.Y < minY) return;

      // Don't triangulate **across** fixed edges
      if (vAlt.pt.Y < pivot.pt.Y)
      {
        if (IsLeftEdge(eAlt!)) return;
      }
      else if (vAlt.pt.Y > pivot.pt.Y)
      {
        if (IsRightEdge(eAlt!)) return;
      }

      Edge? eX = FindLinkingEdge(vAlt, v, (vAlt.pt.Y < v.pt.Y));
      if (eX == null)
      {
        // be very careful creating loose horizontals at minY
        if (vAlt.pt.Y == v.pt.Y && v.pt.Y == minY &&
          HorizontalBetween(vAlt, v) != null) return;
        eX = CreateEdge(vAlt, v, EdgeKind.loose);
      }

      CreateTriangle(edge, eAlt!, eX);
      if (!EdgeCompleted(eX))
        DoTriangulateLeft(eX, vAlt, minY);
    }

    private void DoTriangulateRight(Edge edge, Vertex2 pivot, long minY)
    {
      // precondition - pivot must be one end of edge (Usually .vB)
      Vertex2? vAlt = null;
      Edge? eAlt = null;
      Vertex2 v = ReferenceEquals(edge.vB, pivot) ? edge.vT! : edge.vB!;

      foreach (Edge e in pivot.edges)
      {
        if (ReferenceEquals(e, edge) || !e.isActive) continue;
        Vertex2 vX = ReferenceEquals(e.vT, pivot) ? e.vB! : e.vT!;
        if (ReferenceEquals(vX, v)) continue;

        int cps = InternalClipper.CrossProductSign(v.pt, pivot.pt, vX.pt);
        if (cps == 0) // collinear paths
        {
          // if pivot is between v and vX then continue;
          // nb: this is important for both horiz and non-horiz collinear
          if ((v.pt.X > pivot.pt.X) == (pivot.pt.X > vX.pt.X)) continue;
        }
        // else if right-turning or not the best edge, then continue
        else if (cps < 0 || (vAlt != null && !RightTurning(vX.pt, pivot.pt, vAlt.pt)))
          continue;
        vAlt = vX;
        eAlt = e;
      }

      if (vAlt == null || vAlt.pt.Y < minY) return;

      // Don't triangulate **across** fixed edges
      if (vAlt.pt.Y < pivot.pt.Y)
      {
        if (IsRightEdge(eAlt!)) return;
      }
      else if (vAlt.pt.Y > pivot.pt.Y)
      {
        if (IsLeftEdge(eAlt!)) return;
      }

      Edge? eX = FindLinkingEdge(vAlt, v, (vAlt.pt.Y > v.pt.Y));
      if (eX == null)
      {
        // be very careful creating loose horizontals at minY
        if (vAlt.pt.Y == v.pt.Y && v.pt.Y == minY &&
          HorizontalBetween(vAlt, v) != null) return;
        eX = CreateEdge(vAlt, v, EdgeKind.loose);
      }

      CreateTriangle(edge, eX, eAlt!);
      if (!EdgeCompleted(eX))
        DoTriangulateRight(eX, vAlt, minY);
    }

    private void AddEdgeToActives(Edge edge)
    {
      // nb: on occassions this method can get called twice for a given edge
      // This is because, in the Execute() method where vertex 'edges'
      // arrays are being parsed, edges can be removed from the array
      // which changes the index of following edges.
      if (edge.isActive) return;

      edge.prevE = null;
      edge.nextE = firstActive;
      edge.isActive = true;
      if (firstActive != null)
        firstActive.prevE = edge;
      firstActive = edge;
    }

    private void RemoveEdgeFromActives(Edge edge)
    {
      // first, remove the edge from its vertices
      RemoveEdgeFromVertex(edge.vB!, edge);
      RemoveEdgeFromVertex(edge.vT!, edge);

      // now remove the edge from double linked list (AEL)
      Edge? prev = edge.prevE;
      Edge? next = edge.nextE;
      if (next != null) next.prevE = prev;
      if (prev != null) prev.nextE = next;
      edge.isActive = false;
      if (ReferenceEquals(firstActive, edge)) firstActive = next;
    }

    private static int VertexListSort(Vertex2 a, Vertex2 b)
    {
      return (a.pt.Y == b.pt.Y) ? a.pt.X.CompareTo(b.pt.X) : b.pt.Y.CompareTo(a.pt.Y);
    }

    private static int EdgeListSort(Edge a, Edge b)
    {
      return a.vL!.pt.X.CompareTo(b.vL!.pt.X);
    }

    public TriangulateResult Execute(Paths64 paths, out Paths64 sol)
    {
      sol = new Paths64();
      if (!AddPaths(paths))
      {
        return TriangulateResult.noPolygons; // oops!
      }

      // if necessary fix path orientation because the algorithm
      // expects clockwise outer paths and counter-clockwise inner paths
      if (lowermostVertex!.innerLM)
      {
        // the orientation of added paths must be wrong, so
        // 1. reverse innerLM flags ...
        while (locMinStack.Count > 0)
        {
          Vertex2 lm = locMinStack.Pop();
          lm.innerLM = !lm.innerLM;
        }
        // 2. swap edge kinds
        foreach (Edge e in allEdges)
          if (e.kind == EdgeKind.ascend)
            e.kind = EdgeKind.descend;
          else
            e.kind = EdgeKind.ascend;
      }
      else
      {
        // path orientation is fine so ...
        locMinStack.Clear();
      }

      // nb: both lists are sorted with the specialised introsort - List.Sort with
      // a delegate costs ~14% of a triangulation (shared generic code, no inlining)
      IntroSort.Sort(CollectionsMarshal.AsSpan(allEdges), new EdgeLess());
      if (!FixupEdgeIntersects())
      {
        CleanUp();
        return TriangulateResult.pathsIntersect; // oops!
      }

      IntroSort.Sort(CollectionsMarshal.AsSpan(allVertices), new Vertex2Less());
      MergeDupOrCollinearVertices();

      long currY = allVertices[0].pt.Y;
      foreach (Vertex2 v in allVertices)
      {
        if (v.edges.Count == 0) continue;
        if (v.pt.Y != currY)
        {
          // JOIN AN INNER LOCMIN WITH A SUITABLE EDGE BELOW
          while (locMinStack.Count > 0)
          {
            Vertex2 lm = locMinStack.Pop();
            Edge? e = CreateInnerLocMinLooseEdge(lm);
            if (e == null)
            {
              CleanUp();
              return TriangulateResult.fail; // oops!
            }

            if (IsHorizontal(e))
            {
              if (ReferenceEquals(e.vL, e.vB))
                DoTriangulateLeft(e, e.vB!, currY);
              else
                DoTriangulateRight(e, e.vB!, currY);
            }
            else
            {
              DoTriangulateLeft(e, e.vB!, currY);
              if (!EdgeCompleted(e))
                DoTriangulateRight(e, e.vB!, currY);
            }

            // and because adding locMin edges to Actives was delayed ..
            AddEdgeToActives(lm.edges[0]);
            AddEdgeToActives(lm.edges[1]);
          }

          while (horzEdgeStack.Count > 0)
          {
            Edge e = horzEdgeStack.Pop();
            if (EdgeCompleted(e)) continue;
            if (ReferenceEquals(e.vB, e.vL)) // #45
            {
              if (IsLeftEdge(e))
                DoTriangulateLeft(e, e.vB!, currY);
            }
            else
              if (IsRightEdge(e))
                DoTriangulateRight(e, e.vB!, currY);
          }
          currY = v.pt.Y;
        }

        for (int i = v.edges.Count - 1; i >= 0; --i)
        {
          // the following line may look superfluous, but within this loop
          // v->edges may be altered with additions and or deletions.
          // So this line *is* necessary (and why we can't use an iterator).
          // Also, we need to use a *descending* index which is safe because
          // any additions will be loose edges which are ignored here.
          if (i >= v.edges.Count) continue;

          Edge e = v.edges[i];
          if (EdgeCompleted(e) || IsLooseEdge(e)) continue;

          if (ReferenceEquals(v, e.vB))
          {
            if (IsHorizontal(e))
              horzEdgeStack.Push(e);
            // delay adding locMin edges to actives
            if (!v.innerLM)
              AddEdgeToActives(e);
          }
          else
          {
            if (IsHorizontal(e))
              horzEdgeStack.Push(e);
            else if (IsLeftEdge(e))
              DoTriangulateLeft(e, e.vB!, v.pt.Y);
            else
              DoTriangulateRight(e, e.vB!, v.pt.Y);
          }
        } // for v->edges loop

        if (v.innerLM) locMinStack.Push(v);

      } // for allVertices loop

      while (horzEdgeStack.Count > 0)
      {
        Edge e = horzEdgeStack.Pop();
        if (!EdgeCompleted(e) && ReferenceEquals(e.vB, e.vL))
          DoTriangulateLeft(e, e.vB!, currY);
      }

      if (useDelaunay)
      {
        // Convert triangles to Delaunay conforming
        while (pendingDelaunayStack.Count > 0)
        {
          Edge e = pendingDelaunayStack.Pop();
          ForceLegal(e);
        }
      }

      Paths64 res = new Paths64();
      res.Capacity = allTriangles.Count;
      foreach (Triangle tri in allTriangles)
      {
        Path64 p = PathFromTriangle(tri);
        int cps = InternalClipper.CrossProductSign(p[0], p[1], p[2]);
        if (cps == 0) continue; // skip any empty triangles
        if (cps < 0) // ccw
          p.Reverse();
        res.Add(p);
      }

      CleanUp();
      sol = res;
      return TriangulateResult.success;
    }

    private void AddPath(Path64 path)
    {
      int len = path.Count, i0 = 0, iPrev, iNext;
      // find the first locMin for the current path
      if (!FindLocMinIdx(path, len, ref i0)) return;
      iPrev = Prev(i0, len);
      while (path[iPrev] == path[i0]) iPrev = Prev(iPrev, len);
      iNext = Next(i0, len);

      // it is possible for a locMin here to simply be a
      // collinear spike that should be ignored, so ...
      int i = i0;
      while (InternalClipper.CrossProductSign(path[iPrev], path[i], path[iNext]) == 0)
      {
        FindLocMinIdx(path, len, ref i);
        if (i == i0) return; // this is an entirely collinear path
        iPrev = Prev(i, len);
        while (path[iPrev] == path[i]) iPrev = Prev(iPrev, len);
        iNext = Next(i, len);
      }

      int vertCnt = allVertices.Count;

      // we are now at the first legitimate locMin
      Vertex2 v0 = new Vertex2(path[i]);
      allVertices.Add(v0);

      if (LeftTurning(path[iPrev], path[i], path[iNext]))
        v0.innerLM = true;
      Vertex2 vPrev = v0;
      i = iNext;

      for (; ; )
      {
        // vPrev is a locMin here
        locMinStack.Push(vPrev);
        // update lowermostVertex ...
        if (lowermostVertex == null ||
          vPrev.pt.Y > lowermostVertex.pt.Y ||
          (vPrev.pt.Y == lowermostVertex.pt.Y &&
          vPrev.pt.X < lowermostVertex.pt.X))
          lowermostVertex = vPrev;

        iNext = Next(i, len);
        if (InternalClipper.CrossProductSign(vPrev.pt, path[i], path[iNext]) == 0)
        {
          i = iNext;
          continue;
        }

        // ascend up next bound to LocMax
        while (path[i].Y <= vPrev.pt.Y)
        {
          Vertex2 v = new Vertex2(path[i]);
          allVertices.Add(v);
          CreateEdge(vPrev, v, EdgeKind.ascend);
          vPrev = v;
          i = iNext;
          iNext = Next(i, len);

          while (InternalClipper.CrossProductSign(vPrev.pt, path[i], path[iNext]) == 0)
          {
            i = iNext;
            iNext = Next(i, len);
          }
        }

        // Now at a locMax, so descend to next locMin
        Vertex2 vPrevPrev = vPrev;
        while (i != i0 && path[i].Y >= vPrev.pt.Y)
        {
          Vertex2 v = new Vertex2(path[i]);
          allVertices.Add(v);
          CreateEdge(v, vPrev, EdgeKind.descend);
          vPrevPrev = vPrev;
          vPrev = v;
          i = iNext;
          iNext = Next(i, len);

          while (InternalClipper.CrossProductSign(vPrev.pt, path[i], path[iNext]) == 0)
          {
            i = iNext;
            iNext = Next(i, len);
          }
        }

        // now at the next locMin
        if (i == i0) break; // break for(;;) loop
        if (LeftTurning(vPrevPrev.pt, vPrev.pt, path[i]))
          vPrev.innerLM = true;
      }
      CreateEdge(v0, vPrev, EdgeKind.descend);

      // finally, ignore this path if is not a polygon or too small
      len = allVertices.Count - vertCnt;
      i = vertCnt;
      if (len < 3 || (len == 3 &&      // or just a very tiny triangle
        ((InternalClipper.DistanceSqr(allVertices[i].pt, allVertices[i + 1].pt) <= 1) ||
          (InternalClipper.DistanceSqr(allVertices[i + 1].pt, allVertices[i + 2].pt) <= 1) ||
          (InternalClipper.DistanceSqr(allVertices[i + 2].pt, allVertices[i].pt) <= 1))))
      {
        for (int j = vertCnt; j < allVertices.Count; ++j)
          allVertices[j].edges.Clear(); // flag to ignore
      }
    }

    private bool AddPaths(Paths64 paths)
    {
      long totalVertexCount = 0;
      foreach (Path64 path in paths) totalVertexCount += path.Count;
      if (totalVertexCount == 0) return false;

      foreach (Path64 path in paths)
        AddPath(path);
      return allVertices.Count > 2;
    }
  }
}
