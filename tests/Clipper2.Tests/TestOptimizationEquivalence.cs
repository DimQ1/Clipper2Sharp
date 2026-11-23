using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  /// <summary>
  /// The 2.0.5 memory and speed changes were meant to change no result. The
  /// golden output check covers the integer API, ClipperD and PolyTreeD; the
  /// tests below cover the rest by running each changed function against a
  /// verbatim copy of its 2.0.4 implementation (class Legacy) on random input,
  /// and compare every coordinate (and Z), the path order and the tree nesting.
  /// </summary>
  [TestClass]
  public class TestOptimizationEquivalence
  {
    // ---- random input ---------------------------------------------------------

    /// <summary>A path with duplicate runs, collinear runs, spikes and horizontals.</summary>
    private static Path64 RandomPath(Random rnd, int n, long range)
    {
      Path64 p = new Path64(n);
      long x = rnd.NextInt64(-range, range), y = rnd.NextInt64(-range, range);
      for (int i = 0; i < n; i++)
      {
        switch (rnd.Next(8))
        {
          case 0: break;                                   // duplicate
          case 1: x += rnd.Next(1, 4); break;              // horizontal / collinear
          case 2: y += rnd.Next(1, 4); break;
          case 3: x = -x; break;                           // spike
          default:
            x = rnd.NextInt64(-range, range);
            y = rnd.NextInt64(-range, range);
            break;
        }
        p.Add(new Point64(x, y));
      }
      if (rnd.Next(3) == 0 && n > 0) p.Add(p[0]);          // closing duplicate
      return p;
    }

    private static PathD RandomPathD(Random rnd, int n, double range)
    {
      PathD p = new PathD(n);
      double x = 0, y = 0;
      for (int i = 0; i < n; i++)
      {
        switch (rnd.Next(6))
        {
          case 0: break;
          case 1: x += rnd.NextDouble() * 1e-3; break;     // near duplicate
          default:
            x = (rnd.NextDouble() * 2 - 1) * range;
            y = (rnd.NextDouble() * 2 - 1) * range;
            break;
        }
        p.Add(new PointD(x, y));
      }
      return p;
    }

    private static PathD Circle(double cx, double cy, double r, int n)
    {
      PathD p = new PathD(n);
      for (int i = 0; i < n; i++)
      {
        double a = 2 * Math.PI * i / n;
        p.Add(new PointD(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
      }
      return p;
    }

    // ---- exact comparison -------------------------------------------------------

    private static void Same(Path64 expected, Path64 actual, string what)
    {
      Assert.AreEqual(expected.Count, actual.Count, $"{what}: point count");
      for (int i = 0; i < expected.Count; i++)
      {
        Assert.AreEqual(expected[i].X, actual[i].X, $"{what}: X of point {i}");
        Assert.AreEqual(expected[i].Y, actual[i].Y, $"{what}: Y of point {i}");
      }
    }

    private static void Same(PathD expected, PathD actual, string what)
    {
      Assert.AreEqual(expected.Count, actual.Count, $"{what}: point count");
      for (int i = 0; i < expected.Count; i++)
      {
        // bitwise: NaN and -0.0 must survive unchanged as well
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected[i].x),
          BitConverter.DoubleToInt64Bits(actual[i].x), $"{what}: x of point {i}");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected[i].y),
          BitConverter.DoubleToInt64Bits(actual[i].y), $"{what}: y of point {i}");
      }
    }

    private static void Same(Paths64 expected, Paths64 actual, string what)
    {
      Assert.AreEqual(expected.Count, actual.Count, $"{what}: path count");
      for (int i = 0; i < expected.Count; i++) Same(expected[i], actual[i], $"{what}, path {i}");
    }

    private static void Same(PathsD expected, PathsD actual, string what)
    {
      Assert.AreEqual(expected.Count, actual.Count, $"{what}: path count");
      for (int i = 0; i < expected.Count; i++) Same(expected[i], actual[i], $"{what}, path {i}");
    }

    private static void SameTree(PolyPathD expected, PolyPathD actual, string what)
    {
      Assert.AreEqual(expected.Count, actual.Count, $"{what}: child count");
      Assert.AreEqual(expected.IsHole, actual.IsHole, $"{what}: hole flag");
      if (expected.Polygon != null) Same(expected.Polygon, actual.Polygon!, what);
      for (int i = 0; i < expected.Count; i++)
        SameTree(expected[i], actual[i], $"{what}/{i}");
    }

    // ---- the tests --------------------------------------------------------------

    [TestMethod]
    public void TestSimplifyPathMatchesLegacy()
    {
      Random rnd = new Random(1);
      for (int iter = 0; iter < 3000; iter++)
      {
        Path64 p = RandomPath(rnd, rnd.Next(0, 120), rnd.Next(2) == 0 ? 50 : 1_000_000);
        double eps = new[] { 0.0, 0.5, 2, 10, 1000, 1e9 }[rnd.Next(6)];
        bool closed = rnd.Next(2) == 0;
        Same(Legacy.SimplifyPath(p, eps, closed), Clipper.SimplifyPath(p, eps, closed),
          $"Path64 #{iter} eps {eps} closed {closed}");

        PathD d = Clipper.PathD(p);
        if (rnd.Next(4) == 0) d = RandomPathD(rnd, rnd.Next(0, 80), 100);
        Same(Legacy.SimplifyPath(d, eps, closed), Clipper.SimplifyPath(d, eps, closed),
          $"PathD #{iter} eps {eps} closed {closed}");
      }
      // long runs of removed points (where the old flag scan was slowest)
      PathD dense = Circle(0, 0, 100, 5000);
      Same(Legacy.SimplifyPath(dense, 5, true), Clipper.SimplifyPath(dense, 5, true), "dense circle");
    }

    [TestMethod]
    public void TestStripAndTrimMatchLegacy()
    {
      Random rnd = new Random(2);
      for (int iter = 0; iter < 3000; iter++)
      {
        Path64 p = RandomPath(rnd, rnd.Next(0, 60), rnd.Next(2) == 0 ? 3 : 1000);
        bool closed = rnd.Next(2) == 0;
        Same(Legacy.StripDuplicates(p, closed), Clipper.StripDuplicates(p, closed), $"StripDuplicates #{iter}");
        Same(Legacy.TrimCollinear(p, !closed), Clipper.TrimCollinear(p, !closed), $"TrimCollinear #{iter}");
        double d2 = new[] { 0.0, 1, 4, 100 }[rnd.Next(4)];
        Same(Legacy.StripNearEqual(p, d2, closed), InternalClipper.StripNearEqual(p, d2, closed), $"StripNearEqual64 #{iter}");

        PathD d = RandomPathD(rnd, rnd.Next(0, 60), rnd.Next(2) == 0 ? 1e-3 : 10);
        Same(Legacy.StripNearDuplicates(d, d2, closed), Clipper.StripNearDuplicates(d, d2, closed), $"StripNearDuplicates #{iter}");
        Same(Legacy.StripNearEqual(d, d2, closed), InternalClipper.StripNearEqual(d, d2, closed), $"StripNearEqualD #{iter}");
      }
      // the Paths forms keep the order (serial and parallel)
      Paths64 many = new Paths64();
      for (int i = 0; i < 9000; i++) many.Add(RandomPath(rnd, rnd.Next(0, 6), 3));
      Paths64 stripped = InternalClipper.StripNearEqual(many, 1, true);
      for (int i = 0; i < many.Count; i++)
        Same(Legacy.StripNearEqual(many[i], 1, true), stripped[i], $"StripNearEqual(Paths64) #{i}");
    }

    [TestMethod]
    public void TestMinkowskiMatchesLegacy()
    {
      Random rnd = new Random(3);
      for (int iter = 0; iter < 150; iter++)
      {
        Path64 pattern = RandomPath(rnd, rnd.Next(0, 8), 20);
        Path64 path = RandomPath(rnd, rnd.Next(0, 40), 500);
        bool closed = rnd.Next(2) == 0;
        Same(Legacy.Minkowski(pattern, path, true, closed), Clipper.MinkowskiSum(pattern, path, closed), $"sum #{iter}");
        Same(Legacy.Minkowski(pattern, path, false, closed), Clipper.MinkowskiDiff(pattern, path, closed), $"diff #{iter}");

        PathD pd = Clipper.ScalePathD(pattern, 0.01), qd = Clipper.ScalePathD(path, 0.01);
        Same(Legacy.Minkowski(pd, qd, true, closed), Clipper.MinkowskiSum(pd, qd, closed), $"sumD #{iter}");
        Same(Legacy.Minkowski(pd, qd, false, closed), Clipper.MinkowskiDiff(pd, qd, closed), $"diffD #{iter}");
      }
    }

    [TestMethod]
    public void TestInflatePathsDMatchesLegacy()
    {
      Random rnd = new Random(4);
      double[] deltas = { -3, -0.004, 0, 0.001, 0.3, 2.5, 12 };
      int legacyThrew = 0;
      for (int iter = 0; iter < 200; iter++)
      {
        PathsD paths = new PathsD();
        int n = rnd.Next(1, 4);
        for (int i = 0; i < n; i++)
          paths.Add(rnd.Next(3) == 0
            ? Clipper.ScalePathD(RandomPath(rnd, rnd.Next(1, 30), 3000), 0.01)
            : Circle(rnd.Next(-50, 50), rnd.Next(-50, 50), rnd.Next(1, 30), rnd.Next(3, 40)));
        JoinType jt = (JoinType) rnd.Next(4);
        EndType et = (EndType) rnd.Next(5);
        double delta = deltas[rnd.Next(deltas.Length)];
        int precision = rnd.Next(0, 5);
        double arcTol = rnd.Next(2) == 0 ? 0 : 0.05;
        // nb: 2.0.4 threw for a 'Joined' path whose points were all equal; that
        // is fixed (see TestInflateJoinedDegeneratePath), so such a case only
        // has to succeed now
        string what = $"#{iter} {jt}/{et} delta {delta} precision {precision}";
        PathsD expected;
        try { expected = Legacy.InflatePaths(paths, delta, jt, et, 2.0, precision, arcTol); }
        catch (ArgumentOutOfRangeException)
        {
          Assert.AreEqual(EndType.Joined, et, what);
          Clipper.InflatePaths(paths, delta, jt, et, 2.0, precision, arcTol);
          legacyThrew++;
          continue;
        }
        Same(expected, Clipper.InflatePaths(paths, delta, jt, et, 2.0, precision, arcTol), what);
      }
      Assert.IsTrue(legacyThrew < 40, $"too few comparable cases ({legacyThrew} of 200 threw)");
    }

    [TestMethod]
    public void TestInflateJoinedDegeneratePath()
    {
      // a 'Joined' path of coincident points is offset as the single point it
      // is (as in the C++ original), i.e. exactly like the same path with an
      // open end type; 2.0.4 threw ArgumentOutOfRangeException here
      foreach (JoinType jt in new[] { JoinType.Square, JoinType.Bevel, JoinType.Round, JoinType.Miter })
        foreach (int n in new[] { 1, 2, 5 })
          foreach (double delta in new[] { -4.0, 0.5, 4.0 })
          {
            Path64 dot = new Path64();
            for (int i = 0; i < n; i++) dot.Add(new Point64(10, 20));
            Paths64 input = new Paths64 { dot };
            Paths64 joined = Clipper.InflatePaths(input, delta, jt, EndType.Joined);
            Paths64 open = Clipper.InflatePaths(input, delta, jt, EndType.Butt);
            Same(open, joined, $"{jt} n={n} delta={delta}");
            if (delta >= 1) Assert.AreEqual(1, joined.Count, $"{jt} n={n} delta={delta}");
          }

      // an empty open path is skipped instead of throwing, for every open end type
      Paths64 square = new Paths64 { new Path64 { new(0, 0), new(100, 0), new(100, 100), new(0, 100) } };
      foreach (EndType et in new[] { EndType.Joined, EndType.Butt, EndType.Square, EndType.Round })
      {
        Paths64 withEmpty = new Paths64 { new Path64(), square[0] };
        Same(Clipper.InflatePaths(square, 5, JoinType.Round, et),
          Clipper.InflatePaths(withEmpty, 5, JoinType.Round, et), et.ToString());
      }
    }

    [TestMethod]
    public void TestStripDuplicatesPaths64()
    {
      // 2.0.4 returned the paths unchanged (the stripped copies were discarded)
      Random rnd = new Random(41);
      foreach (bool closed in new[] { false, true })
        for (int iter = 0; iter < 50; iter++)
        {
          Paths64 paths = new Paths64();
          int n = rnd.Next(0, 70); // past the serial threshold too
          for (int i = 0; i < n; i++)
          {
            Path64 p = new Path64();
            int len = rnd.Next(0, 12);
            for (int j = 0; j < len; j++) p.Add(new Point64(rnd.Next(3), rnd.Next(3)));
            paths.Add(p);
          }
          Paths64 expected = new Paths64(paths.Count);
          foreach (Path64 p in paths) expected.Add(Clipper.StripDuplicates(p, closed));
          Paths64 result = Clipper.StripDuplicates(paths, closed);
          Assert.AreSame(paths, result, "the list is stripped in place");
          Same(expected, result, $"closed={closed} #{iter}");
        }
    }

    [TestMethod]
    public void TestRectClipDMatchesLegacy()
    {
      Random rnd = new Random(5);
      for (int iter = 0; iter < 300; iter++)
      {
        PathsD paths = new PathsD();
        int n = rnd.Next(0, 12);
        for (int i = 0; i < n; i++)
          paths.Add(rnd.Next(2) == 0
            ? Clipper.ScalePathD(RandomPath(rnd, rnd.Next(0, 25), 6000), 0.01)
            : Circle(rnd.Next(-80, 80), rnd.Next(-80, 80), rnd.Next(1, 40), rnd.Next(2, 30)));
        double l = rnd.Next(-60, 20) + 0.005, t = rnd.Next(-60, 20) + 0.0049;
        RectD rect = new RectD(l, t, l + rnd.Next(0, 80), t + rnd.Next(0, 80));
        int precision = rnd.Next(0, 4);
        Same(Legacy.RectClip(rect, paths, precision, false), Clipper.RectClip(rect, paths, precision), $"polygons #{iter}");
        Same(Legacy.RectClip(rect, paths, precision, true), Clipper.RectClipLines(rect, paths, precision), $"lines #{iter}");
      }
    }

    [TestMethod]
    public void TestPointInPolygonDMatchesLegacy()
    {
      Random rnd = new Random(6);
      for (int iter = 0; iter < 300; iter++)
      {
        PathD poly = rnd.Next(2) == 0
          ? Clipper.ScalePathD(RandomPath(rnd, rnd.Next(0, 60), 2000), 0.01)
          : Circle(0, 0, 10, rnd.Next(3, 300));
        int precision = rnd.Next(0, 4);
        PointD[] pts = new PointD[rnd.Next(0, 40)];
        for (int i = 0; i < pts.Length; i++)
          pts[i] = rnd.Next(4) == 0 && poly.Count > 0
            ? poly[rnd.Next(poly.Count)]
            : new PointD(rnd.NextDouble() * 50 - 25, rnd.NextDouble() * 50 - 25);
        PointInPolygonResult[] batch = new PointInPolygonResult[pts.Length];
        Clipper.PointInPolygon(poly, pts, batch, precision);
        PointInPolygonLocatorD locator = new PointInPolygonLocatorD(poly, precision);
        for (int i = 0; i < pts.Length; i++)
        {
          PointInPolygonResult expected = Legacy.PointInPolygon(pts[i], poly, precision);
          Assert.AreEqual(expected, Clipper.PointInPolygon(pts[i], poly, precision), $"single #{iter}/{i}");
          Assert.AreEqual(expected, batch[i], $"batch #{iter}/{i}");
          Assert.AreEqual(expected, locator.Locate(pts[i]), $"locator #{iter}/{i}");
        }
      }
    }

    [TestMethod]
    public void TestLocatorMatchesScanOnWideCoordinates()
    {
      // power of two bucket heights at every scale, including spans near 2^62
      Random rnd = new Random(7);
      foreach (long range in new[] { 1L, 7L, 1000L, 1L << 31, 1L << 45, 1L << 60 })
        for (int iter = 0; iter < 60; iter++)
        {
          Path64 poly = RandomPath(rnd, rnd.Next(3, 200), range);
          PointInPolygonLocator locator = new PointInPolygonLocator(poly);
          for (int k = 0; k < 200; k++)
          {
            Point64 pt = rnd.Next(3) == 0
              ? poly[rnd.Next(poly.Count)]
              : new Point64(rnd.NextInt64(-range - 2, range + 2), rnd.NextInt64(-range - 2, range + 2));
            Assert.AreEqual(Clipper.PointInPolygon(pt, poly), locator.Locate(pt), $"range {range} #{iter}/{k}");
          }
        }
    }

    /// <summary>A user type deriving from PolyTreeD: the engine still builds it
    /// through AddChild(Path64), i.e. the 2.0.4 way.</summary>
    private sealed class DerivedTreeD : PolyTreeD { }

    [TestMethod]
    public void TestPolyTreeDBuiltDirectlyMatchesPath64Route()
    {
      Random rnd = new Random(8);
      for (int iter = 0; iter < 200; iter++)
      {
        PathsD subject = new PathsD(), clip = new PathsD();
        for (int i = rnd.Next(1, 6); i > 0; i--)
          subject.Add(Circle(rnd.Next(-30, 30), rnd.Next(-30, 30), rnd.Next(2, 30), rnd.Next(3, 30)));
        for (int i = rnd.Next(0, 4); i > 0; i--)
          clip.Add(Clipper.ScalePathD(RandomPath(rnd, rnd.Next(3, 20), 3000), 0.01));
        ClipType ct = (ClipType) rnd.Next(1, 5);
        FillRule fr = (FillRule) rnd.Next(4);
        int precision = rnd.Next(0, 5);

        PolyTreeD direct = new PolyTreeD();
        DerivedTreeD legacy = new DerivedTreeD();
        Clipper.BooleanOp(ct, fr, subject, clip, direct, precision);
        ClipperD c = new ClipperD(precision);
        c.AddSubject(subject);
        c.AddClip(clip);
        c.Execute(ct, fr, legacy, new PathsD());
        SameTree(legacy, direct, $"#{iter} {ct}/{fr}");
        Assert.AreEqual(legacy.ToString(), direct.ToString());
      }
    }

    [TestMethod]
    public void TestExecuteWithoutOpenListMatchesWithList()
    {
      Random rnd = new Random(9);
      for (int iter = 0; iter < 200; iter++)
      {
        Paths64 subject = new Paths64 { RandomPath(rnd, rnd.Next(3, 30), 500) };
        Paths64 open = new Paths64 { RandomPath(rnd, rnd.Next(2, 10), 500) };
        Paths64 clip = new Paths64 { RandomPath(rnd, rnd.Next(3, 30), 500) };
        ClipType ct = (ClipType) rnd.Next(1, 5);
        FillRule fr = (FillRule) rnd.Next(4);

        Clipper64 c = new Clipper64();
        Paths64 withList = new Paths64(), openOut = new Paths64(), without = new Paths64 { new Path64() };
        c.AddSubject(subject); c.AddOpenSubject(open); c.AddClip(clip);
        c.Execute(ct, fr, withList, openOut);
        c.Clear();
        c.AddSubject(subject); c.AddOpenSubject(open); c.AddClip(clip);
        c.Execute(ct, fr, without);
        Same(withList, without, $"Paths64 #{iter}");

        PolyTree64 t1 = new PolyTree64(), t2 = new PolyTree64();
        c.Clear();
        c.AddSubject(subject); c.AddOpenSubject(open); c.AddClip(clip);
        c.Execute(ct, fr, t1, new Paths64());
        c.Clear();
        c.AddSubject(subject); c.AddOpenSubject(open); c.AddClip(clip);
        c.Execute(ct, fr, t2);
        Same(Clipper.PolyTreeToPaths64(t1), Clipper.PolyTreeToPaths64(t2), $"PolyTree64 #{iter}");

        PathsD sd = Clipper.ScalePathsD(subject, 0.01), od = Clipper.ScalePathsD(open, 0.01), cd = Clipper.ScalePathsD(clip, 0.01);
        ClipperD d = new ClipperD(3);
        PathsD dWith = new PathsD(), dWithout = new PathsD();
        d.AddSubject(sd); d.AddOpenSubject(od); d.AddClip(cd);
        d.Execute(ct, fr, dWith, new PathsD());
        d.Clear();
        d.AddSubject(sd); d.AddOpenSubject(od); d.AddClip(cd);
        d.Execute(ct, fr, dWithout);
        Same(dWith, dWithout, $"PathsD #{iter}");
      }
    }

    [TestMethod]
    public void TestSharedClipperDAcrossPrecisions()
    {
      // the per thread engine is re-targeted when the precision changes
      PathsD subject = new PathsD { Circle(0, 0, 10.123456789, 50) };
      PathsD clip = new PathsD { Circle(5.5, 0.25, 7.987654321, 37) };
      for (int round = 0; round < 3; round++)
        foreach (int precision in new[] { 2, 5, -1, 8, 0, 2 })
        {
          ClipperD fresh = new ClipperD(precision);
          fresh.AddSubject(subject);
          fresh.AddClip(clip);
          PathsD expected = new PathsD();
          fresh.Execute(ClipType.Xor, FillRule.NonZero, expected);
          Same(expected, Clipper.Xor(subject, clip, FillRule.NonZero, precision), $"precision {precision}");
        }
      // an invalid precision still throws, and the next call still works
      Assert.ThrowsException<Clipper2Exception>(() => Clipper.Union(subject, clip, FillRule.NonZero, 9));
      Assert.AreEqual(1, Clipper.Union(subject, clip, FillRule.NonZero, 3).Count);
    }

    [TestMethod]
    public void TestRepeatedTriangulationsAreIndependent()
    {
      Random rnd = new Random(10);
      Paths64 a = new Paths64 { Clipper.ScalePath64(Circle(0, 0, 1000, 300), 1) };
      Paths64 b = new Paths64 { RandomPath(rnd, 50, 1000) };
      Assert.AreEqual(TriangulateResult.success, Clipper.Triangulate(a, out Paths64 first));
      Clipper.Triangulate(b, out _, false);          // whatever the outcome, it must not leak into the next call
      Clipper.Triangulate(new Paths64(), out _);     // noPolygons
      Assert.AreEqual(TriangulateResult.success, Clipper.Triangulate(a, out Paths64 again));
      Same(first, again, "triangulation after other calls");
    }

    [TestMethod]
    public void TestPolyTreeLeavesAndEnumeration()
    {
      PolyTree64 tree = new PolyTree64();
      Clipper.BooleanOp(ClipType.Union, FillRule.NonZero,
        new Paths64 { Clipper.MakePath(new[] { 0, 0, 100, 0, 100, 100, 0, 100 }),
                      Clipper.MakePath(new[] { 10, 10, 10, 90, 90, 90, 90, 10 }) }, null, tree);
      Assert.AreEqual(1, tree.Count);
      PolyPath64 outer = tree[0];
      Assert.AreEqual(1, outer.Count);
      PolyPath64 hole = outer[0];
      Assert.AreEqual(0, hole.Count);                      // a leaf
      Assert.AreEqual(0, hole.Cast<object>().Count());     // enumerating a leaf
      Assert.ThrowsException<InvalidOperationException>(() => hole[0]);
      Assert.ThrowsException<InvalidOperationException>(() => hole.Child(0));
      Assert.AreEqual(-6400.0, hole.Area());
      Assert.AreEqual(10000.0 - 6400.0, tree.Area());
      hole.Clear();                                        // clearing a leaf
      Assert.AreEqual("Polytree with 1 polygon.\n  +- polygon (0) contains 1 hole.\n\n", tree.ToString());
    }
  }

  /// <summary>Verbatim copies of the 2.0.4 implementations (serial forms).</summary>
  internal static class Legacy
  {
    private static int GetNext(int current, int high, bool[] flags)
    {
      ++current;
      while (current <= high && flags[current]) ++current;
      if (current <= high) return current;
      current = 0;
      while (flags[current]) ++current;
      return current;
    }

    private static int GetPrior(int current, int high, bool[] flags)
    {
      if (current == 0) current = high;
      else --current;
      while (current > 0 && flags[current]) --current;
      if (!flags[current]) return current;
      current = high;
      while (flags[current]) --current;
      return current;
    }

    private static List<T> Simplify<T>(List<T> path, double epsilon, bool isClosedPath,
      Func<T, T, T, double> dist)
    {
      int len = path.Count, high = len - 1;
      double epsSqr = epsilon * epsilon;
      if (len < 4) return path;
      bool[] flags = new bool[len];
      double[] dsq = new double[len];
      int curr = 0;
      if (isClosedPath)
      {
        dsq[0] = dist(path[0], path[high], path[1]);
        dsq[high] = dist(path[high], path[0], path[high - 1]);
      }
      else
      {
        dsq[0] = double.MaxValue;
        dsq[high] = double.MaxValue;
      }
      for (int i = 1; i < high; ++i)
        dsq[i] = dist(path[i], path[i - 1], path[i + 1]);
      for (; ; )
      {
        if (dsq[curr] > epsSqr)
        {
          int start = curr;
          do
          {
            curr = GetNext(curr, high, flags);
          } while (curr != start && dsq[curr] > epsSqr);
          if (curr == start) break;
        }
        int prev = GetPrior(curr, high, flags);
        int next = GetNext(curr, high, flags);
        if (next == prev) break;
        int prior2;
        if (dsq[next] < dsq[curr])
        {
          prior2 = prev;
          prev = curr;
          curr = next;
          next = GetNext(next, high, flags);
        }
        else
          prior2 = GetPrior(prev, high, flags);
        flags[curr] = true;
        curr = next;
        next = GetNext(next, high, flags);
        if (isClosedPath || ((curr != high) && (curr != 0)))
          dsq[curr] = dist(path[curr], path[prev], path[next]);
        if (isClosedPath || ((prev != 0) && (prev != high)))
          dsq[prev] = dist(path[prev], path[prior2], path[curr]);
      }
      List<T> result = new List<T>(len);
      for (int i = 0; i < len; i++)
        if (!flags[i]) result.Add(path[i]);
      return result;
    }

    public static Path64 SimplifyPath(Path64 path, double epsilon, bool isClosedPath)
    {
      List<Point64> r = Simplify(path, epsilon, isClosedPath, Clipper.PerpendicDistFromLineSqrd);
      return ReferenceEquals(r, path) ? path : new Path64(r);
    }

    public static PathD SimplifyPath(PathD path, double epsilon, bool isClosedPath)
    {
      List<PointD> r = Simplify(path, epsilon, isClosedPath, Clipper.PerpendicDistFromLineSqrd);
      return ReferenceEquals(r, path) ? path : new PathD(r);
    }

    public static Path64 StripDuplicates(Path64 path, bool isClosedPath)
    {
      int cnt = path.Count;
      Path64 result = new Path64(cnt);
      if (cnt == 0) return result;
      Point64 lastPt = path[0];
      result.Add(lastPt);
      for (int i = 1; i < cnt; i++)
        if (lastPt != path[i])
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      if (isClosedPath && lastPt == result[0])
        result.RemoveAt(result.Count - 1);
      return result;
    }

    private static bool PointsNearEqual(PointD pt1, PointD pt2, double distanceSqrd) =>
      (pt1.x - pt2.x) * (pt1.x - pt2.x) + (pt1.y - pt2.y) * (pt1.y - pt2.y) < distanceSqrd;

    public static PathD StripNearDuplicates(PathD path, double minEdgeLenSqrd, bool isClosedPath)
    {
      int cnt = path.Count;
      PathD result = new PathD(cnt);
      if (cnt == 0) return result;
      PointD lastPt = path[0];
      result.Add(lastPt);
      for (int i = 1; i < cnt; i++)
        if (!PointsNearEqual(lastPt, path[i], minEdgeLenSqrd))
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      if (isClosedPath && PointsNearEqual(lastPt, result[0], minEdgeLenSqrd))
        result.RemoveAt(result.Count - 1);
      return result;
    }

    private static bool NearEqual(Point64 a, Point64 b, double d) =>
      Clipper.Sqr(a.X - b.X) + Clipper.Sqr(a.Y - b.Y) < d;

    public static Path64 StripNearEqual(Path64 path, double maxDistSqrd, bool isClosedPath)
    {
      if (path.Count == 0) return new Path64();
      Path64 result = new Path64(path.Count);
      Point64 firstPt = path[0], lastPt = firstPt;
      result.Add(firstPt);
      for (int i = 1; i < path.Count; i++)
        if (!NearEqual(path[i], lastPt, maxDistSqrd))
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      if (!isClosedPath) return result;
      while (result.Count > 1 && NearEqual(result[^1], firstPt, maxDistSqrd))
        result.RemoveAt(result.Count - 1);
      return result;
    }

    public static PathD StripNearEqual(PathD path, double maxDistSqrd, bool isClosedPath)
    {
      if (path.Count == 0) return new PathD();
      PathD result = new PathD(path.Count);
      PointD firstPt = path[0], lastPt = firstPt;
      result.Add(firstPt);
      for (int i = 1; i < path.Count; i++)
        if (!PointsNearEqual(path[i], lastPt, maxDistSqrd))
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      if (!isClosedPath) return result;
      while (result.Count > 1 && PointsNearEqual(result[^1], firstPt, maxDistSqrd))
        result.RemoveAt(result.Count - 1);
      return result;
    }

    private static bool IsCollinear(Point64 pt1, Point64 sharedPt, Point64 pt2)
    {
      Int128 a = (Int128) (sharedPt.X - pt1.X) * (pt2.Y - sharedPt.Y);
      Int128 b = (Int128) (sharedPt.Y - pt1.Y) * (pt2.X - sharedPt.X);
      return a == b;
    }

    public static Path64 TrimCollinear(Path64 p, bool isOpenPath)
    {
      int len = p.Count;
      if (len < 3)
      {
        if (!isOpenPath || len < 2 || p[0] == p[1]) return new Path64();
        else return p;
      }
      Path64 dst = new Path64(len);
      int srcI = 0, prevI, stop = len - 1;
      if (!isOpenPath)
      {
        while (srcI != stop && IsCollinear(p[stop], p[srcI], p[srcI + 1]))
          ++srcI;
        while (srcI != stop && IsCollinear(p[stop - 1], p[stop], p[srcI]))
          --stop;
        if (srcI == stop) return new Path64();
      }
      prevI = srcI++;
      dst.Add(p[prevI]);
      for (; srcI != stop; ++srcI)
      {
        if (!IsCollinear(p[prevI], p[srcI], p[srcI + 1]))
        {
          prevI = srcI;
          dst.Add(p[prevI]);
        }
      }
      if (isOpenPath)
        dst.Add(p[srcI]);
      else if (!IsCollinear(p[prevI], p[stop], dst[0]))
        dst.Add(p[stop]);
      else
      {
        while (dst.Count > 2 &&
          IsCollinear(dst[dst.Count - 1], dst[dst.Count - 2], dst[0]))
          dst.RemoveAt(dst.Count - 1);
        if (dst.Count < 3) return new Path64();
      }
      return dst;
    }

    public static Paths64 Minkowski(Path64 pattern, Path64 path, bool isSum, bool isClosed)
    {
      int delta = isClosed ? 0 : 1;
      int patLen = pattern.Count, pathLen = path.Count;
      Paths64 quads = new Paths64();
      if (patLen > 0 && pathLen > 0)
      {
        Paths64 tmp = new Paths64(pathLen);
        foreach (Point64 p in path)
        {
          Path64 path2 = new Path64(patLen);
          foreach (Point64 q in pattern) path2.Add(isSum ? p + q : p - q);
          tmp.Add(path2);
        }
        int g = isClosed ? pathLen - 1 : 0;
        int h = patLen - 1;
        for (int i = delta; i < pathLen; ++i)
        {
          for (int j = 0; j < patLen; j++)
          {
            Path64 quad = new Path64(4) { tmp[g][h], tmp[i][h], tmp[i][j], tmp[g][j] };
            if (!Clipper.IsPositive(quad)) quad.Reverse();
            quads.Add(quad);
            h = j;
          }
          g = i;
        }
      }
      return Clipper.Union(quads, FillRule.NonZero);
    }

    public static PathsD Minkowski(PathD pattern, PathD path, bool isSum, bool isClosed)
    {
      double scale = Math.Pow(10, 2);
      Paths64 tmp = Minkowski(Clipper.ScalePath64(pattern, scale),
        Clipper.ScalePath64(path, scale), isSum, isClosed);
      return Clipper.ScalePathsD(tmp, 1 / scale);
    }

    public static PathsD InflatePaths(PathsD paths, double delta, JoinType joinType,
      EndType endType, double miterLimit, int precision, double arcTolerance)
    {
      if (delta == 0.0) return paths;
      double scale = Math.Pow(10, precision);
      Paths64 tmp = Clipper.ScalePaths64(paths, scale);
      ClipperOffset co = new ClipperOffset(miterLimit, scale * arcTolerance);
      co.AddPaths(tmp, joinType, endType);
      co.Execute(delta * scale, tmp);
      return Clipper.ScalePathsD(tmp, 1 / scale);
    }

    public static PathsD RectClip(RectD rect, PathsD paths, int precision, bool lines)
    {
      if (rect.IsEmpty() || paths.Count == 0) return new PathsD();
      double scale = Math.Pow(10, precision);
      Rect64 r = Clipper.ScaleRect(rect, scale);
      Paths64 tmpPath = Clipper.ScalePaths64(paths, scale);
      tmpPath = lines ? new RectClipLines64(r).Execute(tmpPath) : new RectClip64(r).Execute(tmpPath);
      return Clipper.ScalePathsD(tmpPath, 1 / scale);
    }

    public static PointInPolygonResult PointInPolygon(PointD pt, PathD polygon, int precision)
    {
      double scale = Math.Pow(10, precision);
      Point64 p = new Point64(pt, scale);
      Path64 path = Clipper.ScalePath64(polygon, scale);
      return Clipper.PointInPolygon(p, path);
    }
  }
}
