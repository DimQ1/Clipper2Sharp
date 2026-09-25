using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  /// <summary>
  /// Ports of the remaining C++ tests: CPP/Tests/TestRect.cpp, TestRectClip.cpp,
  /// TestSimplifyPath.cpp and TestTrimCollinear.cpp.
  /// </summary>
  [TestClass]
  public class TestMisc
  {
    [TestMethod]
    public void TestRectOpPlus()
    {
      {
        Rect64 lhs = Rect64.InvalidRect();
        Rect64 rhs = new Rect64(-1, -1, 10, 10);
        Rect64 sum = lhs + rhs;
        Assert.AreEqual(rhs, sum);
        Rect64 t = lhs; lhs = rhs; rhs = t;
        sum = lhs + rhs;
        Assert.AreEqual(lhs, sum);
      }
      {
        Rect64 lhs = Rect64.InvalidRect();
        Rect64 rhs = new Rect64(1, 1, 10, 10);
        Rect64 sum = lhs + rhs;
        Assert.AreEqual(rhs, sum);
        Rect64 t = lhs; lhs = rhs; rhs = t;
        sum = lhs + rhs;
        Assert.AreEqual(lhs, sum);
      }
      {
        Rect64 lhs = new Rect64(0, 0, 1, 1);
        Rect64 rhs = new Rect64(-1, -1, 0, 0);
        Rect64 expected = new Rect64(-1, -1, 1, 1);
        Rect64 sum = lhs + rhs;
        Assert.AreEqual(expected, sum);
        Rect64 t = lhs; lhs = rhs; rhs = t;
        sum = lhs + rhs;
        Assert.AreEqual(expected, sum);
      }
      {
        Rect64 lhs = new Rect64(-10, -10, -1, -1);
        Rect64 rhs = new Rect64(1, 1, 10, 10);
        Rect64 expected = new Rect64(-10, -10, 10, 10);
        Rect64 sum = lhs + rhs;
        Assert.AreEqual(expected, sum);
        Rect64 t = lhs; lhs = rhs; rhs = t;
        sum = lhs + rhs;
        Assert.AreEqual(expected, sum);
      }
    }

    [TestMethod]
    public void TestRectClip1()
    {
      Paths64 sub = new Paths64(), clp = new Paths64(), sol;
      Rect64 rect = new Rect64(100, 100, 700, 500);
      clp.Add(rect.AsPath());
      sub.Add(Clipper.MakePath(new int[] { 100, 100, 700, 100, 700, 500, 100, 500 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(Clipper.Area(sol) == Clipper.Area(sub));
      sub.Clear();
      sub.Add(Clipper.MakePath(new int[] { 110, 110, 700, 100, 700, 500, 100, 500 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(Clipper.Area(sol) == Clipper.Area(sub));
      sub.Clear();
      sub.Add(Clipper.MakePath(new int[] { 90, 90, 700, 100, 700, 500, 100, 500 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(Clipper.Area(sol) == Clipper.Area(clp));
      sub.Clear();
      sub.Add(Clipper.MakePath(new int[] { 110, 110, 690, 110, 690, 490, 110, 490 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(Clipper.Area(sol) == Clipper.Area(sub));
      sub.Clear();
      clp.Clear();
      rect = new Rect64(390, 290, 410, 310);
      clp.Add(rect.AsPath());
      sub.Add(Clipper.MakePath(new int[] { 410, 290, 500, 290, 500, 310, 410, 310 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(sol.Count == 0);
      sub.Clear();
      sub.Add(Clipper.MakePath(new int[] { 430, 290, 470, 330, 390, 330 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(sol.Count == 0);
      sub.Clear();
      sub.Add(Clipper.MakePath(new int[] { 450, 290, 480, 330, 450, 330 }));
      sol = Clipper.RectClip(rect, sub);
      Assert.IsTrue(sol.Count == 0);
      sub.Clear();
      sub.Add(Clipper.MakePath(new int[] { 208, 66, 366, 112, 402, 303,
        234, 332, 233, 262, 243, 140, 215, 126, 40, 172 }));
      rect = new Rect64(237, 164, 322, 248);
      sol = Clipper.RectClip(rect, sub);
      Rect64 solBounds = Clipper.GetBounds(sol);
      Assert.AreEqual(rect.Width, solBounds.Width);
      Assert.AreEqual(rect.Height, solBounds.Height);
    }

    [TestMethod]
    public void TestRectClip2() // #597
    {
      Rect64 rect = new Rect64(54690, 0, 65628, 6000);
      Paths64 subject = new Paths64 {
        Clipper.MakePath(new long[] { 700000, 6000, 0, 6000, 0, 5925, 700000, 5925 }) };
      Paths64 solution = Clipper.RectClip(rect, subject);
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 4);
    }

    [TestMethod]
    public void TestRectClip3() // #637
    {
      Rect64 r = new Rect64(-1800000000L, -137573171L, -1741475021L, 3355443L);
      Paths64 subject = new Paths64(), solution;
      subject.Add(Clipper.MakePath(new long[] { -1800000000L, 10005000L,
        -1800000000L, -5000L, -1789994999L, -5000L, -1789994999L, 10005000L }));
      solution = Clipper.RectClip(r, subject);
      Assert.IsTrue(solution.Count == 1);
    }

    [TestMethod]
    public void TestRectClipOrientation() // #864
    {
      Rect64 rect = new Rect64(1222, 1323, 3247, 3348);
      Path64 subject = Clipper.MakePath(new int[] { 375, 1680, 1915, 4716, 5943, 586, 3987, 152 });
      RectClip64 clip = new RectClip64(rect);
      Paths64 solution = clip.Execute(new Paths64 { subject });
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(Clipper.IsPositive(subject), Clipper.IsPositive(solution[0]));
    }

    [TestMethod]
    public void TestRectClipLines1()
    {
      Rect64 rect = new Rect64(0, 0, 100, 100);
      Paths64 subject = new Paths64 { Clipper.MakePath(new int[] { -50, 50, 150, 50 }) };
      Paths64 solution = Clipper.RectClipLines(rect, subject);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(2, solution[0].Count);
      Assert.IsTrue(Math.Abs(Clipper.Length(solution[0]) - 100) < 0.0001);
    }

    [TestMethod]
    public void TestSimplifyPath()
    {
      Path64 input1 = Clipper.MakePath(new int[] {
        0,0, 1,1, 0,20, 0,21, 1,40, 0,41, 0,60, 0,61, 0,80, 1,81, 0,100 });
      Path64 output1 = Clipper.SimplifyPath(input1, 2, false);
      Assert.AreEqual(100.0, Clipper.Length(output1));
      Assert.AreEqual(2, output1.Count);
    }

    [TestMethod]
    public void TestTrimCollinear()
    {
      Path64 input1 = Clipper.MakePath(new int[] {
        10,10, 10,10, 50,10, 100,10, 100,100, 10,100, 10,10, 20,10 });
      Path64 output1 = Clipper.TrimCollinear(input1, false);
      Assert.AreEqual(4, output1.Count);

      Path64 input2 = Clipper.MakePath(new int[] {
        10,10, 10,10, 100,10, 100,100, 10,100, 10,10, 10,10 });
      Path64 output2 = Clipper.TrimCollinear(input2, true);
      Assert.AreEqual(5, output2.Count);

      Path64 input3 = Clipper.MakePath(new int[] {
        10,10, 10,50, 10,10, 50,10,50,50,
        50,10, 70,10, 70,50, 70,10, 50,10, 100,10, 100,50, 100,10 });
      Path64 output3 = Clipper.TrimCollinear(input3);
      Assert.AreEqual(0, output3.Count);

      Path64 input4 = Clipper.MakePath(new int[] {
        2,3, 3,4, 4,4, 4,5, 7,5,
        8,4, 8,3, 9,3, 8,3, 7,3, 6,3, 5,3, 4,3, 3,3, 2,3 });
      Path64 output4a = Clipper.TrimCollinear(input4);
      Path64 output4b = Clipper.TrimCollinear(output4a);
      int area4a = (int) Clipper.Area(output4a);
      int area4b = (int) Clipper.Area(output4b);
      Assert.AreEqual(7, output4a.Count);
      Assert.AreEqual(-9, area4a);
      Assert.AreEqual(output4a.Count, output4b.Count);
      Assert.AreEqual(area4a, area4b);
    }

    [TestMethod]
    public void TestMultiGroupOffset()
    {
      // nb: a large multi group offset goes through the parallel group path
      // (BulkOps.ShouldParallelize), so this also exercises that branch
      Paths64 group1 = new Paths64();
      Paths64 group2 = new Paths64();
      for (int i = 0; i < 16; i++)
      {
        Path64 square = Clipper.MakePath(new int[] {
          i * 400, 0, i * 400 + 300, 0, i * 400 + 300, 300, i * 400, 300 });
        Path64 circle = Clipper.Ellipse(new Point64(i * 400 + 150, 500), 120, 120, 512);
        group1.Add(square);
        group2.Add(circle);
      }

      ClipperOffset offseter = new ClipperOffset();
      offseter.AddPaths(group1, JoinType.Miter, EndType.Polygon);
      offseter.AddPaths(group2, JoinType.Round, EndType.Polygon);
      Paths64 solution = new Paths64();
      offseter.Execute(10, solution);

      // the groups offset one at a time, combined and finished exactly like the
      // multi group call does, must give the identical result
      Paths64 part1 = new Paths64(), part2 = new Paths64();
      ClipperOffset o1 = new ClipperOffset();
      o1.AddPaths(group1, JoinType.Miter, EndType.Polygon);
      o1.Execute(10, part1);
      ClipperOffset o2 = new ClipperOffset();
      o2.AddPaths(group2, JoinType.Round, EndType.Polygon);
      o2.Execute(10, part2);

      Paths64 all = new Paths64();
      all.AddRange(part1);
      all.AddRange(part2);
      Paths64 expected = Clipper.Union(all, FillRule.Positive);

      Assert.AreEqual(expected.Count, solution.Count);
      Assert.AreEqual(Clipper.Area(expected), Clipper.Area(solution));
    }

    [TestMethod]
    public void TestParallelBulkOps()
    {
      // a path set big enough to trigger the multi-threaded code paths
      Paths64 paths = new Paths64();
      for (int i = 0; i < 200; i++)
        paths.Add(Clipper.Ellipse(new Point64(i * 10, 0), 100, 60, 256));

      double expectedArea = 0;
      foreach (Path64 p in paths) expectedArea += Clipper.Area(p);

      Assert.AreEqual(expectedArea, Clipper.Area(paths));

      Rect64 bounds = Clipper.GetBounds(paths);
      Assert.AreEqual(199 * 10 + 100, bounds.right);
      Assert.AreEqual(-60, bounds.top);
      Assert.AreEqual(60, bounds.bottom);

      PathsD asD = Clipper.PathsD(paths);
      Assert.AreEqual(paths.Count, asD.Count);
      Paths64 backTo64 = Clipper.Paths64(asD);
      Assert.AreEqual(paths.Count, backTo64.Count);
      Assert.AreEqual(expectedArea, Clipper.Area(backTo64));

      Paths64 simplified = Clipper.SimplifyPaths(paths, 0.1);
      Assert.AreEqual(paths.Count, simplified.Count);

      // nb: the multi threaded path-set helpers must produce exactly the same
      // result as the per path (sequential) calls
      Paths64 simplifiedSeq = new Paths64();
      foreach (Path64 p in paths) simplifiedSeq.Add(Clipper.SimplifyPath(p, 0.1));
      Assert.AreEqual(simplifiedSeq.Count, simplified.Count);
      for (int i = 0; i < simplified.Count; i++)
      {
        Assert.AreEqual(simplifiedSeq[i].Count, simplified[i].Count);
        for (int j = 0; j < simplified[i].Count; j++)
          Assert.AreEqual(simplifiedSeq[i][j], simplified[i][j]);
      }

      Paths64 scaled = Clipper.ScalePaths(paths, 2.0);
      for (int i = 0; i < paths.Count; i++)
      {
        Path64 expected = Clipper.ScalePath(paths[i], 2.0);
        Assert.AreEqual(expected.Count, scaled[i].Count);
        for (int j = 0; j < expected.Count; j++)
          Assert.AreEqual(expected[j], scaled[i][j]);
      }

      Paths64 reversed = Clipper.ReversePaths(paths);
      for (int i = 0; i < paths.Count; i++)
      {
        Path64 expected = Clipper.ReversePath(paths[i]);
        Assert.AreEqual(expected.Count, reversed[i].Count);
        for (int j = 0; j < expected.Count; j++)
          Assert.AreEqual(expected[j], reversed[i][j]);
      }
    }

    [TestMethod]
    public void TestPointInPolygonWideCoordinates()    {
      // nb: CrossProductSign has a 64 bit fast path for coordinates whose
      // differences stay below 2^31 and falls back to 128 bit arithmetic
      // beyond that (#834, #835). These vertices are far enough apart
      // (9e9 > 2^31) to be handled by the wide path.
      Path64 triangle = Clipper.MakePath(new long[] {
        0, 0, 9000000000, 1000000000, 1000000000, 9000000000 });

      Assert.AreEqual(PointInPolygonResult.IsInside,
        Clipper.PointInPolygon(new Point64(3333333333, 3333333333), triangle));

      // (4500000000, 500000000) sits exactly on the (0,0)->(9e9,1e9) edge
      Assert.AreEqual(PointInPolygonResult.IsOn,
        Clipper.PointInPolygon(new Point64(4500000000, 500000000), triangle));

      Assert.AreEqual(PointInPolygonResult.IsOutside,
        Clipper.PointInPolygon(new Point64(8000000000, 8000000000), triangle));

      // the same shape next to the origin must give the same answers (the
      // fast path), so the two branches agree with one another
      Path64 small = Clipper.MakePath(new long[] { 0, 0, 9000, 1000, 1000, 9000 });
      Assert.AreEqual(PointInPolygonResult.IsInside,
        Clipper.PointInPolygon(new Point64(3333, 3333), small));
      Assert.AreEqual(PointInPolygonResult.IsOn,
        Clipper.PointInPolygon(new Point64(4500, 500), small));
      Assert.AreEqual(PointInPolygonResult.IsOutside,
        Clipper.PointInPolygon(new Point64(8000, 8000), small));

      // ... and clipping still works with coordinates beyond 2^31
      Paths64 subject = new Paths64 { triangle };
      Paths64 clip = new Paths64 { Clipper.MakePath(new long[] {
        4000000000, 0, 6000000000, 0, 6000000000, 4000000000, 4000000000, 4000000000 }) };
      Paths64 solution = Clipper.Intersect(subject, clip, FillRule.NonZero);
      Assert.IsTrue(solution.Count > 0);
      Paths64 expected = Clipper.Intersect(
        new Paths64 { Clipper.MakePath(new long[] { 0, 0, 9000, 1000, 1000, 9000 }) },
        new Paths64 { Clipper.MakePath(new long[] { 4000, 0, 6000, 0, 6000, 4000, 4000, 4000 }) },
        FillRule.NonZero);
      Assert.AreEqual(expected.Count, solution.Count);
      // nb: the copies are exact, so the areas scale by 10^12 - but the two
      // results snap their intersection points to their own coordinate grid, and
      // the coarse (x1) grid is the less accurate of the two, hence the
      // tolerance instead of an exact comparison
      double smallArea = Clipper.Area(expected), hugeArea = Clipper.Area(solution) / 1e12;
      Assert.AreEqual(smallArea, hugeArea, Math.Abs(smallArea) * 1e-3);
    }

    [TestMethod]
    public void TestPointInPolygonLocator()
    {
      // the prepared locator must give exactly the answers of the vertex scan,
      // including every 'on' case: probes on vertices, on horizontal and sloped
      // edges, on the lines of horizontal runs, and far outside
      Random rnd = new Random(4242);
      for (int round = 0; round < 400; round++)
      {
        int n = rnd.Next(3, 60);
        long range = (round % 4) switch { 0 => 6, 1 => 50, 2 => 5000, _ => 6_000_000_000 };
        Path64 poly = new Path64(n);
        for (int i = 0; i < n; i++)
        {
          long x = rnd.NextInt64(-range, range + 1), y = rnd.NextInt64(-range, range + 1);
          if (i > 0 && rnd.Next(4) == 0) y = poly[i - 1].Y;          // horizontal runs
          if (i > 0 && rnd.Next(9) == 0) x = poly[i - 1].X;          // vertical edges
          if (i > 0 && rnd.Next(15) == 0) { x = poly[i - 1].X; y = poly[i - 1].Y; } // duplicates
          poly.Add(new Point64(x, y));
        }
        PointInPolygonLocator locator = new PointInPolygonLocator(poly);
        List<Point64> probes = new List<Point64>();
        foreach (Point64 v in poly) probes.Add(v);
        for (int i = 0; i < n; i++)
        {
          Point64 a = poly[i], b = poly[(i + 1) % n];
          probes.Add(new Point64((a.X + b.X) / 2, (a.Y + b.Y) / 2));
          probes.Add(new Point64(a.X - 1, a.Y));
          probes.Add(new Point64(a.X + 1, a.Y));
        }
        for (int i = 0; i < 300; i++)
          probes.Add(new Point64(rnd.NextInt64(-range - 2, range + 3), rnd.NextInt64(-range - 2, range + 3)));
        Point64[] pts = probes.ToArray();
        PointInPolygonResult[] batch = new PointInPolygonResult[pts.Length];
        Clipper.PointInPolygon(poly, pts, batch);
        for (int i = 0; i < pts.Length; i++)
        {
          PointInPolygonResult expected = Clipper.PointInPolygon(pts[i], poly);
          Assert.AreEqual(expected, locator.Locate(pts[i]), $"round {round}, probe {pts[i]}");
          Assert.AreEqual(expected, batch[i], $"round {round}, batch probe {pts[i]}");
        }
      }
      // degenerate polygons
      Path64 flat = Clipper.MakePath(new long[] { 0, 5, 10, 5, 20, 5 });
      Assert.AreEqual(PointInPolygonResult.IsOutside, new PointInPolygonLocator(flat).Locate(new Point64(10, 5)));
      Assert.AreEqual(Clipper.PointInPolygon(new Point64(10, 5), flat), new PointInPolygonLocator(flat).Locate(new Point64(10, 5)));
      Path64 two = Clipper.MakePath(new long[] { 0, 0, 10, 10 });
      Assert.AreEqual(Clipper.PointInPolygon(new Point64(5, 5), two), new PointInPolygonLocator(two).Locate(new Point64(5, 5)));
    }

    [TestMethod]
    public void TestBooleanOpParallel()
    {
      // clusters of overlapping shapes, far apart from each other: the parallel
      // entry point must produce the same regions as the single operation
      Random rnd = new Random(77);
      Paths64 subj = new Paths64(), clip = new Paths64();
      for (int k = 0; k < 60; k++)
      {
        long ox = (k % 8) * 100_000, oy = (k / 8) * 100_000;
        for (int j = 0; j < 4; j++)
        {
          Path64 e = Clipper.Ellipse(new Point64(ox + rnd.Next(-3000, 3000), oy + rnd.Next(-3000, 3000)),
            rnd.Next(2000, 9000), rnd.Next(2000, 9000), 90);
          if (j < 3) subj.Add(e); else clip.Add(e);
        }
      }
      foreach (ClipType ct in new[] { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor })
        foreach (FillRule fr in new[] { FillRule.NonZero, FillRule.EvenOdd })
        {
          Paths64 seq = Clipper.BooleanOp(ct, fr, subj, clip);
          Paths64 par = Clipper.BooleanOpParallel(ct, fr, subj, clip);
          // nb: the regions agree up to the engine's integer rounding, not bit for
          // bit: the other clusters' scanlines split the scanbeams of a cluster
          // differently in the single operation, and an intersection that rounds
          // outside its scanbeam is snapped to the beam's edge
          Assert.AreEqual(seq.Count, par.Count, $"{ct} {fr}: path count");
          Assert.AreEqual(Clipper.Area(seq), Clipper.Area(par), 1e-5 * Math.Abs(Clipper.Area(seq)) + 1, $"{ct} {fr}: area");
          // a vertex that moves by a unit changes a path's area by at most about
          // its perimeter, so that is the per path tolerance
          List<(double area, double len)> a1 = new List<(double, double)>();
          List<double> a2 = new List<double>();
          foreach (Path64 p in seq) a1.Add((Clipper.Area(p), Clipper.Length(p, true)));
          foreach (Path64 p in par) a2.Add(Clipper.Area(p));
          a1.Sort(); a2.Sort();
          for (int i = 0; i < a1.Count; i++)
            Assert.AreEqual(a1[i].area, a2[i], a1[i].len + 1, $"{ct} {fr}: path {i} area");
          // deterministic
          Paths64 par2 = Clipper.BooleanOpParallel(ct, fr, subj, clip);
          Assert.AreEqual(par.Count, par2.Count);
          for (int i = 0; i < par.Count; i++) CollectionAssert.AreEqual(par[i], par2[i]);
        }
    }

    [TestMethod]
    public void TestReuseableDataContainer()
    {
      // paths handed over through a ReuseableDataContainer64 must clip exactly
      // like the same paths added directly, also when mixed with direct paths,
      // reused by several engines, and after the container was cleared
      Random rnd = new Random(31);
      for (int round = 0; round < 60; round++)
      {
        Paths64 subj = new Paths64(), clip = new Paths64(), open = new Paths64();
        for (int k = 0; k < rnd.Next(1, 5); k++)
        {
          Path64 p = new Path64();
          for (int i = 0; i < rnd.Next(3, 30); i++) p.Add(new Point64(rnd.Next(-500, 500), rnd.Next(-500, 500)));
          subj.Add(p);
        }
        for (int k = 0; k < rnd.Next(1, 4); k++)
        {
          Path64 p = new Path64();
          for (int i = 0; i < rnd.Next(3, 30); i++) p.Add(new Point64(rnd.Next(-500, 500), rnd.Next(-500, 500)));
          clip.Add(p);
        }
        Path64 line = new Path64();
        for (int i = 0; i < 6; i++) line.Add(new Point64(rnd.Next(-500, 500), rnd.Next(-500, 500)));
        open.Add(line);
        ClipType ct = (ClipType) (1 + round % 4);
        FillRule fr = (FillRule) (round % 4);

        Clipper64 direct = new Clipper64();
        direct.AddSubject(subj);
        direct.AddOpenSubject(open);
        direct.AddClip(clip);
        Paths64 expected = new Paths64(), expectedOpen = new Paths64();
        direct.Execute(ct, fr, expected, expectedOpen);

        ReuseableDataContainer64 data = new ReuseableDataContainer64();
        data.AddPaths(subj, PathType.Subject, false);
        data.AddPaths(open, PathType.Subject, true);
        for (int engine = 0; engine < 2; engine++)
        {
          Clipper64 c = new Clipper64();
          c.AddReuseableData(data);
          c.AddClip(clip);
          Paths64 sol = new Paths64(), solOpen = new Paths64();
          c.Execute(ct, fr, sol, solOpen);
          Assert.AreEqual(expected.Count, sol.Count, $"round {round}");
          for (int i = 0; i < sol.Count; i++) CollectionAssert.AreEqual(expected[i], sol[i], $"round {round} path {i}");
          Assert.AreEqual(expectedOpen.Count, solOpen.Count, $"round {round} open");
          for (int i = 0; i < solOpen.Count; i++) CollectionAssert.AreEqual(expectedOpen[i], solOpen[i]);
          if (engine == 0)
          {
            // the engine keeps its own copy: clearing the container is harmless
            data.Clear();
            Paths64 again = new Paths64(), againOpen = new Paths64();
            c.Execute(ct, fr, again, againOpen);
            Assert.AreEqual(expected.Count, again.Count);
            data.AddPaths(subj, PathType.Subject, false);
            data.AddPaths(open, PathType.Subject, true);
          }
        }
      }
    }

    [TestMethod]
    public void TestIntersectNodeSorter()
    {
      // nb: the engine sorts its intersection list with a hand written introsort
      // (Clipper.Sorts.cs) because the generic Span.Sort routes every comparison
      // through an interface. This pins its contract: the engine's ordering, no
      // lost or duplicated nodes, and exact agreement with the reference
      // relation whenever the keys are distinct.
      Random rnd = new Random(20260925);
      for (int round = 0; round < 12; round++)
      {
        int n = rnd.Next(0, 2500);
        bool uniqueKeys = (round & 1) == 1;
        int span = uniqueKeys ? 1_000_000 : 40; // small span => many equal keys
        IntersectNode[] nodes = new IntersectNode[n];
        IntersectNode[] reference = new IntersectNode[n];
        for (int i = 0; i < n; i++)
        {
          IntersectNode node = new IntersectNode(-1, -1,
            new Point64(rnd.Next(-span, span), rnd.Next(-span, span)));
          nodes[i] = node;
          reference[i] = node;
        }
        Array.Sort(reference, (a, b) => ClipperEngine.IntersectListSort(a, b));
        IntroSort.Sort(nodes.AsSpan(), new IntersectNodeLess());

        for (int i = 1; i < n; i++)
        {
          IntersectNode a = nodes[i - 1], b = nodes[i];
          Assert.IsTrue(a.pt.Y != b.pt.Y ? b.pt.Y < a.pt.Y : a.pt.X <= b.pt.X,
            "nodes are not in bottom-up, left-to-right order");
        }

        List<string> expectedKeys = new List<string>(), sortedKeys = new List<string>();
        for (int i = 0; i < n; i++)
        {
          expectedKeys.Add($"{reference[i].pt.Y},{reference[i].pt.X}");
          sortedKeys.Add($"{nodes[i].pt.Y},{nodes[i].pt.X}");
        }
        expectedKeys.Sort();
        sortedKeys.Sort();
        CollectionAssert.AreEqual(expectedKeys, sortedKeys, "nodes were lost or duplicated");

        if (uniqueKeys)
        {
          for (int i = 0; i < n; i++)
            Assert.AreEqual(reference[i].pt, nodes[i].pt, "order differs with distinct keys");
        }
      }
    }

    [TestMethod]
    public void TestTriangulateSquare()
    {
      Paths64 subject = new Paths64
      {
        Clipper.MakePath(new int[] { 0, 0, 100, 0, 100, 100, 0, 100 })
      };
      TriangulateResult result = Clipper.Triangulate(subject, out Paths64 solution);
      Assert.AreEqual(TriangulateResult.success, result);
      Assert.AreEqual(2, solution.Count);
      Assert.IsTrue(Math.Abs(Clipper.Area(solution) - 10000) < 0.0001);
    }

    [TestMethod]
    public void TestTriangulatePolygonWithHole()
    {
      Paths64 subject = new Paths64
      {
        // outer square (clockwise)
        Clipper.MakePath(new int[] { 0, 0, 100, 0, 100, 100, 0, 100 }),
        // inner square / hole (counter-clockwise)
        Clipper.MakePath(new int[] { 25, 25, 25, 75, 75, 75, 75, 25 })
      };
      TriangulateResult result = Clipper.Triangulate(subject, out Paths64 solution);
      Assert.AreEqual(TriangulateResult.success, result);
      // 100 x 100 outer minus the 50 x 50 hole
      Assert.IsTrue(Math.Abs(Clipper.Area(solution) - 7500) < 0.0001,
        string.Format("unexpected triangulated area: {0}", Clipper.Area(solution)));
    }

    [TestMethod]
    public void TestTriangulateDouble()
    {
      PathsD subject = new PathsD
      {
        Clipper.MakePath(new double[] { 0, 0, 100.5, 0, 100.5, 100.5, 0, 100.5 })
      };
      TriangulateResult result = Clipper.Triangulate(subject, 2, out PathsD solution);
      Assert.AreEqual(TriangulateResult.success, result);
      Assert.AreEqual(2, solution.Count);
      Assert.IsTrue(Math.Abs(Clipper.Area(solution) - 100.5 * 100.5) < 0.001);
    }

    [TestMethod]
    public void TestMinkowskiSum()
    {
      Path64 pattern = Clipper.MakePath(new int[] { 0, 0, 0, 5, 5, 5, 5, 0 });
      Path64 path = Clipper.MakePath(new int[] { 10, 10, 20, 10, 20, 20, 10, 20 });
      Paths64 solution = Clipper.MinkowskiSum(pattern, path, true);
      // nb: the expected area (200 rather than 225) reproduces the reference
      // implementation's behaviour exactly - see Results/minkowski-check.
      Assert.AreEqual(2, solution.Count);
      Assert.AreEqual(200.0, Clipper.Area(solution));

      Paths64 diff = Clipper.MinkowskiDiff(pattern, path, true);
      Assert.AreEqual(2, diff.Count);
      Assert.AreEqual(200.0, Clipper.Area(diff));
    }

    [TestMethod]
    public void TestEllipse()
    {
      Path64 path = Clipper.Ellipse(new Point64(0, 0), 100, 50);
      Assert.IsTrue(path.Count > 8);
      // the polygon should approximate (but not exceed) the ellipse's area
      double a = Clipper.Area(path);
      Assert.IsTrue(a > 0 && a <= Math.PI * 100.0 * 50.0 + 0.0001);
    }

    [TestMethod]
    public void TestRamerDouglasPeucker()
    {
      Path64 path = Clipper.MakePath(new int[] {
        0,0, 1,1, 0,20, 0,21, 1,40, 0,41, 0,60, 0,61, 0,80, 1,81, 0,100 });
      Path64 result = Clipper.RamerDouglasPeucker(path, 2);
      Assert.AreEqual(2, result.Count);
    }
  }
}
