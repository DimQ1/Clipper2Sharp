using System;
using Clipper2Lib;

namespace RectClipDemo
{
  internal static class Program
  {
    private static void Main(string[] args)
    {
      Rect64 rect = new Rect64(100, 100, 700, 500);

      Paths64 subject = new Paths64
      {
        Clipper.MakePath(new int[] { 50, 50, 400, 50, 400, 400, 50, 400 }),
        Clipper.MakePath(new int[] { 400, 200, 800, 200, 800, 600, 400, 600 })
      };
      Paths64 lines = new Paths64
      {
        Clipper.MakePath(new int[] { 0, 300, 800, 300 })
      };

      Paths64 clipped = Clipper.RectClip(rect, subject);
      Paths64 clippedLines = Clipper.RectClipLines(rect, lines);
      Console.WriteLine($"subject area       : {Clipper.Area(subject)}");
      Console.WriteLine($"clipped area       : {Clipper.Area(clipped)}");
      Console.WriteLine($"clipped line count : {clippedLines.Count}");

      RectD rectD = new RectD(100.5, 100.5, 700.5, 500.5);
      PathsD subjectD = Clipper.PathsD(subject);
      PathsD clippedD = Clipper.RectClip(rectD, subjectD);
      Console.WriteLine($"clipped area (D)   : {Clipper.Area(clippedD):0.###}");

      if (args.Length > 0)
      {
        SvgWriter svg = new SvgWriter();
        SvgUtils.AddSubject(svg, subject);
        SvgUtils.AddOpenSubject(svg, lines);
        SvgUtils.AddRCSolution(svg, clipped, false);
        SvgUtils.AddOpenSolution(svg, clippedLines, false);
        SvgUtils.SaveToFile(svg, args[0], FillRule.EvenOdd, 800, 600, 10);
        Console.WriteLine($"SVG written to {args[0]}");
      }
    }
  }
}
