using System;
using System.Diagnostics;
using Clipper2Lib;

namespace Clipper2.WebDemo.Demo;

public sealed record BenchRow(int Shapes, long Vertices, double Milliseconds, double AllocatedMB, int ResultPaths, bool AllocationMeasured);

/// <summary>
/// The workload behind the speed panel: union of an increasing number of
/// overlapping shapes. It is the operation the repository's own benchmark uses as
/// its worst case (one big sweep over every path in one call), so the curve it
/// draws in the browser is the same shape as the one in the README.
/// </summary>
public static class Bench
{
  public const int Seed = 20260925;
  public const int DefaultVerticesPerShape = 32;
  public const long Spread = 3200;
  public const double ShapeRadius = 26;

  public static readonly int[] DefaultCounts = { 25, 50, 100, 200, 400 };

  public static BenchRow[] UnionScaling(int[] counts, int verticesPerShape)
  {
    BenchRow[] rows = new BenchRow[counts.Length];
    for (int i = 0; i < counts.Length; i++)
    {
      int shapes = counts[i];
      Paths64 subject = Shapes.RandomPaths(Seed + i, shapes, verticesPerShape, ShapeRadius, Spread, Spread);
      long vertices = 0;
      foreach (Path64 path in subject) vertices += path.Count;

      // one warm-up run, then the best of a few - the same convention the
      // repository's benchmarks use, so a single noisy run does not define the row
      int reps = shapes <= 100 ? 3 : 1;
      Clipper.Union(subject, FillRule.NonZero);

      long allocatedBefore = AllocationMeter.Read();
      double best = double.MaxValue;
      Paths64 result = new();
      for (int r = 0; r < reps; r++)
      {
        long start = Stopwatch.GetTimestamp();
        result = Clipper.Union(subject, FillRule.NonZero);
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        if (ms < best) best = ms;
      }

      long allocatedAfter = AllocationMeter.Read();
      bool measured = AllocationMeter.Measured(allocatedBefore, allocatedAfter);
      double allocatedMb = measured ? (allocatedAfter - allocatedBefore) / (double) reps / 1048576.0 : 0;
      rows[i] = new BenchRow(shapes, vertices, best, allocatedMb, result.Count, measured);
    }
    return rows;
  }
}
