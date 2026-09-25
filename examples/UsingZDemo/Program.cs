using System;
using Clipper2ZLib;

namespace UsingZDemo
{
  internal static class MainClass
  {
    private static void Main()
    {
      // The Z-coordinate flavour of the library (Clipper2ZLib) keeps a
      // user defined value with every coordinate (port of the C++ USINGZ build).
      Path64 subject = Clipper.MakePathZ(new long[]
      {
        0,0,1, 100,0,2, 100,100,3, 0,100,4
      });
      Path64 clip = Clipper.MakePathZ(new long[]
      {
        50,50,5, 150,50,6, 150,150,7, 50,150,8
      });

      Clipper64 c = new Clipper64();
      c.AddSubject(subject);
      c.AddClip(clip);

      Paths64 solution = new Paths64();
      c.Execute(ClipType.Intersection, FillRule.EvenOdd, solution);

      Console.WriteLine($"Z-aware intersection: {solution.Count} path(s)");
      foreach (Path64 path in solution)
      {
        Console.Write("  ");
        foreach (Point64 pt in path)
          Console.Write($"{pt} ");
        Console.WriteLine();
      }
    }
  }
}
