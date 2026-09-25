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
    private static Paths64 MinkowskiInternal(Path64 pattern, Path64 path,
      bool isSum, bool isClosed)
    {
      int delta = isClosed ? 0 : 1;
      int patLen = pattern.Count, pathLen = path.Count;
      if (patLen == 0 || pathLen == 0) return new Paths64();
      Paths64 tmp = new Paths64(pathLen);
      for (int i = 0; i < pathLen; i++) tmp.Add(null!);

      if (isSum)
      {
        BulkOps.For(pathLen, i =>
        {
          Point64 p = path[i];
          Path64 path2 = new Path64(patLen);
          Span<Point64> dst = BulkOps.GrowUninitialized(path2, patLen);
          ReadOnlySpan<Point64> pat = CollectionsMarshal.AsSpan(pattern);
          for (int j = 0; j < patLen; j++) dst[j] = p + pat[j];
          tmp[i] = path2;
        });
      }
      else
      {
        BulkOps.For(pathLen, i =>
        {
          Point64 p = path[i];
          Path64 path2 = new Path64(patLen);
          Span<Point64> dst = BulkOps.GrowUninitialized(path2, patLen);
          ReadOnlySpan<Point64> pat = CollectionsMarshal.AsSpan(pattern);
          for (int j = 0; j < patLen; j++) dst[j] = p - pat[j];
          tmp[i] = path2;
        });
      }

      Paths64 result = new Paths64();
      result.Capacity = (pathLen - delta) * patLen;
      int g = isClosed ? pathLen - 1 : 0;
      int h = patLen - 1;
      for (int i = delta; i < pathLen; ++i)
      {
        for (int j = 0; j < patLen; j++)
        {
          Path64 quad = new Path64(4)
          {
            tmp[g][h],
            tmp[i][h],
            tmp[i][j],
            tmp[g][j]
          };
          if (!Clipper.IsPositive(quad))
            quad.Reverse();
          result.Add(quad);
          h = j;
        }
        g = i;
      }
      return result;
    }

    public static Paths64 Sum(Path64 pattern, Path64 path, bool isClosed)
    {
      return Clipper.Union(MinkowskiInternal(pattern, path, true, isClosed), FillRule.NonZero);
    }

    public static PathsD Sum(PathD pattern, PathD path, bool isClosed, int decimalPlaces = 2)
    {
      double scale = Math.Pow(10, decimalPlaces);
      Paths64 tmp = Clipper.Union(
        MinkowskiInternal(Clipper.ScalePath64(pattern, scale),
          Clipper.ScalePath64(path, scale), true, isClosed), FillRule.NonZero);
      return Clipper.ScalePathsD(tmp, 1 / scale);
    }

    public static Paths64 Diff(Path64 pattern, Path64 path, bool isClosed)
    {
      return Clipper.Union(MinkowskiInternal(pattern, path, false, isClosed), FillRule.NonZero);
    }

    public static PathsD Diff(PathD pattern, PathD path, bool isClosed, int decimalPlaces = 2)
    {
      double scale = Math.Pow(10, decimalPlaces);
      Paths64 tmp = Clipper.Union(
        MinkowskiInternal(Clipper.ScalePath64(pattern, scale),
          Clipper.ScalePath64(path, scale), false, isClosed), FillRule.NonZero);
      return Clipper.ScalePathsD(tmp, 1 / scale);
    }
  }
}
