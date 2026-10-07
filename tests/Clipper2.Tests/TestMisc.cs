using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clipper2.WebDemo.Demo;
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
    public void TestWebDemoPointInPolygonFillRules()
    {
      Assert.AreEqual("below timer resolution", OpRunner.FormatProbeSpeedup(10, 0));
      Assert.AreEqual("below timer resolution", OpRunner.FormatProbeSpeedup(0, 10));
      Assert.AreEqual("below timer resolution", OpRunner.FormatProbeSpeedup(0, 0));
      Assert.AreEqual("2.0x scan / locator", OpRunner.FormatProbeSpeedup(20, 10));
      Assert.AreEqual("0.5x scan / locator", OpRunner.FormatProbeSpeedup(10, 20));
      Paths64 subject = new Paths64 {
        Shapes.Rect(5, 5, 250, 250), Shapes.Rect(55, 55, 100, 100),
        Shapes.Rect(505, 5, 150, 150) };
      foreach (bool hole in new[] { false, true })
      {
        if (hole) subject[1].Reverse();
        foreach (FillRule fillRule in Enum.GetValues<FillRule>())
        {
          OpOutcome result = OpRunner.Run(new OpRequest { Op = OpKind.PointInPolygon,
            Subject = subject, FillRule = fillRule, Repetitions = 1 });
          bool Inside(long x, long y)
          {
            int index = result.Probes!.FindIndex(probe => probe.X == x && probe.Y == y);
            Assert.IsTrue(index >= 0);
            return result.Probes[index].Inside;
          }
          bool filled = fillRule != FillRule.Negative;
          Assert.AreEqual(filled, Inside(30, 30), $"First contour: {fillRule}, hole={hole}");
          Assert.AreEqual(filled, Inside(530, 30), $"Last contour: {fillRule}, hole={hole}");
          Assert.AreEqual(filled, Inside(5, 80), $"Boundary: {fillRule}, hole={hole}");
          Assert.AreEqual(filled && !hole && fillRule != FillRule.EvenOdd, Inside(80, 80));
          Assert.IsFalse(Inside(905, 605));
        }
      }
    }

    [TestMethod]
    public void TestWebDemoProbeGridFitsCanvas()
    {
      (long[] xs, long[] ys) = Shapes.ProbeGrid();
      Assert.AreEqual(xs.Length, ys.Length);
      Assert.IsTrue(xs.Length > 0);
      for (int index = 0; index < xs.Length; index++)
      {
        Assert.IsTrue(xs[index] >= 0 && xs[index] <= Shapes.WorldWidth);
        Assert.IsTrue(ys[index] >= 0 && ys[index] <= Shapes.WorldHeight);
      }
    }

    [TestMethod]
    public void TestBooleanOpDoubleMatchesFreshEngine()
    {
      PathsD subject = new PathsD {
        new RectD(-12.125, -3.625, 18.375, 20.875).AsPath(),
        new RectD(22.125, -3.625, 38.375, 20.875).AsPath() };
      PathsD clip = new PathsD { new RectD(-5.125, 1.625, 9.375, 13.875).AsPath() };
      List<int> precisions = new List<int> { 2, 2 };
      for (int precision = -8; precision <= 8; precision++)
      {
        precisions.Add(precision);
        precisions.Add(precision);
      }
      foreach (int precision in precisions)
      foreach (ClipType clipType in Enum.GetValues<ClipType>())
      foreach (FillRule fillRule in Enum.GetValues<FillRule>())
      foreach (PathsD? clips in new PathsD?[] { clip, null, new PathsD() })
      {
        ClipperD engine = new ClipperD(precision);
        engine.AddSubject(subject);
        if (clips != null) engine.AddClip(clips);
        PathsD expected = new PathsD();
        engine.Execute(clipType, fillRule, expected);
        PathsD actual = Clipper.BooleanOp(clipType, fillRule, subject, clips, precision);
        Assert.AreEqual(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
          CollectionAssert.AreEqual(expected[index], actual[index]);

        PolyTreeD expectedTree = new PolyTreeD();
        PolyTreeD actualTree = new PolyTreeD();
        engine.Execute(clipType, fillRule, expectedTree);
        Clipper.BooleanOp(clipType, fillRule, subject, clips, actualTree, precision);
        expected = Clipper.PolyTreeToPathsD(expectedTree);
        actual = Clipper.PolyTreeToPathsD(actualTree);
        Assert.AreEqual(expectedTree.Count, actualTree.Count);
        Assert.AreEqual(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
          CollectionAssert.AreEqual(expected[index], actual[index]);
      }
    }

    [TestMethod]
    public void TestClipperDDirectInputMatchesScaled64()
    {
      PathsD subject = new PathsD {
        new RectD(-12.125, -3.625, 18.375, 20.875).AsPath(),
        new PathD { new PointD(-8.125, -8.125), new PointD(14.375, 16.875),
          new PointD(-8.125, 16.875), new PointD(14.375, -8.125), new PointD(14.375, -8.125) },
        new PathD(), new PathD { new PointD(0.125, 0.125) } };
      PathsD clip = new PathsD { new RectD(-5.125, 1.625, 9.375, 13.875).AsPath() };
      PathsD open = new PathsD { new PathD { new PointD(-25.125, 5.625),
        new PointD(0.125, 5.625), new PointD(25.375, 5.625) } };
      for (int precision = -8; precision <= 8; precision++)
      foreach (ClipType clipType in Enum.GetValues<ClipType>())
      foreach (FillRule fillRule in Enum.GetValues<FillRule>())
      {
        double scale = Math.Pow(2, Math.ILogB(Math.Pow(10, precision)) + 1);
        int errorCode = 0;
        ClipperD expected = new ClipperD(precision) { PreserveCollinear = (precision & 1) == 0,
          ReverseSolution = (precision & 2) == 0 };
        expected.AddPaths(InternalClipper.ScalePaths(subject, scale, ref errorCode), PathType.Subject, false);
        expected.AddPaths(InternalClipper.ScalePaths(clip, scale, ref errorCode), PathType.Clip, false);
        expected.AddPaths(InternalClipper.ScalePaths(open, scale, ref errorCode), PathType.Subject, true);
        ClipperD actual = new ClipperD(precision) { PreserveCollinear = expected.PreserveCollinear,
          ReverseSolution = expected.ReverseSolution };
        actual.AddSubject(subject);
        actual.AddClip(clip);
        actual.AddOpenSubject(open[0]);
        PathsD closedExpected = new PathsD(), openExpected = new PathsD();
        PathsD closedD = new PathsD(), openD = new PathsD();
        Assert.AreEqual(expected.Execute(clipType, fillRule, closedExpected, openExpected),
          actual.Execute(clipType, fillRule, closedD, openD));
        void EqualPaths(PathsD scaled, PathsD doubles)
        {
          Assert.AreEqual(scaled.Count, doubles.Count);
          for (int index = 0; index < scaled.Count; index++)
            CollectionAssert.AreEqual(scaled[index], doubles[index]);
        }
        EqualPaths(closedExpected, closedD);
        EqualPaths(openExpected, openD);
        PolyTreeD treeExpected = new PolyTreeD();
        PolyTreeD treeD = new PolyTreeD();
        Assert.AreEqual(expected.Execute(clipType, fillRule, treeExpected, openExpected),
          actual.Execute(clipType, fillRule, treeD, openD));
        void EqualTree(PolyPathD expectedNode, PolyPathD actualNode)
        {
          Assert.AreEqual(expectedNode.Count, actualNode.Count);
          Assert.AreEqual(expectedNode.Polygon == null, actualNode.Polygon == null);
          if (expectedNode.Polygon != null)
            EqualPaths(new PathsD { expectedNode.Polygon }, new PathsD { actualNode.Polygon! });
          for (int index = 0; index < expectedNode.Count; index++)
            EqualTree(expectedNode[index], actualNode[index]);
        }
        EqualTree(treeExpected, treeD);
        EqualPaths(openExpected, openD);
      }
    }

    [TestMethod]
    public void TestClipperDDirectInputAllocationAndRange()
    {
      PathsD subject = new PathsD { Clipper.Ellipse(new PointD(0, 0), 100, 100, 2000) };
      ClipperD direct = new ClipperD(3);
      Clipper64 legacy = new Clipper64();
      double scale = Math.Pow(2, Math.ILogB(Math.Pow(10, 3)) + 1);
      Action oldInput = () => {
        int errorCode = 0;
        legacy.AddSubject(InternalClipper.ScalePaths(subject, scale, ref errorCode));
        legacy.Clear();
      };
      Action newInput = () => { direct.AddSubject(subject); direct.Clear(); };
      for (int repeat = 0; repeat < 10; repeat++) { oldInput(); newInput(); }
      long Allocated(Action run)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < 50; repeat++) run();
        return GC.GetAllocatedBytesForCurrentThread() - before;
      }
      long oldBytes = Allocated(oldInput), newBytes = Allocated(newInput);
      Assert.IsTrue(newBytes * 10 < oldBytes,
        $"Expected direct vertex input: old {oldBytes}, direct {newBytes}");
      PathD valid = new RectD(0, 0, 10, 10).AsPath();
      PathD invalid = new PathD { new PointD(-1e30, -1e30), new PointD(1e30, -1e30), new PointD(0, 1e30) };
      direct.AddSubject(valid);
      Assert.ThrowsException<Clipper2Exception>(() => direct.AddSubject(
        new PathsD { new RectD(20, 0, 30, 10).AsPath(), invalid }));
      Assert.ThrowsException<Clipper2Exception>(() => direct.AddSubject(invalid));
      Assert.AreEqual(0, direct.ErrorCode());
      PathsD result = new PathsD();
      Assert.IsTrue(direct.Execute(ClipType.Union, FillRule.NonZero, result));
      Assert.AreEqual(1, result.Count);
      Assert.AreEqual(100.0, Clipper.Area(result));
      direct.Clear();
      direct.AddSubject(valid);
      Assert.IsTrue(direct.Execute(ClipType.Union, FillRule.NonZero, result));
      Assert.AreEqual(100.0, Clipper.Area(result));
    }

    [TestMethod]
    public void TestClipperDSharedEngineLifecycle()
    {
      ClipperD engine = ClipperD.RentShared(3);
      ClipperD nested = ClipperD.RentShared(3);
      try
      {
        Assert.AreNotSame(engine, nested);
        engine.PreserveCollinear = false;
        engine.ReverseSolution = true;
        engine.AddSubject(new RectD(0, 0, 10, 10).AsPath());
        engine.AddOpenSubject(new PathD { new PointD(-10, 5), new PointD(20, 5) });
      }
      finally
      {
        ClipperD.ReturnShared(nested);
        ClipperD.ReturnShared(engine);
      }
      ClipperD reused = ClipperD.RentShared(3);
      try
      {
        Assert.AreSame(engine, reused);
        Assert.IsTrue(reused.PreserveCollinear);
        Assert.IsFalse(reused.ReverseSolution);
        Assert.AreEqual(0, reused.ErrorCode());
        PathsD closed = new PathsD(), open = new PathsD();
        Assert.IsTrue(reused.Execute(ClipType.Union, FillRule.NonZero, closed, open));
        Assert.AreEqual(0, closed.Count);
        Assert.AreEqual(0, open.Count);
        PathD large = new PathD((1 << 17) + 1);
        for (int index = 0; index <= 1 << 17; index++)
          large.Add(new PointD(index, index & 1));
        reused.AddSubject(large);
      }
      finally
      {
        ClipperD.ReturnShared(reused);
      }
      ClipperD bounded = ClipperD.RentShared(3);
      try { Assert.AreNotSame(reused, bounded); }
      finally { ClipperD.ReturnShared(bounded); }
    }

    [TestMethod]
    public void TestBooleanOpDoubleRecoversAfterException()
    {
      PathsD subject = new PathsD { new RectD(0.125, 0.125, 10.125, 10.125).AsPath() };
      PathsD invalid = new PathsD { new PathD { new PointD(-1e30, -1e30),
        new PointD(1e30, -1e30), new PointD(0, 1e30) } };
      PolyTreeD tree = new PolyTreeD();
      foreach (int precision in new[] { -9, 9, int.MinValue, int.MaxValue })
      {
        Clipper.Union(subject, FillRule.NonZero);
        Assert.ThrowsException<Clipper2Exception>(() =>
          Clipper.BooleanOp(ClipType.Union, FillRule.NonZero, subject, null, precision));
        Assert.ThrowsException<Clipper2Exception>(() =>
          Clipper.BooleanOp(ClipType.Union, FillRule.NonZero, subject, null, tree, precision));
      }
      for (int repeat = 0; repeat < 2; repeat++)
      {
        Assert.ThrowsException<Clipper2Exception>(() =>
          Clipper.BooleanOp(ClipType.Union, FillRule.NonZero, subject, invalid));
        Assert.ThrowsException<Clipper2Exception>(() =>
          Clipper.BooleanOp(ClipType.Union, FillRule.NonZero, subject, invalid, tree));
        PathsD result = Clipper.Union(subject, FillRule.NonZero);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(100.0, Clipper.Area(result));
        Assert.AreEqual(0, Clipper.Union(new PathsD(), FillRule.NonZero).Count);
      }
    }

    [TestMethod]
    public void TestBooleanOpDoubleConcurrentCalls()
    {
      Parallel.For(0, 128, iteration =>
      {
        int precision = iteration % 17 - 8;
        PathsD subject = new PathsD {
          new RectD(iteration + 0.125, -3.625, iteration + 18.375, 20.875).AsPath() };
        ClipperD fresh = new ClipperD(precision);
        fresh.AddSubject(subject);
        PathsD expected = new PathsD();
        fresh.Execute(ClipType.Union, FillRule.NonZero, expected);
        for (int repeat = 0; repeat < 4; repeat++)
        {
          PathsD actual = Clipper.BooleanOp(ClipType.Union, FillRule.NonZero, subject, null, precision);
          Assert.AreEqual(expected.Count, actual.Count);
          for (int index = 0; index < expected.Count; index++)
            CollectionAssert.AreEqual(expected[index], actual[index]);
        }
      });
    }

    [TestMethod]
    public void TestBooleanOpDoubleReusesBuffers()
    {
      PathsD subject = new PathsD { new RectD(0, 0, 10, 10).AsPath() };
      Func<PathsD> fresh = () =>
      {
        ClipperD engine = new ClipperD();
        engine.AddSubject(subject);
        PathsD result = new PathsD();
        engine.Execute(ClipType.Union, FillRule.NonZero, result);
        return result;
      };
      Func<PathsD> shared = () => Clipper.BooleanOp(ClipType.Union, FillRule.NonZero, subject, null);
      for (int repeat = 0; repeat < 10; repeat++) { fresh(); shared(); }
      long Allocated(Func<PathsD> run)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < 200; repeat++) run();
        return GC.GetAllocatedBytesForCurrentThread() - before;
      }
      long freshBytes = Allocated(fresh), sharedBytes = Allocated(shared);
      Assert.IsTrue(sharedBytes * 2 < freshBytes,
        $"Expected buffer reuse: fresh {freshBytes} bytes, shared {sharedBytes} bytes");
    }

    [TestMethod]
    public void TestBooleanOpLegacyTreeOrder()
    {
      Paths64 subject = new Paths64 { new Rect64(0, 0, 100, 100).AsPath() };
      Paths64 clip = new Paths64 { new Rect64(25, 25, 75, 75).AsPath() };
      foreach (ClipType clipType in new[] { ClipType.Intersection, ClipType.Union,
        ClipType.Difference, ClipType.Xor })
      foreach (FillRule fillRule in Enum.GetValues<FillRule>())
      foreach (Paths64? clips in new Paths64?[] { clip, null, new Paths64() })
      {
        PolyTree64 expected = new PolyTree64();
        PolyTree64 actual = new PolyTree64();
        Clipper.BooleanOp(clipType, fillRule, subject, clips, expected);
        Clipper.BooleanOp(clipType, subject, clips, actual, fillRule);
        Paths64 expectedPaths = Clipper.PolyTreeToPaths64(expected);
        Paths64 actualPaths = Clipper.PolyTreeToPaths64(actual);
        Assert.AreEqual(expected.Count, actual.Count);
        Assert.AreEqual(expectedPaths.Count, actualPaths.Count);
        for (int index = 0; index < expectedPaths.Count; index++)
          CollectionAssert.AreEqual(expectedPaths[index], actualPaths[index]);
      }
    }

    [TestMethod]
    public void TestBooleanOpLegacyTreeOrderDouble()
    {
      PathsD subject = new PathsD { new RectD(0.125, 0.125, 10.125, 10.125).AsPath() };
      PathsD clip = new PathsD { new RectD(2.625, 2.625, 7.625, 7.625).AsPath() };
      foreach (int precision in new[] { 2, 3 })
      foreach (ClipType clipType in new[] { ClipType.Intersection, ClipType.Union,
        ClipType.Difference, ClipType.Xor })
      foreach (FillRule fillRule in Enum.GetValues<FillRule>())
      foreach (PathsD? clips in new PathsD?[] { clip, null, new PathsD() })
      {
        PolyTreeD expected = new PolyTreeD();
        PolyTreeD actual = new PolyTreeD();
        Clipper.BooleanOp(clipType, fillRule, subject, clips, expected, precision);
        if (precision == 2)
          Clipper.BooleanOp(clipType, subject, clips, actual, fillRule);
        else
          Clipper.BooleanOp(clipType, subject, clips, actual, fillRule, precision);
        PathsD expectedPaths = Clipper.PolyTreeToPathsD(expected);
        PathsD actualPaths = Clipper.PolyTreeToPathsD(actual);
        Assert.AreEqual(expected.Count, actual.Count);
        Assert.AreEqual(expectedPaths.Count, actualPaths.Count);
        for (int index = 0; index < expectedPaths.Count; index++)
          CollectionAssert.AreEqual(expectedPaths[index], actualPaths[index]);
      }
    }

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
    public void TestPolyTreeDScale()
    {
      // a ClipperD polytree must hold the same coordinates as its PathsD output
      // (a port defect scaled the tree's polygons by the engine scale squared)
      PathsD subj = new PathsD { Clipper.MakePath(new double[] { 0, 0, 10.5, 0, 10.5, 7.25, 0, 7.25 }) };
      PathsD clip = new PathsD { Clipper.MakePath(new double[] { 2, 2, 5, 2, 5, 5, 2, 5 }) };
      foreach (int precision in new[] { 0, 2, 5 })
      {
        ClipperD c = new ClipperD(precision);
        c.AddSubject(subj);
        c.AddClip(clip);
        PathsD paths = new PathsD();
        c.Execute(ClipType.Difference, FillRule.NonZero, paths);
        PolyTreeD tree = new PolyTreeD();
        c.Execute(ClipType.Difference, FillRule.NonZero, tree);
        Assert.AreEqual(Clipper.Area(paths), tree.Area(), 1e-9, "precision " + precision);
        // (precision 0 works on a grid of 0.5, which moves 7.25 to 7.5)
        if (precision > 0) Assert.AreEqual(67.125, tree.Area(), 1e-9);
        Assert.AreEqual(1, tree.Count);
        Assert.AreEqual(1, tree[0].Count); // the hole
        RectD b = Clipper.GetBounds(tree[0].Polygon!);
        Assert.AreEqual(10.5, b.right, 1e-9);
      }
    }

    [TestMethod]
    public void TestPointInPolygonSmallBatchD()
    {
      PathD polygon = new RectD(-10.125, -5.625, 20.375, 15.875).AsPath();
      PointD[] points = new PointD[16];
      for (int index = 0; index < points.Length; index++)
        points[index] = index < polygon.Count ? polygon[index] : new PointD(index - 6.125, index - 8.625);
      foreach (PathD path in new[] { polygon, new PathD(), new PathD { points[0] } })
      for (int precision = -8; precision <= 8; precision++)
      foreach (int count in new[] { 0, 1, 2, 15, 16 })
      {
        PointInPolygonResult[] results = new PointInPolygonResult[18];
        Array.Fill(results, PointInPolygonResult.IsOn);
        Clipper.PointInPolygon(path, points.AsSpan(0, count), results, precision);
        for (int index = 0; index < count; index++)
          Assert.AreEqual(Clipper.PointInPolygon(points[index], path, precision), results[index]);
        for (int index = count; index < results.Length; index++)
          Assert.AreEqual(PointInPolygonResult.IsOn, results[index]);
      }
      Clipper.PointInPolygon(polygon, ReadOnlySpan<PointD>.Empty, Span<PointInPolygonResult>.Empty, 9);
      Assert.ThrowsException<Clipper2Exception>(() => Clipper.PointInPolygon(polygon, points.AsSpan(0, 1), new PointInPolygonResult[1], 9));
      Assert.ThrowsException<ArgumentException>(() => Clipper.PointInPolygon(polygon, points.AsSpan(0, 2), new PointInPolygonResult[1]));

      PathD dense = Clipper.Ellipse(new PointD(0, 0), 100, 100, 2000);
      PointInPolygonResult[] batch = new PointInPolygonResult[15];
      Action scalar = () => {
        for (int index = 0; index < batch.Length; index++)
          batch[index] = Clipper.PointInPolygon(points[index], dense);
      };
      Action bulk = () => Clipper.PointInPolygon(dense, points.AsSpan(0, batch.Length), batch);
      for (int repeat = 0; repeat < 10; repeat++) { scalar(); bulk(); }
      long Allocated(Action run)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < 50; repeat++) run();
        return GC.GetAllocatedBytesForCurrentThread() - before;
      }
      long scalarBytes = Allocated(scalar), bulkBytes = Allocated(bulk);
      Assert.IsTrue(bulkBytes * 10 < scalarBytes,
        $"Expected one scaling per batch: scalar {scalarBytes}, bulk {bulkBytes}");
    }

    [TestMethod]
    public void TestPointInPolygonLocatorD()
    {
      // the PathD locator and batch query answer exactly like the single call
      Random rnd = new Random(99);
      for (int round = 0; round < 100; round++)
      {
        int n = rnd.Next(3, 40);
        PathD poly = new PathD(n);
        for (int i = 0; i < n; i++)
        {
          double x = Math.Round(rnd.NextDouble() * 20 - 10, 3), y = Math.Round(rnd.NextDouble() * 20 - 10, 3);
          if (i > 0 && rnd.Next(4) == 0) y = poly[i - 1].y;
          poly.Add(new PointD(x, y));
        }
        int precision = round % 4;
        PointInPolygonLocatorD locator = new PointInPolygonLocatorD(poly, precision);
        List<PointD> probes = new List<PointD>(poly);
        for (int i = 0; i < 2000; i++)
          probes.Add(new PointD(Math.Round(rnd.NextDouble() * 24 - 12, 4), Math.Round(rnd.NextDouble() * 24 - 12, 4)));
        PointD[] pts = probes.ToArray();
        PointInPolygonResult[] batch = new PointInPolygonResult[pts.Length];
        Clipper.PointInPolygon(poly, pts, batch, precision);
        PointInPolygonResult[] batch2 = new PointInPolygonResult[pts.Length];
        locator.Locate(pts, batch2);
        for (int i = 0; i < pts.Length; i++)
        {
          PointInPolygonResult expected = Clipper.PointInPolygon(pts[i], poly, precision);
          Assert.AreEqual(expected, locator.Locate(pts[i]), $"round {round} probe {pts[i]}");
          Assert.AreEqual(expected, batch[i], $"round {round} batch probe {pts[i]}");
          Assert.AreEqual(expected, batch2[i], $"round {round} locator batch probe {pts[i]}");
          Assert.AreEqual(expected != PointInPolygonResult.IsOutside, locator.Contains(pts[i]));
        }
      }
      PointInPolygonLocator square = new PointInPolygonLocator(
        Clipper.MakePath(new long[] { 0, 0, 10, 0, 10, 10, 0, 10 }));
      Assert.AreEqual(new Rect64(0, 0, 10, 10), square.Bounds);
      Assert.AreEqual(4, square.Count);
      Assert.IsTrue(square.Contains(new Point64(5, 5)));
      Assert.IsTrue(square.Contains(new Point64(10, 5)));   // on the boundary
      Assert.IsFalse(square.Contains(new Point64(11, 5)));
      Assert.IsFalse(new PointInPolygonLocator(new Path64()).Bounds.IsValid());
      RectD bd = new PointInPolygonLocatorD(Clipper.MakePath(new double[] { 0, 0, 1.5, 0, 1.5, 2.25 }), 2).Bounds;
      Assert.AreEqual(1.5, bd.right, 1e-12);
      Assert.AreEqual(2.25, bd.bottom, 1e-12);
    }

    private static (Paths64 subj, Paths64 clip) ClusteredInput(int seed, int clusters)
    {
      Random rnd = new Random(seed);
      Paths64 subj = new Paths64(), clip = new Paths64();
      for (int k = 0; k < clusters; k++)
      {
        long ox = (k % 8) * 100_000, oy = (k / 8) * 100_000;
        // a ring (outer + hole) per cluster, plus overlapping ellipses
        subj.Add(Clipper.Ellipse(new Point64(ox, oy), 9000, 9000, 120));
        Path64 hole = Clipper.Ellipse(new Point64(ox, oy), 4000, 4000, 80);
        hole.Reverse();
        subj.Add(hole);
        subj.Add(Clipper.Ellipse(new Point64(ox + rnd.Next(-5000, 5000), oy + rnd.Next(-5000, 5000)),
          rnd.Next(2000, 6000), rnd.Next(2000, 6000), 90));
        clip.Add(Clipper.Ellipse(new Point64(ox + rnd.Next(-3000, 3000), oy + rnd.Next(-3000, 3000)),
          rnd.Next(2000, 7000), rnd.Next(2000, 7000), 90));
      }
      return (subj, clip);
    }

    private static int CountNodes(PolyPathBase pp, int level, int[] perLevel)
    {
      int n = 0;
      foreach (PolyPathBase child in pp)
      {
        if (level < perLevel.Length) perLevel[level]++;
        n += 1 + CountNodes(child, level + 1, perLevel);
      }
      return n;
    }

    [TestMethod]
    public void TestBooleanOpParallelVariants()
    {
      (Paths64 subj, Paths64 clip) = ClusteredInput(5, 40);
      foreach (ClipType ct in new[] { ClipType.Union, ClipType.Difference })
      {
        // polytree: same nesting (nodes per level) and the same area
        PolyTree64 seqTree = new PolyTree64(), parTree = new PolyTree64();
        Clipper.BooleanOp(ct, FillRule.NonZero, subj, clip, seqTree);
        Clipper.BooleanOpParallel(ct, FillRule.NonZero, subj, clip, parTree);
        int[] l1 = new int[4], l2 = new int[4];
        Assert.AreEqual(CountNodes(seqTree, 0, l1), CountNodes(parTree, 0, l2), $"{ct}: node count");
        CollectionAssert.AreEqual(l1, l2, $"{ct}: nodes per level");
        Assert.AreEqual(seqTree.Area(), parTree.Area(), 1e-5 * Math.Abs(seqTree.Area()) + 1, $"{ct}: tree area");
        foreach (PolyPath64 top in parTree)
        {
          Assert.IsFalse(top.IsHole);
          foreach (PolyPath64 child in top) Assert.IsTrue(child.IsHole);
        }

        // PathsD and PolyTreeD, against ClipperD with the same precision
        PathsD subjD = Clipper.ScalePathsD(subj, 0.001), clipD = Clipper.ScalePathsD(clip, 0.001);
        ClipperD cd = new ClipperD(3);
        cd.AddSubject(subjD);
        cd.AddClip(clipD);
        PathsD seqD = new PathsD();
        cd.Execute(ct, FillRule.NonZero, seqD);
        PathsD parD = Clipper.BooleanOpParallel(ct, FillRule.NonZero, subjD, clipD, 3);
        Assert.AreEqual(seqD.Count, parD.Count, $"{ct}: PathsD count");
        Assert.AreEqual(Clipper.Area(seqD), Clipper.Area(parD), 1e-5 * Math.Abs(Clipper.Area(seqD)), $"{ct}: PathsD area");
        PolyTreeD seqTreeD = new PolyTreeD(), parTreeD = new PolyTreeD();
        cd.Execute(ct, FillRule.NonZero, seqTreeD);
        Clipper.BooleanOpParallel(ct, FillRule.NonZero, subjD, clipD, parTreeD, 3);
        Assert.AreEqual(seqTreeD.Area(), parTreeD.Area(), 1e-5 * Math.Abs(seqTreeD.Area()), $"{ct}: PolyTreeD area");
        Assert.AreEqual(Clipper.Area(parD), parTreeD.Area(), 1e-9 * Math.Abs(Clipper.Area(parD)), $"{ct}: PolyTreeD vs PathsD");
      }

      // the union shorthands
      Paths64 u = Clipper.UnionParallel(subj, FillRule.NonZero);
      Assert.AreEqual(Clipper.Area(Clipper.Union(subj, FillRule.NonZero)), Clipper.Area(u),
        1e-5 * Math.Abs(Clipper.Area(u)));
      PathsD uD = Clipper.UnionParallel(Clipper.ScalePathsD(subj, 0.01), FillRule.NonZero, 2);
      Assert.AreEqual(Clipper.Area(u) * 1e-4, Clipper.Area(uD), 1e-4 * Math.Abs(Clipper.Area(uD)));

      // small inputs take the plain (bit-identical) route
      Paths64 small = new Paths64 { Clipper.MakePath(new long[] { 0, 0, 10, 0, 10, 10 }) };
      CollectionAssert.AreEqual(Clipper.Union(small, FillRule.NonZero)[0],
        Clipper.UnionParallel(small, FillRule.NonZero)[0]);
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
