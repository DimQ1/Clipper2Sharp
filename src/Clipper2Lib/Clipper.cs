/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  5 March 2025                                                    *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  This module provides a simple interface to the Clipper Library  *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.h (Clipper2 ver. 2.0.1)                          *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  // PRE-COMPILER CONDITIONAL ...
  // USINGZ: For user defined Z-coordinates. See Clipper.SetZ

  public static class Clipper
  {
    private static readonly Rect64 invalidRect64 = new Rect64(false);
    public static Rect64 InvalidRect64 => invalidRect64;

    private static readonly RectD invalidRectD = new RectD(false);
    public static RectD InvalidRectD => invalidRectD;

    // Boolean operations --------------------------------------------------------

    public static Paths64 Intersect(Paths64 subject, Paths64 clip, FillRule fillRule)
    {
      return BooleanOp(ClipType.Intersection, fillRule, subject, clip);
    }

    public static PathsD Intersect(PathsD subject, PathsD clip,
      FillRule fillRule, int precision = 2)
    {
      return BooleanOp(ClipType.Intersection, fillRule, subject, clip, precision);
    }

    public static Paths64 Union(Paths64 subject, FillRule fillRule)
    {
      return BooleanOp(ClipType.Union, fillRule, subject, new Paths64());
    }

    public static Paths64 Union(Paths64 subject, Paths64 clip, FillRule fillRule)
    {
      return BooleanOp(ClipType.Union, fillRule, subject, clip);
    }

    public static PathsD Union(PathsD subject, FillRule fillRule)
    {
      return BooleanOp(ClipType.Union, fillRule, subject, new PathsD());
    }

    public static PathsD Union(PathsD subject, PathsD clip,
      FillRule fillRule, int precision = 2)
    {
      return BooleanOp(ClipType.Union, fillRule, subject, clip, precision);
    }

    public static Paths64 Difference(Paths64 subject, Paths64 clip, FillRule fillRule)
    {
      return BooleanOp(ClipType.Difference, fillRule, subject, clip);
    }

    public static PathsD Difference(PathsD subject, PathsD clip,
      FillRule fillRule, int precision = 2)
    {
      return BooleanOp(ClipType.Difference, fillRule, subject, clip, precision);
    }

    public static Paths64 Xor(Paths64 subject, Paths64 clip, FillRule fillRule)
    {
      return BooleanOp(ClipType.Xor, fillRule, subject, clip);
    }

    public static PathsD Xor(PathsD subject, PathsD clip,
      FillRule fillRule, int precision = 2)
    {
      return BooleanOp(ClipType.Xor, fillRule, subject, clip, precision);
    }

    public static Paths64 BooleanOp(ClipType clipType, FillRule fillRule,
      Paths64 subject, Paths64? clip)
    {
      Paths64 solution = new Paths64();
      if (subject == null) return solution;
      Clipper64 c = new Clipper64();
      c.AddSubject(subject);
      if (clip != null && clip.Count > 0)
        c.AddClip(clip);
      c.Execute(clipType, fillRule, solution);
      return solution;
    }

    public static void BooleanOp(ClipType clipType, FillRule fillRule,
      Paths64 subject, Paths64? clip, PolyTree64 polytree)
    {
      if (subject == null) return;
      Clipper64 c = new Clipper64();
      c.AddSubject(subject);
      if (clip != null && clip.Count > 0)
        c.AddClip(clip);
      c.Execute(clipType, fillRule, polytree);
    }

    public static PathsD BooleanOp(ClipType clipType, FillRule fillRule,
      PathsD subject, PathsD? clip, int precision = 2)
    {
      PathsD solution = new PathsD();
      ClipperD c = new ClipperD(precision);
      c.AddSubject(subject);
      if (clip != null && clip.Count > 0)
        c.AddClip(clip);
      c.Execute(clipType, fillRule, solution);
      return solution;
    }

    public static void BooleanOp(ClipType clipType, FillRule fillRule,
      PathsD subject, PathsD? clip, PolyTreeD polytree, int precision = 2)
    {
      if (subject == null) return;
      ClipperD c = new ClipperD(precision);
      c.AddSubject(subject);
      if (clip != null && clip.Count > 0)
        c.AddClip(clip);
      c.Execute(clipType, fillRule, polytree);
    }

    // Offsetting ----------------------------------------------------------------

    public static Paths64 InflatePaths(Paths64 paths, double delta,
      JoinType joinType, EndType endType,
      double miterLimit = 2.0, double arcTolerance = 0.0)
    {
      if (delta == 0.0) return paths;
      ClipperOffset co = new ClipperOffset(miterLimit, arcTolerance);
      co.AddPaths(paths, joinType, endType);
      Paths64 solution = new Paths64();
      co.Execute(delta, solution);
      return solution;
    }

    public static PathsD InflatePaths(PathsD paths, double delta,
      JoinType joinType, EndType endType, double miterLimit = 2.0,
      int precision = 2, double arcTolerance = 0.0)
    {
      InternalClipper.CheckPrecision(precision);
      if (delta == 0.0) return paths;
      double scale = Math.Pow(10, precision);
      Paths64 tmp = ScalePaths64(paths, scale);
      ClipperOffset co = new ClipperOffset(miterLimit, scale * arcTolerance);
      co.AddPaths(tmp, joinType, endType);
      co.Execute(delta * scale, tmp); // reuse 'tmp' to receive (scaled) solution
      return ScalePathsD(tmp, 1 / scale);
    }

    // Rect clipping -------------------------------------------------------------

    public static Paths64 RectClip(Rect64 rect, Paths64 paths)
    {
      if (rect.IsEmpty() || paths.Count == 0) return new Paths64();
      RectClip64 rc = new RectClip64(rect);
      return rc.Execute(paths);
    }

    public static Paths64 RectClip(Rect64 rect, Path64 path)
    {
      if (rect.IsEmpty() || path.Count == 0) return new Paths64();
      Paths64 tmp = new Paths64(1) { path };
      return RectClip(rect, tmp);
    }

    public static PathsD RectClip(RectD rect, PathsD paths, int precision = 2)
    {
      InternalClipper.CheckPrecision(precision);
      if (rect.IsEmpty() || paths.Count == 0) return new PathsD();
      double scale = Math.Pow(10, precision);
      Rect64 r = ScaleRect(rect, scale);
      Paths64 tmpPath = ScalePaths64(paths, scale);
      RectClip64 rc = new RectClip64(r);
      tmpPath = rc.Execute(tmpPath);
      return ScalePathsD(tmpPath, 1 / scale);
    }

    public static PathsD RectClip(RectD rect, PathD path, int precision = 2)
    {
      if (rect.IsEmpty() || path.Count == 0) return new PathsD();
      PathsD tmp = new PathsD(1) { path };
      return RectClip(rect, tmp, precision);
    }

    public static Paths64 RectClipLines(Rect64 rect, Paths64 paths)
    {
      if (rect.IsEmpty() || paths.Count == 0) return new Paths64();
      RectClipLines64 rc = new RectClipLines64(rect);
      return rc.Execute(paths);
    }

    public static Paths64 RectClipLines(Rect64 rect, Path64 path)
    {
      if (rect.IsEmpty() || path.Count == 0) return new Paths64();
      Paths64 tmp = new Paths64(1) { path };
      return RectClipLines(rect, tmp);
    }

    public static PathsD RectClipLines(RectD rect, PathsD paths, int precision = 2)
    {
      InternalClipper.CheckPrecision(precision);
      if (rect.IsEmpty() || paths.Count == 0) return new PathsD();
      double scale = Math.Pow(10, precision);
      Rect64 r = ScaleRect(rect, scale);
      Paths64 tmpPath = ScalePaths64(paths, scale);
      RectClipLines64 rc = new RectClipLines64(r);
      tmpPath = rc.Execute(tmpPath);
      return ScalePathsD(tmpPath, 1 / scale);
    }

    public static PathsD RectClipLines(RectD rect, PathD path, int precision = 2)
    {
      if (rect.IsEmpty() || path.Count == 0) return new PathsD();
      PathsD tmp = new PathsD(1) { path };
      return RectClipLines(rect, tmp, precision);
    }

    // Minkowski -----------------------------------------------------------------

    public static Paths64 MinkowskiSum(Path64 pattern, Path64 path, bool isClosed)
    {
      return Minkowski.Sum(pattern, path, isClosed);
    }

    public static PathsD MinkowskiSum(PathD pattern, PathD path, bool isClosed)
    {
      return Minkowski.Sum(pattern, path, isClosed);
    }

    public static Paths64 MinkowskiDiff(Path64 pattern, Path64 path, bool isClosed)
    {
      return Minkowski.Diff(pattern, path, isClosed);
    }

    public static PathsD MinkowskiDiff(PathD pattern, PathD path, bool isClosed)
    {
      return Minkowski.Diff(pattern, path, isClosed);
    }

    // Area / orientation --------------------------------------------------------

    public static double Area(Path64 path)
    {
      // https://en.wikipedia.org/wiki/Shoelace_formula
      double a = 0.0;
      int cnt = path.Count;
      if (cnt < 3) return 0.0;
      Point64 prevPt = path[cnt - 1];
      foreach (Point64 pt in path)
      {
        a += (double) (prevPt.Y + pt.Y) * (prevPt.X - pt.X);
        prevPt = pt;
      }
      return a * 0.5;
    }

    public static double Area(Paths64 paths)
    {
      int count = paths.Count;
      if (count == 0) return 0.0;
      if (!BulkOps.ShouldParallelize(count))
      {
        double a = 0.0;
        foreach (Path64 path in paths)
          a += Area(path);
        return a;
      }
      // the per path areas are summed in the original order, so the parallel
      // result is bit-identical to the sequential one
      double[] partial = new double[count];
      BulkOps.For(count, i => partial[i] = Area(paths[i]));
      double sum = 0.0;
      for (int i = 0; i < count; i++) sum += partial[i];
      return sum;
    }

    public static double Area(PathD path)
    {
      // https://en.wikipedia.org/wiki/Shoelace_formula
      double a = 0.0;
      int cnt = path.Count;
      if (cnt < 3) return 0.0;
      PointD prevPt = path[cnt - 1];
      foreach (PointD pt in path)
      {
        a += (prevPt.y + pt.y) * (prevPt.x - pt.x);
        prevPt = pt;
      }
      return a * 0.5;
    }

    public static double Area(PathsD paths)
    {
      int count = paths.Count;
      if (count == 0) return 0.0;
      if (!BulkOps.ShouldParallelize(count))
      {
        double a = 0.0;
        foreach (PathD path in paths)
          a += Area(path);
        return a;
      }
      double[] partial = new double[count];
      BulkOps.For(count, i => partial[i] = Area(paths[i]));
      double sum = 0.0;
      for (int i = 0; i < count; i++) sum += partial[i];
      return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPositive(Path64 poly) => Area(poly) >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPositive(PathD poly) => Area(poly) >= 0;

    // Path <-> string -----------------------------------------------------------

    public static string Path64ToString(Path64 path)
    {
      System.Text.StringBuilder result = new System.Text.StringBuilder();
      foreach (Point64 pt in path)
        result.Append(pt.ToString());
      return result.ToString() + '\n';
    }

    public static string Paths64ToString(Paths64 paths)
    {
      System.Text.StringBuilder result = new System.Text.StringBuilder();
      foreach (Path64 path in paths)
        result.Append(Path64ToString(path));
      return result.ToString();
    }

    public static string PathDToString(PathD path)
    {
      System.Text.StringBuilder result = new System.Text.StringBuilder();
      foreach (PointD pt in path)
        result.Append(pt.ToString());
      return result.ToString() + '\n';
    }

    public static string PathsDToString(PathsD paths)
    {
      System.Text.StringBuilder result = new System.Text.StringBuilder();
      foreach (PathD path in paths)
        result.Append(PathDToString(path));
      return result.ToString();
    }

    // Translation / scaling -----------------------------------------------------

    public static Path64 OffsetPath(Path64 path, long dx, long dy)
    {
      Path64 result = new Path64(path.Count);
      foreach (Point64 pt in path)
        result.Add(new Point64(pt.X + dx, pt.Y + dy));
      return result;
    }

    public static Paths64 OffsetPaths(Paths64 paths, long dx, long dy)
    {
      int count = paths.Count;
      Paths64 result = new Paths64(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = OffsetPath(paths[i], dx, dy));
      return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Point64 ScalePoint64(Point64 pt, double scale)
    {
      return new Point64()
      {
        X = (long) Math.Round(pt.X * scale, MidpointRounding.AwayFromZero),
        Y = (long) Math.Round(pt.Y * scale, MidpointRounding.AwayFromZero),
#if USINGZ
        Z = pt.Z
#endif
      };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PointD ScalePointD(Point64 pt, double scale)
    {
      return new PointD()
      {
        x = pt.X * scale,
        y = pt.Y * scale,
#if USINGZ
        z = pt.Z
#endif
      };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect64 ScaleRect(RectD rec, double scale)
    {
      return new Rect64()
      {
        left = (long) (rec.left * scale),
        top = (long) (rec.top * scale),
        right = (long) (rec.right * scale),
        bottom = (long) (rec.bottom * scale)
      };
    }

    public static Path64 ScalePath(Path64 path, double scale)
    {
      if (InternalClipper.IsAlmostZero(scale - 1)) return path;
      int n = path.Count;
      Path64 result = new Path64(n);
      Span<Point64> dst = BulkOps.GrowUninitialized(result, n);
      ReadOnlySpan<Point64> src = CollectionsMarshal.AsSpan(path);
      for (int i = 0; i < n; i++)
      {
#if USINGZ
        dst[i] = new Point64(src[i].X * scale, src[i].Y * scale, src[i].Z);
#else
        dst[i] = new Point64(src[i].X * scale, src[i].Y * scale);
#endif
      }
      return result;
    }

    public static Paths64 ScalePaths(Paths64 paths, double scale)
    {
      if (InternalClipper.IsAlmostZero(scale - 1)) return paths;
      int count = paths.Count;
      Paths64 result = new Paths64(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = ScalePath(paths[i], scale));
      return result;
    }

    public static PathD ScalePath(PathD path, double scale)
    {
      if (InternalClipper.IsAlmostZero(scale - 1)) return path;
      int n = path.Count;
      PathD result = new PathD(n);
      Span<PointD> dst = BulkOps.GrowUninitialized(result, n);
      ReadOnlySpan<PointD> src = CollectionsMarshal.AsSpan(path);
      for (int i = 0; i < n; i++)
      {
#if USINGZ
        dst[i] = new PointD(src[i].x * scale, src[i].y * scale, src[i].z);
#else
        dst[i] = new PointD(src[i].x * scale, src[i].y * scale);
#endif
      }
      return result;
    }

    public static PathsD ScalePaths(PathsD paths, double scale)
    {
      if (InternalClipper.IsAlmostZero(scale - 1)) return paths;
      int count = paths.Count;
      PathsD result = new PathsD(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = ScalePath(paths[i], scale));
      return result;
    }

    // Unlike ScalePath, both ScalePath64 & ScalePathD also involve type conversion
    public static Path64 ScalePath64(PathD path, double scale)
    {
      int n = path.Count;
      Path64 res = new Path64(n);
      Span<Point64> dst = BulkOps.GrowUninitialized(res, n);
      ReadOnlySpan<PointD> src = CollectionsMarshal.AsSpan(path);
      for (int i = 0; i < n; i++)
        dst[i] = new Point64(src[i], scale);
      return res;
    }

    public static Paths64 ScalePaths64(PathsD paths, double scale)
    {
      int count = paths.Count;
      Paths64 res = new Paths64(count);
      for (int i = 0; i < count; i++) res.Add(null!);
      BulkOps.For(count, i => res[i] = ScalePath64(paths[i], scale));
      return res;
    }

    public static PathD ScalePathD(Path64 path, double scale)
    {
      int n = path.Count;
      PathD res = new PathD(n);
      Span<PointD> dst = BulkOps.GrowUninitialized(res, n);
      ReadOnlySpan<Point64> src = CollectionsMarshal.AsSpan(path);
      for (int i = 0; i < n; i++)
        dst[i] = new PointD(src[i], scale);
      return res;
    }

    public static PathsD ScalePathsD(Paths64 paths, double scale)
    {
      int count = paths.Count;
      PathsD res = new PathsD(count);
      for (int i = 0; i < count; i++) res.Add(null!);
      BulkOps.For(count, i => res[i] = ScalePathD(paths[i], scale));
      return res;
    }

    // The static functions Path64 and PathD convert path types without scaling
    public static Path64 Path64(PathD path)
    {
      int n = path.Count;
      Path64 result = new Path64(n);
      Span<Point64> dst = BulkOps.GrowUninitialized(result, n);
      ReadOnlySpan<PointD> src = CollectionsMarshal.AsSpan(path);
      for (int i = 0; i < n; i++)
        dst[i] = new Point64(src[i]);
      return result;
    }

    public static Paths64 Paths64(PathsD paths)
    {
      int count = paths.Count;
      Paths64 result = new Paths64(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = Path64(paths[i]));
      return result;
    }

    public static PathsD PathsD(Paths64 paths)
    {
      int count = paths.Count;
      PathsD result = new PathsD(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = PathD(paths[i]));
      return result;
    }

    public static PathD PathD(Path64 path)
    {
      int n = path.Count;
      PathD result = new PathD(n);
      Span<PointD> dst = BulkOps.GrowUninitialized(result, n);
      ReadOnlySpan<Point64> src = CollectionsMarshal.AsSpan(path);
      for (int i = 0; i < n; i++)
        dst[i] = new PointD(src[i]);
      return result;
    }

    public static Path64 TranslatePath(Path64 path, long dx, long dy)
    {
      Path64 result = new Path64(path.Count);
      foreach (Point64 pt in path)
        result.Add(new Point64(pt.X + dx, pt.Y + dy));
      return result;
    }

    public static Paths64 TranslatePaths(Paths64 paths, long dx, long dy)
    {
      int count = paths.Count;
      Paths64 result = new Paths64(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = OffsetPath(paths[i], dx, dy));
      return result;
    }

    public static PathD TranslatePath(PathD path, double dx, double dy)
    {
      PathD result = new PathD(path.Count);
      foreach (PointD pt in path)
        result.Add(new PointD(pt.x + dx, pt.y + dy));
      return result;
    }

    public static PathsD TranslatePaths(PathsD paths, double dx, double dy)
    {
      int count = paths.Count;
      PathsD result = new PathsD(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = TranslatePath(paths[i], dx, dy));
      return result;
    }

    public static Path64 ReversePath(Path64 path)
    {
      Path64 result = new Path64(path);
      result.Reverse();
      return result;
    }

    public static PathD ReversePath(PathD path)
    {
      PathD result = new PathD(path);
      result.Reverse();
      return result;
    }

    public static Paths64 ReversePaths(Paths64 paths)
    {
      int count = paths.Count;
      Paths64 result = new Paths64(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = ReversePath(paths[i]));
      return result;
    }

    public static PathsD ReversePaths(PathsD paths)
    {
      int count = paths.Count;
      PathsD result = new PathsD(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = ReversePath(paths[i]));
      return result;
    }

    // Bounds --------------------------------------------------------------------

    public static Rect64 GetBounds(Path64 path)
    {
      if (path.Count == 0) return new Rect64();
      return InternalClipper.GetBounds(path);
    }

    public static Rect64 GetBounds(Paths64 paths)
    {
      if (paths.Count == 0) return new Rect64();
      Rect64 result = InternalClipper.GetBounds(paths);
      return result.left == long.MaxValue ? new Rect64() : result;
    }

    public static RectD GetBounds(PathD path)
    {
      if (path.Count == 0) return new RectD();
      RectD result = InternalClipper.GetBounds(path);
      return Math.Abs(result.left - double.MaxValue) < InternalClipper.floatingPointTolerance
        ? new RectD() : result;
    }

    public static RectD GetBounds(PathsD paths)
    {
      if (paths.Count == 0) return new RectD();
      RectD result = InternalClipper.GetBounds(paths);
      return Math.Abs(result.left - double.MaxValue) < InternalClipper.floatingPointTolerance
        ? new RectD() : result;
    }

    // Path construction ---------------------------------------------------------

    public static Path64 MakePath(int[] arr)
    {
      int len = arr.Length / 2;
      Path64 p = new Path64(len);
      for (int i = 0; i < len; i++)
        p.Add(new Point64(arr[i * 2], arr[i * 2 + 1]));
      return p;
    }

    public static Path64 MakePath(long[] arr)
    {
      int len = arr.Length / 2;
      Path64 p = new Path64(len);
      for (int i = 0; i < len; i++)
        p.Add(new Point64(arr[i * 2], arr[i * 2 + 1]));
      return p;
    }

    public static PathD MakePath(double[] arr)
    {
      int len = arr.Length / 2;
      PathD p = new PathD(len);
      for (int i = 0; i < len; i++)
        p.Add(new PointD(arr[i * 2], arr[i * 2 + 1]));
      return p;
    }

#if USINGZ
    public static Path64 MakePathZ(long[] arr)
    {
      int len = arr.Length / 3;
      Path64 p = new Path64(len);
      for (int i = 0; i < len; i++)
        p.Add(new Point64(arr[i * 3], arr[i * 3 + 1], arr[i * 3 + 2]));
      return p;
    }

    public static PathD MakePathZ(double[] arr)
    {
      int len = arr.Length / 3;
      PathD p = new PathD(len);
      for (int i = 0; i < len; i++)
        p.Add(new PointD(arr[i * 3], arr[i * 3 + 1], (long) arr[i * 3 + 2]));
      return p;
    }
#endif

    // Miscellaneous -------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sqr(double val) => val * val;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sqr(long val) => (double) val * (double) val;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DistanceSqr(Point64 pt1, Point64 pt2)
    {
      return Sqr(pt1.X - pt2.X) + Sqr(pt1.Y - pt2.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DistanceSqr(PointD pt1, PointD pt2)
    {
      return Sqr(pt1.x - pt2.x) + Sqr(pt1.y - pt2.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(Point64 pt1, Point64 pt2)
    {
      return Math.Sqrt(DistanceSqr(pt1, pt2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(PointD pt1, PointD pt2)
    {
      return Math.Sqrt(DistanceSqr(pt1, pt2));
    }

    /// <summary>Returns the length of the (optionally closed) path.</summary>
    public static double Length(Path64 path, bool isClosedPath = false)
    {
      double result = 0.0;
      if (path.Count < 2) return result;
      int stop = path.Count - 1;
      for (int i = 0; i < stop; i++)
        result += Distance(path[i], path[i + 1]);
      if (isClosedPath)
        result += Distance(path[stop], path[0]);
      return result;
    }

    public static double Length(PathD path, bool isClosedPath = false)
    {
      double result = 0.0;
      if (path.Count < 2) return result;
      int stop = path.Count - 1;
      for (int i = 0; i < stop; i++)
        result += Distance(path[i], path[i + 1]);
      if (isClosedPath)
        result += Distance(path[stop], path[0]);
      return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Point64 MidPoint(Point64 pt1, Point64 pt2)
    {
      return new Point64((pt1.X + pt2.X) / 2, (pt1.Y + pt2.Y) / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PointD MidPoint(PointD pt1, PointD pt2)
    {
      return new PointD((pt1.x + pt2.x) / 2, (pt1.y + pt2.y) / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InflateRect(ref Rect64 rec, int dx, int dy)
    {
      rec.left -= dx;
      rec.right += dx;
      rec.top -= dy;
      rec.bottom += dy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InflateRect(ref RectD rec, double dx, double dy)
    {
      rec.left -= dx;
      rec.right += dx;
      rec.top -= dy;
      rec.bottom += dy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool PointsNearEqual(PointD pt1, PointD pt2, double distanceSqrd)
    {
      return Sqr(pt1.x - pt2.x) + Sqr(pt1.y - pt2.y) < distanceSqrd;
    }

    public static PathD StripNearDuplicates(PathD path,
        double minEdgeLenSqrd, bool isClosedPath)
    {
      int cnt = path.Count;
      PathD result = new PathD(cnt);
      if (cnt == 0) return result;
      PointD lastPt = path[0];
      result.Add(lastPt);
      for (int i = 1; i < cnt; i++)
        if (!PointsNearEqual(lastPt, path[i], minEdgeLenSqrd))
        {
          lastPt = path[i];
          result.Add(lastPt);
        }

      if (isClosedPath && PointsNearEqual(lastPt, result[0], minEdgeLenSqrd))
      {
        result.RemoveAt(result.Count - 1);
      }

      return result;
    }

    public static Path64 StripDuplicates(Path64 path, bool isClosedPath)
    {
      int cnt = path.Count;
      Path64 result = new Path64(cnt);
      if (cnt == 0) return result;
      Point64 lastPt = path[0];
      result.Add(lastPt);
      for (int i = 1; i < cnt; i++)
        if (lastPt != path[i])
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      if (isClosedPath && lastPt == result[0])
        result.RemoveAt(result.Count - 1);
      return result;
    }

    public static Paths64 StripDuplicates(Paths64 paths, bool isClosedPaths)
    {
      // in-place, and every path is independent
      BulkOps.For(paths.Count, i => StripDuplicates(paths[i], isClosedPaths));
      return paths;
    }

    // PolyTree ------------------------------------------------------------------

    private static void AddPolyNodeToPaths(PolyPath64 polyPath, Paths64 paths)
    {
      if (polyPath.Polygon != null && polyPath.Polygon.Count > 0)
        paths.Add(polyPath.Polygon);
      for (int i = 0; i < polyPath.Count; i++)
        AddPolyNodeToPaths(polyPath[i], paths);
    }

    public static Paths64 PolyTreeToPaths64(PolyTree64 polyTree)
    {
      Paths64 result = new Paths64();
      for (int i = 0; i < polyTree.Count; i++)
        AddPolyNodeToPaths(polyTree[i], result);
      return result;
    }

    public static void AddPolyNodeToPathsD(PolyPathD polyPath, PathsD paths)
    {
      if (polyPath.Polygon != null && polyPath.Polygon.Count > 0)
        paths.Add(polyPath.Polygon);
      for (int i = 0; i < polyPath.Count; i++)
        AddPolyNodeToPathsD(polyPath[i], paths);
    }

    public static PathsD PolyTreeToPathsD(PolyTreeD polyTree)
    {
      PathsD result = new PathsD();
      for (int i = 0; i < polyTree.Count; i++)
        AddPolyNodeToPathsD(polyTree[i], result);
      return result;
    }

    private static bool PolyPath64ContainsChildren(PolyPath64 pp)
    {
      for (int i = 0; i < pp.Count; i++)
      {
        PolyPath64 child = pp[i];
        // return false if this child isn't fully contained by its parent

        // checking for a single vertex outside is a bit too crude since
        // it doesn't account for rounding errors. It's better to check
        // for consecutive vertices found outside the parent's polygon.

        int outsideCnt = 0;
        foreach (Point64 pt in child.Polygon!)
        {
          PointInPolygonResult result = InternalClipper.PointInPolygon(pt, pp.Polygon!);
          if (result == PointInPolygonResult.IsInside) --outsideCnt;
          else if (result == PointInPolygonResult.IsOutside) ++outsideCnt;
          if (outsideCnt > 1) return false;
          else if (outsideCnt < -1) break;
        }

        // now check any nested children too
        if (child.Count > 0 && !PolyPath64ContainsChildren(child))
          return false;
      }
      return true;
    }

    public static bool CheckPolytreeFullyContainsChildren(PolyTree64 polytree)
    {
      for (int i = 0; i < polytree.Count; i++)
      {
        PolyPath64 child = polytree[i];
        if (child.Count > 0 && !PolyPath64ContainsChildren(child))
          return false;
      }
      return true;
    }

    private static void ShowPolyPathStructure(PolyPath64 pp, int level)
    {
      string spaces = new string(' ', level * 2);
      string caption = (pp.IsHole ? "Hole " : "Outer ");
      if (pp.Count == 0)
      {
        Console.WriteLine(spaces + caption);
      }
      else
      {
        Console.WriteLine(spaces + caption + $"({pp.Count})");
        foreach (PolyPathBase child in pp) { ShowPolyPathStructure((PolyPath64) child, level + 1); }
      }
    }

    public static void ShowPolyTreeStructure(PolyTree64 polytree)
    {
      Console.WriteLine("Polytree Root");
      for (int i = 0; i < polytree.Count; i++)
        ShowPolyPathStructure(polytree[i], 1);
    }

    private static void ShowPolyPathStructure(PolyPathD pp, int level)
    {
      string spaces = new string(' ', level * 2);
      string caption = (pp.IsHole ? "Hole " : "Outer ");
      if (pp.Count == 0)
      {
        Console.WriteLine(spaces + caption);
      }
      else
      {
        Console.WriteLine(spaces + caption + $"({pp.Count})");
        foreach (PolyPathBase child in pp) { ShowPolyPathStructure((PolyPathD) child, level + 1); }
      }
    }

    public static void ShowPolyTreeStructure(PolyTreeD polytree)
    {
      Console.WriteLine("Polytree Root");
      for (int i = 0; i < polytree.Count; i++)
        ShowPolyPathStructure(polytree[i], 1);
    }

    // Geo calculations ----------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double PerpendicDistFromLineSqrd(PointD pt, PointD line1, PointD line2)
    {
      double a = pt.x - line1.x;
      double b = pt.y - line1.y;
      double c = line2.x - line1.x;
      double d = line2.y - line1.y;
      if (c == 0 && d == 0) return 0;
      return Sqr(a * d - c * b) / (c * c + d * d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double PerpendicDistFromLineSqrd(Point64 pt, Point64 line1, Point64 line2)
    {
      double a = (double) pt.X - line1.X;
      double b = (double) pt.Y - line1.Y;
      double c = (double) line2.X - line1.X;
      double d = (double) line2.Y - line1.Y;
      if (c == 0 && d == 0) return 0;
      return Sqr(a * d - c * b) / (c * c + d * d);
    }

    /// <summary>
    /// Returns true if the angle formed by pt1-pt2-pt3 is near-collinear
    /// (C++ NearCollinear).
    /// </summary>
    public static bool NearCollinear(Point64 pt1, Point64 pt2, Point64 pt3,
      double sinSqrdMinAngleRads)
    {
      double cp = Math.Abs(InternalClipper.CrossProduct(pt1, pt2, pt3));
      return (cp * cp) / (DistanceSqr(pt1, pt2) * DistanceSqr(pt2, pt3)) < sinSqrdMinAngleRads;
    }

    public static bool NearCollinear(PointD pt1, PointD pt2, PointD pt3,
      double sinSqrdMinAngleRads)
    {
      double cp = Math.Abs(InternalClipper.CrossProduct(
        new PointD(pt2.x - pt1.x, pt2.y - pt1.y),
        new PointD(pt3.x - pt2.x, pt3.y - pt2.y)));
      return (cp * cp) / (DistanceSqr(pt1, pt2) * DistanceSqr(pt2, pt3)) < sinSqrdMinAngleRads;
    }

    public static PointInPolygonResult PointInPolygon(Point64 pt, Path64 polygon)
    {
      return InternalClipper.PointInPolygon(pt, polygon);
    }

    public static PointInPolygonResult PointInPolygon(PointD pt,
      PathD polygon, int precision = 2)
    {
      InternalClipper.CheckPrecision(precision);
      double scale = Math.Pow(10, precision);
      Point64 p = new Point64(pt, scale);
      Path64 path = ScalePath64(polygon, scale);
      return InternalClipper.PointInPolygon(p, path);
    }

    /// <summary>
    /// Returns true when path2 contains path1 (C++ Path2ContainsPath1).
    /// </summary>
    public static bool Path2ContainsPath1(Path64 path1, Path64 path2)
    {
      return InternalClipper.Path2ContainsPath1(path1, path2);
    }

    public static bool Path2ContainsPath1(PathD path1, PathD path2)
    {
      return InternalClipper.Path2ContainsPath1(path1, path2);
    }

    // Ellipse -------------------------------------------------------------------

    public static Path64 Ellipse(Rect64 rect, int steps = 0)
    {
      return Ellipse(rect.MidPoint(),
        (double) rect.Width * 0.5,
        (double) rect.Height * 0.5, steps);
    }

    public static PathD Ellipse(RectD rect, int steps = 0)
    {
      return Ellipse(rect.MidPoint(),
        rect.Width * 0.5,
        rect.Height * 0.5, steps);
    }

    public static Path64 Ellipse(Point64 center,
      double radiusX, double radiusY = 0, int steps = 0)
    {
      if (radiusX <= 0) return new Path64();
      if (radiusY <= 0) radiusY = radiusX;
      if (steps <= 2)
        steps = (int) (InternalClipper.PI * Math.Sqrt((radiusX + radiusY) / 2));

      double si = Math.Sin(2 * InternalClipper.PI / steps);
      double co = Math.Cos(2 * InternalClipper.PI / steps);
      double dx = co, dy = si;
      Path64 result = new Path64(steps) { new Point64(center.X + radiusX, center.Y) };
      for (int i = 1; i < steps; ++i)
      {
        result.Add(new Point64(center.X + radiusX * dx, center.Y + radiusY * dy));
        double x = dx * co - dy * si;
        dy = dy * co + dx * si;
        dx = x;
      }
      return result;
    }

    public static PathD Ellipse(PointD center,
      double radiusX, double radiusY = 0, int steps = 0)
    {
      if (radiusX <= 0) return new PathD();
      if (radiusY <= 0) radiusY = radiusX;
      if (steps <= 2)
        steps = (int) (InternalClipper.PI * Math.Sqrt((radiusX + radiusY) / 2));

      double si = Math.Sin(2 * InternalClipper.PI / steps);
      double co = Math.Cos(2 * InternalClipper.PI / steps);
      double dx = co, dy = si;
      PathD result = new PathD(steps) { new PointD(center.x + radiusX, center.y) };
      for (int i = 1; i < steps; ++i)
      {
        result.Add(new PointD(center.x + radiusX * dx, center.y + radiusY * dy));
        double x = dx * co - dy * si;
        dy = dy * co + dx * si;
        dx = x;
      }
      return result;
    }

    // Ramer-Douglas-Peucker -----------------------------------------------------

    internal static void RDP(Path64 path, int begin, int end, double epsSqrd, List<bool> flags)
    {
      while (true)
      {
        int idx = 0;
        double maxD = 0;
        while (end > begin && path[begin] == path[end]) flags[end--] = false;
        for (int i = begin + 1; i < end; ++i)
        {
          // PerpendicDistFromLineSqrd - avoids expensive Sqrt()
          double d = PerpendicDistFromLineSqrd(path[i], path[begin], path[end]);
          if (d <= maxD) continue;
          maxD = d;
          idx = i;
        }

        if (maxD <= epsSqrd) return;
        flags[idx] = true;
        if (idx > begin + 1) RDP(path, begin, idx, epsSqrd, flags);
        if (idx < end - 1)
        {
          begin = idx;
          continue;
        }

        break;
      }
    }

    public static Path64 RamerDouglasPeucker(Path64 path, double epsilon)
    {
      int len = path.Count;
      if (len < 5) return path;
      List<bool> flags = new List<bool>(new bool[len]);
      flags[0] = true;
      flags[len - 1] = true;
      RDP(path, 0, len - 1, Sqr(epsilon), flags);
      Path64 result = new Path64(len);
      for (int i = 0; i < len; ++i)
        if (flags[i]) result.Add(path[i]);
      return result;
    }

    public static Paths64 RamerDouglasPeucker(Paths64 paths, double epsilon)
    {
      Paths64 result = new Paths64(paths.Count);
      foreach (Path64 path in paths)
        result.Add(RamerDouglasPeucker(path, epsilon));
      return result;
    }

    internal static void RDP(PathD path, int begin, int end, double epsSqrd, List<bool> flags)
    {
      while (true)
      {
        int idx = 0;
        double maxD = 0;
        while (end > begin && path[begin] == path[end]) flags[end--] = false;
        for (int i = begin + 1; i < end; ++i)
        {
          // PerpendicDistFromLineSqrd - avoids expensive Sqrt()
          double d = PerpendicDistFromLineSqrd(path[i], path[begin], path[end]);
          if (d <= maxD) continue;
          maxD = d;
          idx = i;
        }

        if (maxD <= epsSqrd) return;
        flags[idx] = true;
        if (idx > begin + 1) RDP(path, begin, idx, epsSqrd, flags);
        if (idx < end - 1)
        {
          begin = idx;
          continue;
        }

        break;
      }
    }

    public static PathD RamerDouglasPeucker(PathD path, double epsilon)
    {
      int len = path.Count;
      if (len < 5) return path;
      List<bool> flags = new List<bool>(new bool[len]);
      flags[0] = true;
      flags[len - 1] = true;
      RDP(path, 0, len - 1, Sqr(epsilon), flags);
      PathD result = new PathD(len);
      for (int i = 0; i < len; ++i)
        if (flags[i]) result.Add(path[i]);
      return result;
    }

    public static PathsD RamerDouglasPeucker(PathsD paths, double epsilon)
    {
      PathsD result = new PathsD(paths.Count);
      foreach (PathD path in paths)
        result.Add(RamerDouglasPeucker(path, epsilon));
      return result;
    }

    // SimplifyPath --------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetNext(int current, int high, Span<bool> flags)
    {
      ++current;
      while (current <= high && flags[current]) ++current;
      if (current <= high) return current;
      current = 0;
      while (flags[current]) ++current;
      return current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPrior(int current, int high, Span<bool> flags)
    {
      if (current == 0) current = high;
      else --current;
      while (current > 0 && flags[current]) --current;
      if (!flags[current]) return current;
      current = high;
      while (flags[current]) --current;
      return current;
    }

    public static Path64 SimplifyPath(Path64 path,
      double epsilon, bool isClosedPath = true)
    {
      int len = path.Count, high = len - 1;
      double epsSqr = Sqr(epsilon);
      if (len < 4) return path;

      // nb: the scratch buffers come from the array pool (no garbage per call)
      using BulkOps.Scratch<bool> flagScratch = new BulkOps.Scratch<bool>();
      using BulkOps.Scratch<double> dsqScratch = new BulkOps.Scratch<double>();
      Span<bool> flags = flagScratch.Rent(len);
      // nb: every element of 'dsq' is assigned below, so it needs no clearing
      Span<double> dsq = dsqScratch.RentDirty(len);
      int curr = 0;

      if (isClosedPath)
      {
        dsq[0] = PerpendicDistFromLineSqrd(path[0], path[high], path[1]);
        dsq[high] = PerpendicDistFromLineSqrd(path[high], path[0], path[high - 1]);
      }
      else
      {
        dsq[0] = double.MaxValue;
        dsq[high] = double.MaxValue;
      }

      for (int i = 1; i < high; ++i)
        dsq[i] = PerpendicDistFromLineSqrd(path[i], path[i - 1], path[i + 1]);

      for (; ; )
      {
        if (dsq[curr] > epsSqr)
        {
          int start = curr;
          do
          {
            curr = GetNext(curr, high, flags);
          } while (curr != start && dsq[curr] > epsSqr);
          if (curr == start) break;
        }

        int prev = GetPrior(curr, high, flags);
        int next = GetNext(curr, high, flags);
        if (next == prev) break;

        int prior2;
        if (dsq[next] < dsq[curr])
        {
          prior2 = prev;
          prev = curr;
          curr = next;
          next = GetNext(next, high, flags);
        }
        else
          prior2 = GetPrior(prev, high, flags);

        flags[curr] = true;
        curr = next;
        next = GetNext(next, high, flags);
        if (isClosedPath || ((curr != high) && (curr != 0)))
          dsq[curr] = PerpendicDistFromLineSqrd(path[curr], path[prev], path[next]);
        if (isClosedPath || ((prev != 0) && (prev != high)))
          dsq[prev] = PerpendicDistFromLineSqrd(path[prev], path[prior2], path[curr]);
      }
      Path64 result = new Path64(len);
      for (int i = 0; i < len; i++)
        if (!flags[i]) result.Add(path[i]);
      return result;
    }

    public static Paths64 SimplifyPaths(Paths64 paths,
      double epsilon, bool isClosedPaths = true)
    {
      int count = paths.Count;
      Paths64 result = new Paths64(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = SimplifyPath(paths[i], epsilon, isClosedPaths));
      return result;
    }

    public static PathD SimplifyPath(PathD path,
      double epsilon, bool isClosedPath = true)
    {
      int len = path.Count, high = len - 1;
      double epsSqr = Sqr(epsilon);
      if (len < 4) return path;

      using BulkOps.Scratch<bool> flagScratch = new BulkOps.Scratch<bool>();
      using BulkOps.Scratch<double> dsqScratch = new BulkOps.Scratch<double>();
      Span<bool> flags = flagScratch.Rent(len);
      // nb: every element of 'dsq' is assigned below, so it needs no clearing
      Span<double> dsq = dsqScratch.RentDirty(len);
      int curr = 0;
      if (isClosedPath)
      {
        dsq[0] = PerpendicDistFromLineSqrd(path[0], path[high], path[1]);
        dsq[high] = PerpendicDistFromLineSqrd(path[high], path[0], path[high - 1]);
      }
      else
      {
        dsq[0] = double.MaxValue;
        dsq[high] = double.MaxValue;
      }
      for (int i = 1; i < high; ++i)
        dsq[i] = PerpendicDistFromLineSqrd(path[i], path[i - 1], path[i + 1]);

      for (; ; )
      {
        if (dsq[curr] > epsSqr)
        {
          int start = curr;
          do
          {
            curr = GetNext(curr, high, flags);
          } while (curr != start && dsq[curr] > epsSqr);
          if (curr == start) break;
        }

        int prev = GetPrior(curr, high, flags);
        int next = GetNext(curr, high, flags);
        if (next == prev) break;

        int prior2;
        if (dsq[next] < dsq[curr])
        {
          prior2 = prev;
          prev = curr;
          curr = next;
          next = GetNext(next, high, flags);
        }
        else
          prior2 = GetPrior(prev, high, flags);

        flags[curr] = true;
        curr = next;
        next = GetNext(next, high, flags);
        if (isClosedPath || ((curr != high) && (curr != 0)))
          dsq[curr] = PerpendicDistFromLineSqrd(path[curr], path[prev], path[next]);
        if (isClosedPath || ((prev != 0) && (prev != high)))
          dsq[prev] = PerpendicDistFromLineSqrd(path[prev], path[prior2], path[curr]);
      }
      PathD result = new PathD(len);
      for (int i = 0; i < len; i++)
        if (!flags[i]) result.Add(path[i]);
      return result;
    }

    public static PathsD SimplifyPaths(PathsD paths,
      double epsilon, bool isClosedPath = true)
    {
      int count = paths.Count;
      PathsD result = new PathsD(count);
      for (int i = 0; i < count; i++) result.Add(null!);
      BulkOps.For(count, i => result[i] = SimplifyPath(paths[i], epsilon, isClosedPath));
      return result;
    }

    // TrimCollinear -------------------------------------------------------------

    public static Path64 TrimCollinear(Path64 p, bool isOpenPath = false)
    {
      int len = p.Count;
      if (len < 3)
      {
        if (!isOpenPath || len < 2 || p[0] == p[1]) return new Path64();
        else return p;
      }

      Path64 dst = new Path64(len);
      int srcI = 0, prevI, stop = len - 1;

      if (!isOpenPath)
      {
        while (srcI != stop && InternalClipper.IsCollinear(p[stop], p[srcI], p[srcI + 1]))
          ++srcI;
        while (srcI != stop && InternalClipper.IsCollinear(p[stop - 1], p[stop], p[srcI]))
          --stop;
        if (srcI == stop) return new Path64();
      }

      prevI = srcI++;
      dst.Add(p[prevI]);
      for (; srcI != stop; ++srcI)
      {
        if (!InternalClipper.IsCollinear(p[prevI], p[srcI], p[srcI + 1]))
        {
          prevI = srcI;
          dst.Add(p[prevI]);
        }
      }

      if (isOpenPath)
        dst.Add(p[srcI]);
      else if (!InternalClipper.IsCollinear(p[prevI], p[stop], dst[0]))
        dst.Add(p[stop]);
      else
      {
        while (dst.Count > 2 &&
          InternalClipper.IsCollinear(dst[dst.Count - 1], dst[dst.Count - 2], dst[0]))
          dst.RemoveAt(dst.Count - 1);
        if (dst.Count < 3) return new Path64();
      }
      return dst;
    }

    public static PathD TrimCollinear(PathD path, int precision, bool isOpenPath = false)
    {
      int errorCode = 0;
      InternalClipper.CheckPrecisionRange(ref precision, ref errorCode);
      if (errorCode != 0) return new PathD();
      double scale = Math.Pow(10, precision);
      Path64 p = ScalePath64(path, scale);
      p = TrimCollinear(p, isOpenPath);
      return ScalePathD(p, 1 / scale);
    }

    // Triangulation -------------------------------------------------------------

    public static TriangulateResult Triangulate(Paths64 pp, out Paths64 solution,
      bool useDelaunay = true)
    {
      Delaunay d = new Delaunay(useDelaunay);
      return d.Execute(pp, out solution);
    }

    public static TriangulateResult Triangulate(PathsD pp, int decPlaces,
      out PathsD solution, bool useDelaunay = true)
    {
      double scale;
      if (decPlaces <= 0) scale = 1.0;
      else if (decPlaces > 8) scale = Math.Pow(10.0, 8.0);
      else scale = Math.Pow(10.0, decPlaces);

      Paths64 pp64 = ScalePaths64(pp, scale);

      Delaunay d = new Delaunay(useDelaunay);
      TriangulateResult result = d.Execute(pp64, out Paths64 sol64);
      if (result == TriangulateResult.success)
        solution = ScalePathsD(sol64, 1.0 / scale);
      else
        solution = new PathsD();
      return result;
    }
  }
}
