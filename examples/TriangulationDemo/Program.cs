using System;
using Clipper2Lib;

namespace TriangulationDemo
{
  internal static class Program
  {
    private static void Main(string[] args)
    {
      Paths64 subject = new Paths64
      {
        Clipper.MakePath(new int[] { 0, 0, 400, 0, 400, 400, 0, 400 }),
        Clipper.MakePath(new int[] { 100, 100, 100, 300, 300, 300, 300, 100 })
      };

      TriangulateResult result = Clipper.Triangulate(subject, out Paths64 solution);
      Console.WriteLine($"triangulation result : {result}");
      Console.WriteLine($"triangle count       : {solution.Count}");
      Console.WriteLine($"triangle total area  : {Clipper.Area(solution)}");
      Console.WriteLine($"subject area         : {Clipper.Area(subject)}");

      if (result == TriangulateResult.success && args.Length > 0)
      {
        SvgWriter svg = new SvgWriter(FillRule.EvenOdd);
        SvgUtils.AddSubject(svg, subject);
        SvgUtils.AddRCSolution(svg, solution, false);
        SvgUtils.SaveToFile(svg, args[0], FillRule.EvenOdd, 800, 600, 10);
        Console.WriteLine($"SVG written to {args[0]}");
      }
    }
  }
}
