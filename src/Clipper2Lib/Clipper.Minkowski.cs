/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  1 November 2023                                                 *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2023                                         *
* Purpose   :  Minkowski Sum and Difference                                    *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.minkowski.h (Clipper2 ver. 2.0.1)                *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  public static class Minkowski
  {
    /// <summary>
    /// The union of the quadrilaterals swept by the pattern along the path.
    /// The C++ builds a pathLen x patLen matrix of translated patterns, then one
    /// Path64 per quadrilateral, then unions them. Here every quad is built in
    /// one reused 4 point path and handed straight to the engine, which copies
    /// it into its vertices at once: the engine receives the very same paths in
    /// the same order (adding paths one by one gives exactly the vertices and
    /// local minima of adding them as one list), so the result is identical and
    /// only the result itself is allocated.
    /// </summary>
    private static Paths64 MinkowskiUnion(Path64 pattern, Path64 path,
      bool isSum, bool isClosed)
    {
      int delta = isClosed ? 0 : 1;
      int patLen = pattern.Count, pathLen = path.Count;
      Paths64 solution = new Paths64();
      if (patLen == 0 || pathLen == 0) return solution;

      ReadOnlySpan<Point64> pat = CollectionsMarshal.AsSpan(pattern);
      ReadOnlySpan<Point64> pts = CollectionsMarshal.AsSpan(path);
      Path64 quad = new Path64(4);
      Span<Point64> q = BulkOps.GrowUninitialized(quad, 4);
      Clipper64 c = Clipper64.RentShared();
      try
      {
        c.ReserveVertices(4 * (pathLen - delta) * patLen);
        int g = isClosed ? pathLen - 1 : 0;
        int h = patLen - 1;
        for (int i = delta; i < pathLen; ++i)
        {
          Point64 pg = pts[g], pi = pts[i];
          for (int j = 0; j < patLen; j++)
          {
            // tmp[x][y] of the C++ is path[x] + (or -) pattern[y]
            if (isSum)
            {
              q[0] = pg + pat[h];
              q[1] = pi + pat[h];
              q[2] = pi + pat[j];
              q[3] = pg + pat[j];
            }
            else
            {
              q[0] = pg - pat[h];
              q[1] = pi - pat[h];
              q[2] = pi - pat[j];
              q[3] = pg - pat[j];
            }
            if (!Clipper.IsPositive(quad))
              q.Reverse();
            c.AddPathDirect(quad, PathType.Subject);
            h = j;
          }
          g = i;
        }
        c.Execute(ClipType.Union, FillRule.NonZero, solution);
      }
      finally
      {
        Clipper64.ReturnShared(c);
      }
      return solution;
    }

    public static Paths64 Sum(Path64 pattern, Path64 path, bool isClosed)
    {
      return MinkowskiUnion(pattern, path, true, isClosed);
    }

    public static PathsD Sum(PathD pattern, PathD path, bool isClosed, int decimalPlaces = 2)
    {
      double scale = Math.Pow(10, decimalPlaces);
      Paths64 tmp = MinkowskiUnion(Clipper.ScalePath64(pattern, scale),
        Clipper.ScalePath64(path, scale), true, isClosed);
      return Clipper.ScalePathsD(tmp, 1 / scale);
    }

    public static Paths64 Diff(Path64 pattern, Path64 path, bool isClosed)
    {
      return MinkowskiUnion(pattern, path, false, isClosed);
    }

    public static PathsD Diff(PathD pattern, PathD path, bool isClosed, int decimalPlaces = 2)
    {
      double scale = Math.Pow(10, decimalPlaces);
      Paths64 tmp = MinkowskiUnion(Clipper.ScalePath64(pattern, scale),
        Clipper.ScalePath64(path, scale), false, isClosed);
      return Clipper.ScalePathsD(tmp, 1 / scale);
    }
  }
}
