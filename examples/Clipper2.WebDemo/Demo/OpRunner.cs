using System;
using System.Collections.Generic;
using System.Diagnostics;
using Clipper2Lib;

namespace Clipper2.WebDemo.Demo;

/// <summary>The operations the page can run on the two shapes.</summary>
public enum OpKind
{
  Union, Intersect, Difference, Xor, Offset, Simplify, RectClip, Triangulate, PointInPolygon
}

/// <summary>Everything an operation needs, i.e. the state of the page.</summary>
public sealed class OpRequest
{
  public OpKind Op { get; init; } = OpKind.Union;
  public Paths64 Subject { get; init; } = new();
  public Paths64 Clip { get; init; } = new();
  public FillRule FillRule { get; init; } = FillRule.NonZero;
  public double Delta { get; init; } = 20;
  public JoinType Join { get; init; } = JoinType.Round;
  public EndType End { get; init; } = EndType.Polygon;
  public double Epsilon { get; init; } = 4;
  public int Repetitions { get; init; } = 5;
  /// <summary>An explicit rectangle for the rect clip operation (the animated window).</summary>
  public Rect64? Rect { get; init; }
}

public readonly record struct Probe(long X, long Y, bool Inside);

/// <summary>What an operation produced, together with how long it took.</summary>
public sealed class OpOutcome
{
  public Paths64 Result { get; set; } = new();
  public List<Path64>? Triangles { get; set; }
  public List<Probe>? Probes { get; set; }
  public Rect64? Rect { get; set; }
  public string Note { get; set; } = string.Empty;

  public double Area { get; set; }
  public double Milliseconds { get; set; }
  public double AllocatedBytesPerRun { get; set; }
  public bool AllocationMeasured { get; set; }
  public int Repetitions { get; set; }
  public long InputVertices { get; set; }
  public long ResultVertices { get; set; }
}

/// <summary>
/// Runs one operation and measures it. The repetitions are there because a single
/// run of a small operation is mostly noise: the page shows the best of them, which
/// is the convention the repository's own benchmarks use.
/// </summary>
public static class OpRunner
{
  public static OpOutcome Run(OpRequest request)
  {
    OpOutcome outcome = new();
    int reps = Math.Max(1, request.Repetitions);
    outcome.InputVertices = CountVertices(request.Subject) + CountVertices(request.Clip);

    long allocatedBefore = AllocationMeter.Read();
    double best = double.MaxValue;
    Paths64 result = new();
    for (int i = 0; i < reps; i++)
    {
      long start = Stopwatch.GetTimestamp();
      result = Compute(request, outcome);
      double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
      if (ms < best) best = ms;
    }

    long allocatedAfter = AllocationMeter.Read();
    outcome.Result = result;
    outcome.Milliseconds = best;
    outcome.Repetitions = reps;
    // a single operation is shorter than the runtime updates its counter, so the
    // per run figure is only reported when there was more than one run to average
    outcome.AllocationMeasured = reps >= 2 && AllocationMeter.Measured(allocatedBefore, allocatedAfter);
    outcome.AllocatedBytesPerRun = outcome.AllocationMeasured
      ? (allocatedAfter - allocatedBefore) / (double) reps
      : 0;
    outcome.ResultVertices = CountVertices(result);
    // area of the result is the honest headline number for the boolean operations;
    // for triangulation it is the same area, split into triangles
    outcome.Area = Clipper.Area(result);
    return outcome;
  }

  private static Paths64 Compute(OpRequest request, OpOutcome outcome)
  {
    switch (request.Op)
    {
      case OpKind.Union:
        return request.Clip.Count == 0
          ? Clipper.Union(request.Subject, request.FillRule)
          : Clipper.Union(request.Subject, request.Clip, request.FillRule);

      case OpKind.Intersect:
        return Clipper.Intersect(request.Subject, request.Clip, request.FillRule);

      case OpKind.Difference:
        return Clipper.Difference(request.Subject, request.Clip, request.FillRule);

      case OpKind.Xor:
        return Clipper.Xor(request.Subject, request.Clip, request.FillRule);

      case OpKind.Offset:
        return Clipper.InflatePaths(request.Subject, request.Delta, request.Join, request.End);

      case OpKind.Simplify:
        return Clipper.SimplifyPaths(request.Subject, request.Epsilon);

      case OpKind.RectClip:
      {
        // the clip shape's bounding box is the rectangle, unless the caller supplied
        // one (the animation slides its own window); because it follows the shape,
        // the effect of the rectangle clipping is visible while dragging
        Rect64 rect = request.Rect ?? Clipper.GetBounds(request.Clip.Count > 0 ? request.Clip : request.Subject);
        outcome.Rect = rect;
        return rect.IsEmpty() ? new Paths64() : Clipper.RectClip(rect, request.Subject);
      }

      case OpKind.Triangulate:
      {
        TriangulateResult status = Clipper.Triangulate(request.Subject, out Paths64 triangles);
        if (status != TriangulateResult.success)
        {
          // the three failure modes explain themselves; keep them visible instead of
          // silently drawing nothing
          outcome.Note = status switch
          {
            TriangulateResult.noPolygons => "the subject has no polygon to triangulate",
            TriangulateResult.pathsIntersect => "the subject paths intersect - untick \"keep separate paths\" or run Union first",
            _ => "triangulation failed (" + status + ")"
          };
          return new Paths64();
        }
        outcome.Triangles = new List<Path64>(triangles);
        return triangles;
      }

      case OpKind.PointInPolygon:
        return Probe(request, outcome);

      default:
        return new Paths64();
    }
  }

  /// <summary>
  /// The probe grid of the point-in-polygon page. It also times the two ways of
  /// asking the same question - the plain vertex scan and the prepared locator -
  /// because that is the difference the locator exists for.
  /// </summary>
  private static Paths64 Probe(OpRequest request, OpOutcome outcome)
  {
    (long[] xs, long[] ys) = Shapes.ProbeGrid();
    int count = xs.Length;
    Point64[] points = new Point64[count];
    for (int i = 0; i < count; i++) points[i] = new Point64(xs[i], ys[i]);

    // inside means "inside an odd number of subject paths", i.e. the EvenOdd union
    // of the subject - the same visual the other operations show
    PointInPolygonResult[] hits = new PointInPolygonResult[count];
    foreach (Path64 path in request.Subject)
    {
      if (path.Count < 3) continue;
      Clipper.PointInPolygon(path, points, hits);
    }

    // The locator exists for the case the plain scan is bad at: a dense polygon
    // asked about many points. The star above is only 14 vertices, so the same
    // question is asked again about a 2000 vertex circle, and both ways are timed.
    Path64 dense = Clipper.Ellipse(new Point64(Shapes.WorldWidth / 2, Shapes.WorldHeight / 2), 420, 380, 2000);
    PointInPolygonResult[] denseHits = new PointInPolygonResult[count];
    long denseScanTicks = Stopwatch.GetTimestamp();
    Clipper.PointInPolygon(dense, points, denseHits);
    denseScanTicks = Stopwatch.GetTimestamp() - denseScanTicks;

    PointInPolygonLocator denseLocator = new(dense);
    long denseLocatorTicks = Stopwatch.GetTimestamp();
    denseLocator.Locate(points, denseHits);
    denseLocatorTicks = Stopwatch.GetTimestamp() - denseLocatorTicks;

    double scanMs = denseScanTicks * 1000.0 / Stopwatch.Frequency;
    double locatorMs = denseLocatorTicks * 1000.0 / Stopwatch.Frequency;
    outcome.Note = $"{count} probes against a 2000 vertex circle: " +
                   $"vertex scan {scanMs:F3} ms, prepared locator {locatorMs:F3} ms (" +
                   $"{(scanMs / Math.Max(locatorMs, 1e-6)):F1}x faster)";

    List<Probe> probes = new(count);
    PointInPolygonResult[] final = new PointInPolygonResult[count];
    foreach (Path64 path in request.Subject)
    {
      if (path.Count < 3) continue;
      Clipper.PointInPolygon(path, points, final);
    }
    for (int i = 0; i < count; i++)
      probes.Add(new Probe(xs[i], ys[i], final[i] == PointInPolygonResult.IsInside));

    outcome.Probes = probes;
    return new Paths64();
  }

  private static long CountVertices(Paths64 paths)
  {
    long n = 0;
    foreach (Path64 path in paths) n += path.Count;
    return n;
  }
}
