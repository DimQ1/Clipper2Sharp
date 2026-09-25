using System;
using Clipper2Lib;

namespace ConsoleDemo
{
  internal static class Program
  {
    private static void Main(string[] args)
    {
      // A simple boolean operation demo (port of CPP/Examples/ConsoleDemo).
      Paths64 subject = new Paths64
      {
        Clipper.MakePath(new int[] { 100, 50, 10, 79, 65, 2, 65, 98, 10, 21 })
      };
      Paths64 clip = new Paths64
      {
        Clipper.MakePath(new int[] { 20, 20, 20, 80, 80, 80, 80, 20 })
      };

      Paths64 solution = Clipper.Intersect(subject, clip, FillRule.EvenOdd);
      Console.WriteLine("Intersection of a star and a square:");
      Console.WriteLine($"  subject area : {Clipper.Area(subject)}");
      Console.WriteLine($"  clip area    : {Clipper.Area(clip)}");
      Console.WriteLine($"  solution area: {Clipper.Area(solution)}");
      Console.WriteLine($"  solution paths: {solution.Count}");

      // ... and the same operation using double precision coordinates
      PathsD subjectD = Clipper.PathsD(subject);
      PathsD clipD = Clipper.PathsD(clip);
      PathsD solutionD = Clipper.Intersect(subjectD, clipD, FillRule.EvenOdd);
      Console.WriteLine($"  (PathsD variant) solution area: {Clipper.Area(solutionD):0.###}");

      if (args.Length > 0)
      {
        SvgWriter svg = new SvgWriter(FillRule.EvenOdd);
        SvgUtils.AddSubject(svg, subject);
        SvgUtils.AddClip(svg, clip);
        SvgUtils.AddSolution(svg, solution, false);
        SvgUtils.SaveToFile(svg, args[0], FillRule.EvenOdd, 800, 600, 10);
        Console.WriteLine($"SVG written to {args[0]}");
      }
    }
  }
}
