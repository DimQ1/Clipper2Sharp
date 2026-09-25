using System;
using Clipper2Lib;

namespace InflateDemo
{
  internal static class Program
  {
    private static void Main(string[] args)
    {
      // A demo of the four join types and the open path end types
      // (port of CPP/Examples/Inflate).
      Paths64 subject = new Paths64
      {
        Clipper.MakePath(new int[] { 0, 0, 100, 0, 100, 100, 0, 100 })
      };
      Paths64 openSubject = new Paths64
      {
        Clipper.MakePath(new int[] { -50, 50, 150, 50, 150, 120 })
      };

      SvgWriter svg = new SvgWriter();
      SvgUtils.AddSubject(svg, subject);
      SvgUtils.AddOpenSubject(svg, openSubject);

      double[] deltas = { 10, -10 };
      JoinType[] joins = { JoinType.Square, JoinType.Bevel, JoinType.Round, JoinType.Miter };
      EndType[] ends = { EndType.Butt, EndType.Square, EndType.Round, EndType.Joined };
      int dy = 0;
      foreach (double delta in deltas)
        foreach (JoinType jt in joins)
        {
          Paths64 solution = Clipper.InflatePaths(subject, delta, jt, EndType.Polygon);
          Paths64 solutionOpen = Clipper.InflatePaths(openSubject, delta, jt, ends[dy % ends.Length]);
          Console.WriteLine($"{jt,-6} {delta,4}: closed paths {solution.Count}, area {Clipper.Area(solution):0}")
            ;
          SvgUtils.AddSolution(svg, solution, false);
          SvgUtils.AddOpenSolution(svg, solutionOpen, false);
          dy++;
        }

      if (args.Length > 0)
      {
        SvgUtils.SaveToFile(svg, args[0], FillRule.EvenOdd, 800, 600, 10);
        Console.WriteLine($"SVG written to {args[0]}");
      }
    }
  }
}
