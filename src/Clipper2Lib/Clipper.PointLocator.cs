/*******************************************************************************
* Purpose   :  Repeated point-in-polygon queries against one polygon            *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* PointInPolygon scans every vertex of the polygon for every query. When the   *
* same polygon is queried many times, the scan can be restricted to the edges  *
* whose Y range contains the query's Y, after a bounding box rejection. The    *
* locator below gives exactly the answers of InternalClipper.PointInPolygon     *
* (docs/optimization-plan.md 5.3):                                             *
*                                                                              *
*  * that scan visits every vertex lying on the query's horizontal line and    *
*    every edge whose end lies on the other side of the line than the last     *
*    vertex off the line before it (vertices on the line keep the side of the  *
*    vertex before them). Each visit can only report 'on' or toggle the        *
*    crossing parity, so the result does not depend on the order of the        *
*    visits - which is what allows visiting just the edges of a Y bucket;      *
*  * an edge whose Y range excludes the query can never be visited, and an     *
*    edge that ends on the line after a run of vertices on the line needs the  *
*    side of the last vertex off the line, which is precomputed per vertex;    *
*  * outside the bounding box no edge can report 'on', and the crossings of a  *
*    closed boundary with a line come in pairs, so the answer is 'outside'.    *
*******************************************************************************/

#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  /// <summary>
  /// A polygon prepared for many point-in-polygon queries. Every query returns
  /// exactly what <see cref="Clipper.PointInPolygon(Point64, Path64)"/> returns
  /// for the polygon as it was when the locator was built.
  /// </summary>
  public sealed class PointInPolygonLocator
  {
    private struct EdgeRec
    {
      public Point64 prev;
      public Point64 curr;
      public long prevSideY; // Y of the last vertex before 'prev' whose Y differs from prev.Y
    }

    private readonly int _count;       // polygon vertex count
    private readonly long _minX, _maxX, _minY, _maxY;
    private readonly long _bucketHeight;
    private readonly int[] _bucketStart = Array.Empty<int>();
    private readonly EdgeRec[] _entries = Array.Empty<EdgeRec>();

    public PointInPolygonLocator(Path64 polygon)
    {
      _count = polygon.Count;
      if (_count < 3) return;
      ReadOnlySpan<Point64> p = CollectionsMarshal.AsSpan(polygon);
      int n = p.Length;

      _minX = long.MaxValue; _maxX = long.MinValue;
      _minY = long.MaxValue; _maxY = long.MinValue;
      foreach (Point64 pt in p)
      {
        if (pt.X < _minX) _minX = pt.X;
        if (pt.X > _maxX) _maxX = pt.X;
        if (pt.Y < _minY) _minY = pt.Y;
        if (pt.Y > _maxY) _maxY = pt.Y;
      }
      if (_minY == _maxY) return; // every vertex on one horizontal: always 'outside'

      // the side a vertex on the query line inherits: the Y of the nearest
      // previous vertex (cyclically) whose Y differs from its own
      long[] prevDiffY = new long[n];
      int s0 = 0;
      while (p[s0].Y == p[(s0 + n - 1) % n].Y) s0++;
      for (int k = 0; k < n; k++)
      {
        int i = (s0 + k) % n, im1 = (i + n - 1) % n;
        prevDiffY[i] = (p[i].Y != p[im1].Y) ? p[im1].Y : prevDiffY[im1];
      }

      // bucket the edges by Y (an edge goes into every bucket its closed Y range
      // overlaps); fewer buckets if long edges would make the table too large
      long span = _maxY - _minY + 1;
      int buckets = Math.Min(n, 1024);
      long total;
      for (; ; )
      {
        _bucketHeight = (span + buckets - 1) / buckets;
        total = 0;
        for (int i = 0; i < n; i++)
        {
          long y1 = p[(i + n - 1) % n].Y, y2 = p[i].Y;
          if (y1 > y2) (y1, y2) = (y2, y1);
          total += Bucket(y2) - Bucket(y1) + 1;
        }
        if (buckets == 1 || total <= 16L * n) break;
        buckets /= 2;
      }
      buckets = (int) ((span + _bucketHeight - 1) / _bucketHeight);

      _bucketStart = new int[buckets + 1];
      for (int i = 0; i < n; i++)
      {
        long y1 = p[(i + n - 1) % n].Y, y2 = p[i].Y;
        if (y1 > y2) (y1, y2) = (y2, y1);
        for (int b = Bucket(y1), e = Bucket(y2); b <= e; b++) _bucketStart[b + 1]++;
      }
      for (int b = 0; b < buckets; b++) _bucketStart[b + 1] += _bucketStart[b];
      _entries = new EdgeRec[_bucketStart[buckets]];
      int[] fill = new int[buckets];
      for (int i = 0; i < n; i++)
      {
        int im1 = (i + n - 1) % n;
        EdgeRec rec = new EdgeRec { prev = p[im1], curr = p[i], prevSideY = prevDiffY[im1] };
        long y1 = rec.prev.Y, y2 = rec.curr.Y;
        if (y1 > y2) (y1, y2) = (y2, y1);
        for (int b = Bucket(y1), e = Bucket(y2); b <= e; b++)
          _entries[_bucketStart[b] + fill[b]++] = rec;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Bucket(long y) => (int) ((y - _minY) / _bucketHeight);

    /// <summary>The number of polygon vertices the locator was built from.</summary>
    public int Count => _count;

    /// <summary>
    /// The polygon's bounding box (an invalid rectangle for fewer than 3
    /// vertices). Every point outside it is 'outside'.
    /// </summary>
    public Rect64 Bounds => _count < 3 ? Rect64.InvalidRect() : new Rect64(_minX, _minY, _maxX, _maxY);

    /// <summary>True when the point is inside the polygon or on its boundary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Point64 pt) => Locate(pt) != PointInPolygonResult.IsOutside;

    public PointInPolygonResult Locate(Point64 pt)
    {
      if (_count < 3 || _minY == _maxY ||
        pt.X < _minX || pt.X > _maxX || pt.Y < _minY || pt.Y > _maxY)
        return PointInPolygonResult.IsOutside;

      int b = Bucket(pt.Y);
      ReadOnlySpan<EdgeRec> edges = new ReadOnlySpan<EdgeRec>(_entries,
        _bucketStart[b], _bucketStart[b + 1] - _bucketStart[b]);
      long px = pt.X, py = pt.Y;
      int val = 0;
      foreach (ref readonly EdgeRec e in edges)
      {
        Point64 prev = e.prev, curr = e.curr;
        if (curr.Y == py)
        {
          if (curr.X == px || (prev.Y == py && ((px < prev.X) != (px < curr.X))))
            return PointInPolygonResult.IsOn;
          continue;
        }
        // an edge outside the query's Y range (it shares the bucket only)
        if ((prev.Y < py && curr.Y < py) || (prev.Y > py && curr.Y > py)) continue;

        // 'isAbove' is the side the scan is on when it reaches 'curr'
        bool isAbove = (prev.Y != py) ? prev.Y < py : e.prevSideY < py;
        if (isAbove == (curr.Y < py)) continue; // no side change: not visited

        if (px < curr.X && px < prev.X)
        {
          // crossing on the right of the point
        }
        else if (px > prev.X && px > curr.X)
          val = 1 - val;
        else
        {
          int d = InternalClipper.CrossProductSign(prev, curr, pt);
          if (d == 0) return PointInPolygonResult.IsOn;
          if ((d < 0) == isAbove) val = 1 - val;
        }
      }
      return val == 0 ? PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;
    }

    /// <summary>Locates every point (results[i] belongs to points[i]).</summary>
    public void Locate(ReadOnlySpan<Point64> points, Span<PointInPolygonResult> results)
    {
      if (results.Length < points.Length)
        throw new ArgumentException("results is shorter than points", nameof(results));
      int n = points.Length, i = 0;
      if (_count < 3 || _minY == _maxY)
      {
        results.Slice(0, n).Fill(PointInPolygonResult.IsOutside);
        return;
      }
#if !USINGZ
      // most probes of a large batch miss the polygon's bounding box, so the box
      // test runs on two points (four coordinates) per 256 bit compare
      if (Vector256.IsHardwareAccelerated)
      {
        Vector256<long> vmin = Vector256.Create(_minX, _minY, _minX, _minY);
        Vector256<long> vmax = Vector256.Create(_maxX, _maxY, _maxX, _maxY);
        ref long coords = ref Unsafe.As<Point64, long>(ref MemoryMarshal.GetReference(points));
        for (; i + 2 <= n; i += 2)
        {
          Vector256<long> v = Vector256.LoadUnsafe(ref coords, (nuint) (2 * i));
          uint outside = (Vector256.LessThan(v, vmin) | Vector256.GreaterThan(v, vmax))
            .ExtractMostSignificantBits();
          results[i] = (outside & 3) != 0 ? PointInPolygonResult.IsOutside : Locate(points[i]);
          results[i + 1] = (outside & 12) != 0 ? PointInPolygonResult.IsOutside : Locate(points[i + 1]);
        }
      }
#endif
      for (; i < n; i++) results[i] = Locate(points[i]);
    }
  }

  /// <summary>
  /// The PathD form of <see cref="PointInPolygonLocator"/>: every query returns
  /// exactly what <see cref="Clipper.PointInPolygon(PointD, PathD, int)"/> returns
  /// for the same precision (the polygon and the points are scaled by
  /// 10^precision and rounded the same way).
  /// </summary>
  public sealed class PointInPolygonLocatorD
  {
    private readonly PointInPolygonLocator _locator;
    private readonly double _scale;

    public PointInPolygonLocatorD(PathD polygon, int precision = 2)
    {
      InternalClipper.CheckPrecision(precision);
      _scale = Math.Pow(10, precision);
      _locator = new PointInPolygonLocator(Clipper.ScalePath64(polygon, _scale));
    }

    public PointInPolygonResult Locate(PointD pt) => _locator.Locate(new Point64(pt, _scale));

    /// <summary>True when the point is inside the polygon or on its boundary.</summary>
    public bool Contains(PointD pt) => Locate(pt) != PointInPolygonResult.IsOutside;

    /// <summary>The polygon's bounding box in the polygon's own units.</summary>
    public RectD Bounds
    {
      get
      {
        Rect64 b = _locator.Bounds;
        if (!b.IsValid()) return RectD.InvalidRect();
        return new RectD(b.left / _scale, b.top / _scale, b.right / _scale, b.bottom / _scale);
      }
    }

    /// <summary>Locates every point (results[i] belongs to points[i]).</summary>
    public void Locate(ReadOnlySpan<PointD> points, Span<PointInPolygonResult> results)
    {
      if (results.Length < points.Length)
        throw new ArgumentException("results is shorter than points", nameof(results));
      // the points are scaled in chunks, so the batch keeps the vectorised
      // bounding box test of the integer locator
      const int chunk = 1024;
      Point64[] buffer = System.Buffers.ArrayPool<Point64>.Shared.Rent(Math.Min(chunk, Math.Max(points.Length, 1)));
      try
      {
        for (int start = 0; start < points.Length; start += chunk)
        {
          int len = Math.Min(chunk, points.Length - start);
          for (int k = 0; k < len; k++) buffer[k] = new Point64(points[start + k], _scale);
          _locator.Locate(new ReadOnlySpan<Point64>(buffer, 0, len), results.Slice(start, len));
        }
      }
      finally
      {
        System.Buffers.ArrayPool<Point64>.Shared.Return(buffer);
      }
    }
  }
}
