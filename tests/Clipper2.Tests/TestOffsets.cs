using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  /// <summary>
  /// Ports of the C++ tests in CPP/Tests/TestOffsets.cpp.
  /// </summary>
  [TestClass]
  public class TestOffsets
  {
    [TestMethod]
    public void TestOffsetEmpty()
    {
      Paths64 solution = new Paths64();

      ClipperOffset offset = new ClipperOffset();
      offset.Execute(10, solution);
    }

    [TestMethod]
    public void TestOffsets1() // the file driven test (Offsets.txt)
    {
      for (int testNumber = 1; testNumber <= 2; ++testNumber)
      {
        ClipperOffset co = new ClipperOffset();
        Paths64 subject = new Paths64(), subjectOpen = new Paths64(), clip = new Paths64();

        Assert.IsTrue(ClipperFileIO.LoadTestNum(TestData.Path("Offsets.txt"),
          testNumber, subject, subjectOpen, clip,
          out ClipType ct, out FillRule fr, out long storedArea, out int storedCount, out _),
          string.Format("Loading test {0} failed.", testNumber));

        co.AddPaths(subject, JoinType.Round, EndType.Polygon);
        Paths64 outputs = new Paths64();
        co.Execute(1, outputs);
        // is the sum total area of the solution positive
        bool outerIsPositive = Clipper.Area(outputs) > 0;
        // there should be exactly one exterior path
        int isPositiveCount = 0;
        foreach (Path64 path in outputs)
          if (Clipper.IsPositive(path)) isPositiveCount++;
        int isNegativeCount = outputs.Count - isPositiveCount;
        if (outerIsPositive)
          Assert.AreEqual(1, isPositiveCount);
        else
          Assert.AreEqual(1, isNegativeCount);
      }
    }

    [TestMethod]
    public void TestOffsets2() // see #448 & #456
    {
      double scale = 10, delta = 10 * scale, arcTol = 0.25 * scale;
      Paths64 subject = new Paths64(), solution = new Paths64();
      ClipperOffset c = new ClipperOffset();
      subject.Add(Clipper.MakePath(new int[] { 50, 50, 100, 50, 100, 150, 50, 150, 0, 100 }));
      subject = Clipper.ScalePaths(subject, scale);
      c.AddPaths(subject, JoinType.Round, EndType.Polygon);
      c.ArcTolerance = arcTol;
      c.Execute(delta, solution);
      double minDist = delta * 2, maxDist = 0;
      foreach (Point64 subjPt in subject[0])
      {
        Point64 prevPt = solution[0][solution[0].Count - 1];
        foreach (Point64 pt in solution[0])
        {
          Point64 mp = MidPoint(prevPt, pt);
          double d = Distance(mp, subjPt);
          if (d < delta * 2)
          {
            if (d < minDist) minDist = d;
            if (d > maxDist) maxDist = d;
          }
          prevPt = pt;
        }
      }
      Assert.IsTrue(minDist + 1 >= delta - arcTol); // +1 for rounding errors
      Assert.IsTrue(solution[0].Count <= 21);
    }

    [TestMethod]
    public void TestOffsets5() // modified from #593 (tests offset clean up)
    {
      Paths64 subject = new Paths64
      {
        Clipper.MakePath(new int[] {
          17325,1094, 17264,1027, 17206,956, 17153,882, 17104,805, 17059,725,
          17020,643, 16985,559, 16972,524, 15324,524, 15291,610, 15254,693,
          15211,773, 15164,852, 15112,927, 15056,999, 14996,1068, 14932,1133,
          14865,1195, 14794,1252, 14719,1305, 14642,1354, 14562,1398, 14480,1437,
          14395,1472, 14309,1502, 14221,1526, 14132,1546, 14042,1560, 13951,1569,
          13860,1573, 13768,1571, 13677,1564, 13587,1552, 13497,1535, 13409,1512,
          13322,1485, 13236,1452, 13153,1414, 13072,1372, 12994,1325, 12918,1274,
          12846,1218, 12777,1158, 12712,1094, 12650,1027, 12592,956, 12539,882,
          12490,805, 12446,725, 12406,643, 12371,559, 12358,524, 10711,524,
          10678,610, 10640,693, 10597,773, 10550,852, 10498,927, 10442,999,
          10382,1068, 10319,1133, 10251,1195, 10180,1252, 10106,1305, 10028,1354,
          9949,1398, 9866,1437, 9782,1472, 9695,1502, 9607,1526, 9518,1546,
          9428,1560, 9337,1569, 9246,1573, 9155,1571, 9064,1564, 8973,1552,
          8883,1535, 8795,1512, 8708,1485, 8623,1452, 8539,1414, 8458,1372,
          8380,1325, 8305,1274, 8232,1218, 8163,1158, 8098,1094, 8036,1027,
          7979,956, 7925,882, 7876,805, 7832,725, 7792,643, 7757,559,
          7745,524, 6097,524, 6064,610, 6026,693, 5983,773, 5936,852,
          5885,927, 5829,999, 5769,1068, 5705,1133, 5637,1195, 5566,1252,
          5492,1305, 5415,1354, 5335,1398, 5252,1437, 5168,1472, 5082,1502,
          4994,1526, 4904,1546, 4814,1560, 4723,1569, 4632,1573, 4541,1571,
          4450,1564, 4359,1552, 4270,1535, 4181,1512, 4094,1485, 4009,1452,
          3926,1414, 3845,1372, 3766,1325, 3691,1274, 3619,1218, 3550,1158,
          3484,1094, 3423,1027, 3365,956, 3312,882, 3263,805, 3218,725,
          3178,643, 3143,559, 3131,524, 1483,524, 1450,609, 1413,692,
          1370,773, 1323,851, 1272,926, 1216,998, 1156,1066, 1093,1131,
          1026,1193, 955,1250, 881,1303, 804,1352, 725,1396, 643,1436,
          559,1470 }),
        Clipper.MakePath(new int[] { -47877, -47877, 84788, -47877, 84788, 81432, -47877, 81432 })
      };
      Paths64 solution = Clipper.InflatePaths(subject, -10000, JoinType.Round, EndType.Polygon);
      Assert.AreEqual(2, solution.Count);
    }

    [TestMethod]
    public void TestOffsets6() // also modified from #593 (tests rounded ends)
    {
      Paths64 subjects = new Paths64
      {
        Clipper.MakePath(new int[] { 620,620, -620,620, -620,-620, 620,-620 }),
        Clipper.MakePath(new int[] {
          20,-277, 42,-275, 59,-272, 80,-266, 97,-261, 114,-254,
          135,-243, 149,-235, 167,-222, 182,-211, 197,-197,
          212,-181, 223,-167, 234,-150, 244,-133, 253,-116,
          260,-99, 267,-78, 272,-61, 275,-40, 278,-18, 276,-39,
          272,-61, 267,-79, 260,-99, 253,-116, 245,-133, 235,-150,
          223,-167, 212,-181, 197,-197, 182,-211, 168,-222, 152,-233,
          135,-243, 114,-254, 97,-261, 80,-267, 59,-272, 42,-275, 20,-278 })
      };
      const double offset = -50;
      ClipperOffset offseter = new ClipperOffset();
      offseter.AddPaths(subjects, JoinType.Round, EndType.Polygon);
      Paths64 solution = new Paths64();
      offseter.Execute(offset, solution);
      Assert.AreEqual(2, solution.Count);
      double area = Clipper.Area(solution[1]);
      Assert.IsTrue(area < -47500);
    }

    [TestMethod]
    public void TestOffsets7() // (#593 & #715)
    {
      Paths64 solution;
      Paths64 subject = new Paths64 { Clipper.MakePath(new int[] { 0, 0, 100, 0, 100, 100, 0, 100 }) };
      solution = Clipper.InflatePaths(subject, -50, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(0, solution.Count);
      subject.Add(Clipper.MakePath(new int[] { 40, 60, 60, 60, 60, 40, 40, 40 }));
      solution = Clipper.InflatePaths(subject, 10, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      subject[0].Reverse();
      subject[1].Reverse();
      solution = Clipper.InflatePaths(subject, 10, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      subject.RemoveAt(1);
      solution = Clipper.InflatePaths(subject, -50, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(0, solution.Count);
    }

    [TestMethod]
    public void TestOffsets9() // (#733)
    {
      // solution orientations should match subject orientations UNLESS
      // reverse_solution is set true in ClipperOffset's constructor
      // start subject's orientation positive ...
      Paths64 subject = new Paths64 { Clipper.MakePath(new int[] { 100, 100, 200, 100, 200, 400, 100, 400 }) };
      Paths64 solution = Clipper.InflatePaths(subject, 50, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      Assert.IsTrue(Clipper.IsPositive(solution[0]));
      // reversing subject's orientation should not affect delta direction
      // (ie where positive deltas inflate).
      subject[0].Reverse();
      solution = Clipper.InflatePaths(subject, 50, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      Assert.IsTrue(Math.Abs(Clipper.Area(solution[0])) > Math.Abs(Clipper.Area(subject[0])));
      Assert.IsFalse(Clipper.IsPositive(solution[0]));
      ClipperOffset co = new ClipperOffset(2, 0, false, true); // last param. reverses solution
      co.AddPaths(subject, JoinType.Miter, EndType.Polygon);
      co.Execute(50, solution);
      Assert.AreEqual(1, solution.Count);
      Assert.IsTrue(Math.Abs(Clipper.Area(solution[0])) > Math.Abs(Clipper.Area(subject[0])));
      Assert.IsTrue(Clipper.IsPositive(solution[0]));
      // add a hole (ie has reverse orientation to outer path)
      subject.Add(Clipper.MakePath(new int[] { 130, 130, 170, 130, 170, 370, 130, 370 }));
      solution = Clipper.InflatePaths(subject, 30, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      Assert.IsFalse(Clipper.IsPositive(solution[0]));
      co.Clear(); // should still reverse solution orientation
      co.AddPaths(subject, JoinType.Miter, EndType.Polygon);
      co.Execute(30, solution);
      Assert.AreEqual(1, solution.Count);
      Assert.IsTrue(Math.Abs(Clipper.Area(solution[0])) > Math.Abs(Clipper.Area(subject[0])));
      Assert.IsTrue(Clipper.IsPositive(solution[0]));
      solution = Clipper.InflatePaths(subject, -15, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(0, solution.Count);
    }

    private static Point64 MidPoint(Point64 p1, Point64 p2)
    {
      return new Point64((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }

    private static double Distance(Point64 pt1, Point64 pt2)
    {
      return Math.Sqrt(Clipper.DistanceSqr(pt1, pt2));
    }
  }
}
