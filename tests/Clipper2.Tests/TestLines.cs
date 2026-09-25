using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestLines
  {
    [TestMethod]
    public void TestOpenPaths()
    {
      for (int i = 0; i <= 16; i++)
      {
        Clipper64 c64 = new Clipper64();
        Paths64 subj = new Paths64(), subjOpen = new Paths64(), clip = new Paths64();
        Paths64 solution = new Paths64(), solutionOpen = new Paths64();

        Assert.IsTrue(ClipperFileIO.LoadTestNum(TestData.Path("Lines.txt"),
          i, subj, subjOpen, clip, out ClipType clipType, out FillRule fillrule,
          out long area, out int count, out _),
            string.Format("Loading test {0} failed.", i));

        c64.AddSubject(subj);
        c64.AddOpenSubject(subjOpen);
        c64.AddClip(clip);
        c64.Execute(clipType, fillrule, solution, solutionOpen);

        if (area > 0)
        {
          double area2 = Clipper.Area(solution);
          double a = area / area2;
          Assert.IsTrue(a > 0.995 && a < 1.005,
            string.Format("Incorrect area in test {0}", i));
        }

        if (count > 0 && Math.Abs(solution.Count - count) > 0)
        {
          Assert.IsTrue(Math.Abs(solution.Count - count) < 2,
            string.Format("Incorrect count in test {0}", i));
        }

      } // bottom of num loop
    }
  }
}
