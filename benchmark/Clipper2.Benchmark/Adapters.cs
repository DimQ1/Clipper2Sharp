extern alias newcli;
extern alias refcli;

using System;
using System.Collections.Generic;
using N = newcli::Clipper2Lib;
using R = refcli::Clipper2Lib;

namespace Clipper2.Benchmark
{
  /// <summary>Result checksum shared by both adapters.</summary>
  internal readonly struct OpResult : IEquatable<OpResult>
  {
    public readonly long Count;
    public readonly long Area;
    public OpResult(long count, long area) { Count = count; Area = area; }
    public bool Equals(OpResult other) => Count == other.Count && Area == other.Area;
    public override bool Equals(object? obj) => obj is OpResult r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Count, Area);
    public override string ToString() => $"{Count} paths / area {Area}";
  }

  /// <summary>
  /// The operations measured by the benchmark, expressed with raw coordinate
  /// arrays so that both libraries receive identical input.
  /// </summary>
  internal interface IClipperAdapter
  {
    string Name { get; }
    OpResult BooleanOp(long[][] subj, long[][]? clip, int clipType, int fillRule);

    /// <summary>
    /// nb: the join and end types are passed by NAME (not by numeric value),
    /// because the two libraries number their JoinType members differently
    /// (the C++ 2.0.1 port uses Square, Bevel, Round, Miter, while the older
    /// upstream C# library uses Miter, Square, Bevel, Round).
    /// </summary>
    OpResult Inflate(long[][] subjects, double delta, string joinType, string endType,
      double miterLimit, double arcTolerance);
    OpResult RectClip(int l, int t, int r, int b, long[][] subject);
    OpResult RectClipLines(int l, int t, int r, int b, long[][] subject);
    OpResult Simplify(long[][] paths, double epsilon);
    OpResult Triangulate(long[][] paths);
    OpResult Areas(long[][] paths);
    long Bounds(long[][] paths);
    long PointInPolygonHits(long[][] subject, long[] xs, long[] ys);
    OpResult ScaleRoundTrip(long[][] paths, double scale);

    /// <summary>Only feeds the input into the engine (used to localise allocations).</summary>
    OpResult IngestOnly(long[][] paths);
  }

  internal sealed class NewAdapter : IClipperAdapter
  {
    public string Name => "Clipper2Sharp (new)";

    private static N.Paths64 ToPaths(long[][] raw)
    {
      N.Paths64 paths = new N.Paths64(raw.Length);
      foreach (long[] pts in raw)
      {
        N.Path64 path = new N.Path64(pts.Length / 2);
        for (int i = 0; i < pts.Length; i += 2)
          path.Add(new N.Point64(pts[i], pts[i + 1]));
        paths.Add(path);
      }
      return paths;
    }

    public OpResult BooleanOp(long[][] subj, long[][]? clip, int clipType, int fillRule)
    {
      N.Clipper64 c = new N.Clipper64();
      c.AddSubject(ToPaths(subj));
      if (clip != null) c.AddClip(ToPaths(clip));
      N.Paths64 solution = new N.Paths64();
      c.Execute((N.ClipType) clipType, (N.FillRule) fillRule, solution);
      return new OpResult(solution.Count, (long) N.Clipper.Area(solution));
    }

    public OpResult Inflate(long[][] subjects, double delta, string joinType, string endType,
      double miterLimit, double arcTolerance)
    {
      N.ClipperOffset co = new N.ClipperOffset(miterLimit, arcTolerance);
      co.AddPaths(ToPaths(subjects), Enum.Parse<N.JoinType>(joinType), Enum.Parse<N.EndType>(endType));
      N.Paths64 solution = new N.Paths64();
      co.Execute(delta, solution);
      return new OpResult(solution.Count, (long) N.Clipper.Area(solution));
    }

    public OpResult RectClip(int l, int t, int r, int b, long[][] subject)
    {
      N.Paths64 solution = N.Clipper.RectClip(new N.Rect64(l, t, r, b), ToPaths(subject));
      return new OpResult(solution.Count, (long) N.Clipper.Area(solution));
    }

    public OpResult RectClipLines(int l, int t, int r, int b, long[][] subject)
    {
      N.Paths64 solution = N.Clipper.RectClipLines(new N.Rect64(l, t, r, b), ToPaths(subject));
      return new OpResult(solution.Count, (long) N.Clipper.Area(solution));
    }

    public OpResult Simplify(long[][] paths, double epsilon)
    {
      N.Paths64 solution = N.Clipper.SimplifyPaths(ToPaths(paths), epsilon);
      return new OpResult(solution.Count, (long) N.Clipper.Area(solution));
    }

    public OpResult Triangulate(long[][] paths)
    {
      N.TriangulateResult res = N.Clipper.Triangulate(ToPaths(paths), out N.Paths64 solution);
      return new OpResult((long) res * 1_000_000 + solution.Count, (long) N.Clipper.Area(solution));
    }

    public OpResult Areas(long[][] paths)
    {
      N.Paths64 p = ToPaths(paths);
      return new OpResult(p.Count, (long) N.Clipper.Area(p));
    }

    public long Bounds(long[][] paths)
    {
      N.Rect64 r = N.Clipper.GetBounds(ToPaths(paths));
      return r.Width + r.Height;
    }

    public long PointInPolygonHits(long[][] subject, long[] xs, long[] ys)
    {
      N.Paths64 paths = ToPaths(subject);
      long hits = 0;
      for (int i = 0; i < xs.Length; i++)
      {
        N.Point64 pt = new N.Point64(xs[i], ys[i]);
        foreach (N.Path64 path in paths)
          if (N.Clipper.PointInPolygon(pt, path) != N.PointInPolygonResult.IsOutside) hits++;
      }
      return hits;
    }

    public OpResult ScaleRoundTrip(long[][] paths, double scale)
    {
      N.Paths64 p = ToPaths(paths);
      N.PathsD d = N.Clipper.ScalePathsD(p, scale);
      N.Paths64 p2 = N.Clipper.ScalePaths64(d, 1 / scale);
      return new OpResult(p2.Count, (long) N.Clipper.Area(p2));
    }

    public OpResult IngestOnly(long[][] paths)
    {
      N.Clipper64 c = new N.Clipper64();
      c.AddSubject(ToPaths(paths));
      return new OpResult(0, 0);
    }
  }

  internal sealed class RefAdapter : IClipperAdapter
  {
    public string Name => "Clipper2 (upstream C#)";

    private static R.Paths64 ToPaths(long[][] raw)
    {
      R.Paths64 paths = new R.Paths64(raw.Length);
      foreach (long[] pts in raw)
      {
        R.Path64 path = new R.Path64(pts.Length / 2);
        for (int i = 0; i < pts.Length; i += 2)
          path.Add(new R.Point64(pts[i], pts[i + 1]));
        paths.Add(path);
      }
      return paths;
    }

    public OpResult BooleanOp(long[][] subj, long[][]? clip, int clipType, int fillRule)
    {
      R.Clipper64 c = new R.Clipper64();
      c.AddSubject(ToPaths(subj));
      if (clip != null) c.AddClip(ToPaths(clip));
      R.Paths64 solution = new R.Paths64();
      c.Execute((R.ClipType) clipType, (R.FillRule) fillRule, solution);
      return new OpResult(solution.Count, (long) R.Clipper.Area(solution));
    }

    public OpResult Inflate(long[][] subjects, double delta, string joinType, string endType,
      double miterLimit, double arcTolerance)
    {
      R.ClipperOffset co = new R.ClipperOffset(miterLimit, arcTolerance);
      co.AddPaths(ToPaths(subjects), Enum.Parse<R.JoinType>(joinType), Enum.Parse<R.EndType>(endType));
      R.Paths64 solution = new R.Paths64();
      co.Execute(delta, solution);
      return new OpResult(solution.Count, (long) R.Clipper.Area(solution));
    }

    public OpResult RectClip(int l, int t, int r, int b, long[][] subject)
    {
      R.Paths64 solution = R.Clipper.RectClip(new R.Rect64(l, t, r, b), ToPaths(subject));
      return new OpResult(solution.Count, (long) R.Clipper.Area(solution));
    }

    public OpResult RectClipLines(int l, int t, int r, int b, long[][] subject)
    {
      R.Paths64 solution = R.Clipper.RectClipLines(new R.Rect64(l, t, r, b), ToPaths(subject));
      return new OpResult(solution.Count, (long) R.Clipper.Area(solution));
    }

    public OpResult Simplify(long[][] paths, double epsilon)
    {
      R.Paths64 solution = R.Clipper.SimplifyPaths(ToPaths(paths), epsilon);
      return new OpResult(solution.Count, (long) R.Clipper.Area(solution));
    }

    public OpResult Triangulate(long[][] paths)
    {
      R.TriangulateResult res = R.Clipper.Triangulate(ToPaths(paths), out R.Paths64 solution);
      return new OpResult((long) res * 1_000_000 + solution.Count, (long) R.Clipper.Area(solution));
    }

    public OpResult Areas(long[][] paths)
    {
      R.Paths64 p = ToPaths(paths);
      return new OpResult(p.Count, (long) R.Clipper.Area(p));
    }

    public long Bounds(long[][] paths)
    {
      R.Rect64 r = R.Clipper.GetBounds(ToPaths(paths));
      return r.Width + r.Height;
    }

    public long PointInPolygonHits(long[][] subject, long[] xs, long[] ys)
    {
      R.Paths64 paths = ToPaths(subject);
      long hits = 0;
      for (int i = 0; i < xs.Length; i++)
      {
        R.Point64 pt = new R.Point64(xs[i], ys[i]);
        foreach (R.Path64 path in paths)
          if (R.Clipper.PointInPolygon(pt, path) != R.PointInPolygonResult.IsOutside) hits++;
      }
      return hits;
    }

    public OpResult ScaleRoundTrip(long[][] paths, double scale)
    {
      R.Paths64 p = ToPaths(paths);
      R.PathsD d = R.Clipper.ScalePathsD(p, scale);
      R.Paths64 p2 = R.Clipper.ScalePaths64(d, 1 / scale);
      return new OpResult(p2.Count, (long) R.Clipper.Area(p2));
    }

    public OpResult IngestOnly(long[][] paths)
    {
      R.Clipper64 c = new R.Clipper64();
      c.AddSubject(ToPaths(paths));
      return new OpResult(0, 0);
    }
  }
}
