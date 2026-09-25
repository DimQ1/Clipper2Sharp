/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  12 October 2025                                                 *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  Core Clipper Library structures and functions                   *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.core.h (Clipper2 ver. 2.0.1)                     *
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
  /// <summary>
  /// Error constants (2^n). These mirror the C++ constants in clipper.core.h.
  /// </summary>
  public static class Clipper2Error
  {
    public const int PrecisionError = 1;   // non-fatal
    public const int ScaleError = 2;       // non-fatal
    public const int NonPairError = 4;     // non-fatal
    public const int UndefinedError = 32;  // fatal
    public const int RangeError = 64;
  }

  /// <summary>
  /// Thrown in place of the C++ Clipper2Exception.
  /// </summary>
  public class Clipper2Exception : Exception
  {
    public Clipper2Exception() : base() { }
    public Clipper2Exception(string message) : base(message) { }
    public Clipper2Exception(string message, Exception? inner) : base(message, inner) { }
  }

  public enum PointInPolygonResult { IsOn, IsInside, IsOutside }

  // Note: all clipping operations except for Difference are commutative.
  public enum ClipType { NoClip, Intersection, Union, Difference, Xor }

  public enum PathType { Subject, Clip }

  // By far the most widely used filling rules for polygons are EvenOdd
  // and NonZero, sometimes called Alternate and Winding respectively.
  // https://en.wikipedia.org/wiki/Nonzero-rule
  public enum FillRule { EvenOdd, NonZero, Positive, Negative }

  /// <summary>
  /// Coordinates of Path64, Paths64, PolyTree64 and PolyPath64.
  /// </summary>
  public struct Point64
  {
    public long X;
    public long Y;

#if USINGZ
    public long Z;
#endif

    public Point64(Point64 pt)
    {
      X = pt.X;
      Y = pt.Y;
#if USINGZ
      Z = pt.Z;
#endif
    }

    public Point64(Point64 pt, double scale)
    {
      X = (long) Math.Round(pt.X * scale, MidpointRounding.AwayFromZero);
      Y = (long) Math.Round(pt.Y * scale, MidpointRounding.AwayFromZero);
#if USINGZ
      Z = (long) Math.Round(pt.Z * scale, MidpointRounding.AwayFromZero);
#endif
    }

    public Point64(long x, long y
#if USINGZ
      , long z = 0
#endif
    )
    {
      X = x;
      Y = y;
#if USINGZ
      Z = z;
#endif
    }

    public Point64(double x, double y
#if USINGZ
      , double z = 0.0
#endif
    )
    {
      X = (long) Math.Round(x, MidpointRounding.AwayFromZero);
      Y = (long) Math.Round(y, MidpointRounding.AwayFromZero);
#if USINGZ
      Z = (long) Math.Round(z, MidpointRounding.AwayFromZero);
#endif
    }

    public Point64(PointD pt)
    {
      X = (long) Math.Round(pt.x, MidpointRounding.AwayFromZero);
      Y = (long) Math.Round(pt.y, MidpointRounding.AwayFromZero);
#if USINGZ
      Z = pt.z;
#endif
    }

    public Point64(PointD pt, double scale)
    {
      X = (long) Math.Round(pt.x * scale, MidpointRounding.AwayFromZero);
      Y = (long) Math.Round(pt.y * scale, MidpointRounding.AwayFromZero);
#if USINGZ
      Z = pt.z;
#endif
    }

#if USINGZ
    public void SetZ(long z) { Z = z; }
#endif

    public static bool operator ==(Point64 lhs, Point64 rhs)
    {
      return lhs.X == rhs.X && lhs.Y == rhs.Y;
    }

    public static bool operator !=(Point64 lhs, Point64 rhs)
    {
      return lhs.X != rhs.X || lhs.Y != rhs.Y;
    }

    public static Point64 operator +(Point64 lhs, Point64 rhs)
    {
      return new Point64(lhs.X + rhs.X, lhs.Y + rhs.Y
#if USINGZ
        , lhs.Z + rhs.Z
#endif
      );
    }

    public static Point64 operator -(Point64 lhs, Point64 rhs)
    {
      return new Point64(lhs.X - rhs.X, lhs.Y - rhs.Y
#if USINGZ
        , lhs.Z - rhs.Z
#endif
      );
    }

    public static Point64 operator -(Point64 pt)
    {
      return new Point64(-pt.X, -pt.Y
#if USINGZ
        , -pt.Z
#endif
      );
    }

    /// <summary>Multiplies the coordinates by <paramref name="scale"/> (Z is preserved).</summary>
    public static Point64 operator *(Point64 pt, double scale)
    {
      return new Point64(pt.X * scale, pt.Y * scale
#if USINGZ
        , pt.Z
#endif
      );
    }

    public void Negate() { X = -X; Y = -Y; }

    public readonly override string ToString()
    {
      // nb: trailing space (matches the C++ operator<<)
#if USINGZ
      return $"{X},{Y},{Z} ";
#else
      return $"{X},{Y} ";
#endif
    }

    public readonly override bool Equals(object? obj)
    {
      if (obj is Point64 p)
        return this == p;
      return false;
    }

    public readonly override int GetHashCode()
    {
      return HashCode.Combine(X, Y); //#599
    }
  }

  /// <summary>
  /// Coordinates of PathD, PathsD, PolyTreeD and PolyPathD.
  /// </summary>
  public struct PointD
  {
    public double x;
    public double y;

#if USINGZ
    public long z;
#endif

    public PointD(PointD pt)
    {
      x = pt.x;
      y = pt.y;
#if USINGZ
      z = pt.z;
#endif
    }

    public PointD(Point64 pt)
    {
      x = pt.X;
      y = pt.Y;
#if USINGZ
      z = pt.Z;
#endif
    }

    public PointD(Point64 pt, double scale)
    {
      x = pt.X * scale;
      y = pt.Y * scale;
#if USINGZ
      z = pt.Z;
#endif
    }

    public PointD(PointD pt, double scale)
    {
      x = pt.x * scale;
      y = pt.y * scale;
#if USINGZ
      z = pt.z;
#endif
    }

    public PointD(long x, long y
#if USINGZ
      , long z = 0
#endif
    )
    {
      this.x = x;
      this.y = y;
#if USINGZ
      this.z = z;
#endif
    }

    public PointD(double x, double y
#if USINGZ
      , long z = 0
#endif
    )
    {
      this.x = x;
      this.y = y;
#if USINGZ
      this.z = z;
#endif
    }

#if USINGZ
    public void SetZ(long z) { this.z = z; }
#endif

    public static bool operator ==(PointD lhs, PointD rhs)
    {
      return InternalClipper.IsAlmostZero(lhs.x - rhs.x) &&
        InternalClipper.IsAlmostZero(lhs.y - rhs.y);
    }

    public static bool operator !=(PointD lhs, PointD rhs)
    {
      return !InternalClipper.IsAlmostZero(lhs.x - rhs.x) ||
        !InternalClipper.IsAlmostZero(lhs.y - rhs.y);
    }

    public static PointD operator +(PointD lhs, PointD rhs)
    {
      return new PointD(lhs.x + rhs.x, lhs.y + rhs.y
#if USINGZ
        , lhs.z + rhs.z
#endif
      );
    }

    public static PointD operator -(PointD lhs, PointD rhs)
    {
      return new PointD(lhs.x - rhs.x, lhs.y - rhs.y
#if USINGZ
        , lhs.z - rhs.z
#endif
      );
    }

    public static PointD operator -(PointD pt)
    {
      return new PointD(-pt.x, -pt.y
#if USINGZ
        , -pt.z
#endif
      );
    }

    public void Negate() { x = -x; y = -y; }

    public readonly string ToString(int precision = 2)
    {
#if USINGZ
      return string.Format($"{{0:F{precision}}},{{1:F{precision}}},{{2:D}}", x, y, z);
#else
      return string.Format($"{{0:F{precision}}},{{1:F{precision}}}", x, y);
#endif
    }

    public readonly override string ToString() => ToString(2);

    public readonly override bool Equals(object? obj)
    {
      if (obj is PointD p)
        return this == p;
      return false;
    }

    public readonly override int GetHashCode()
    {
      return HashCode.Combine(x, y); //#599
    }
  }

  public struct Rect64
  {
    public long left;
    public long top;
    public long right;
    public long bottom;

    public Rect64(long l, long t, long r, long b)
    {
      left = l;
      top = t;
      right = r;
      bottom = b;
    }

    public Rect64(bool isValid = true)
    {
      if (isValid)
      {
        left = 0; top = 0; right = 0; bottom = 0;
      }
      else
      {
        left = long.MaxValue; top = long.MaxValue;
        right = long.MinValue; bottom = long.MinValue;
      }
    }

    public Rect64(Rect64 rec)
    {
      left = rec.left;
      top = rec.top;
      right = rec.right;
      bottom = rec.bottom;
    }

    public static Rect64 InvalidRect() => new Rect64(false);

    public long Width
    {
      readonly get => right - left;
      set => right = left + value;
    }

    public long Height
    {
      readonly get => bottom - top;
      set => bottom = top + value;
    }

    public readonly bool IsEmpty()
    {
      return bottom <= top || right <= left;
    }

    public readonly bool IsValid()
    {
      return left != long.MaxValue;
    }

    public readonly Point64 MidPoint()
    {
      return new Point64((left + right) / 2, (top + bottom) / 2);
    }

    public readonly Path64 AsPath()
    {
      Path64 result = new Path64(4)
      {
        new Point64(left, top),
        new Point64(right, top),
        new Point64(right, bottom),
        new Point64(left, bottom)
      };
      return result;
    }

    public readonly bool Contains(Point64 pt)
    {
      return pt.X > left && pt.X < right &&
        pt.Y > top && pt.Y < bottom;
    }

    public readonly bool Contains(Rect64 rec)
    {
      return rec.left >= left && rec.right <= right &&
        rec.top >= top && rec.bottom <= bottom;
    }

    public readonly bool Intersects(Rect64 rec)
    {
      return (Math.Max(left, rec.left) <= Math.Min(right, rec.right)) &&
        (Math.Max(top, rec.top) <= Math.Min(bottom, rec.bottom));
    }

    public void Scale(double scale)
    {
      left = (long) (left * scale);
      top = (long) (top * scale);
      right = (long) (right * scale);
      bottom = (long) (bottom * scale);
    }

    public static bool operator ==(Rect64 lhs, Rect64 rhs)
    {
      return lhs.left == rhs.left && lhs.right == rhs.right &&
        lhs.top == rhs.top && lhs.bottom == rhs.bottom;
    }

    public static bool operator !=(Rect64 lhs, Rect64 rhs) => !(lhs == rhs);

    /// <summary>Returns the union (bounding rectangle) of two rectangles.</summary>
    public static Rect64 operator +(Rect64 lhs, Rect64 rhs)
    {
      return new Rect64(
        Math.Min(lhs.left, rhs.left),
        Math.Min(lhs.top, rhs.top),
        Math.Max(lhs.right, rhs.right),
        Math.Max(lhs.bottom, rhs.bottom));
    }

    public readonly override bool Equals(object? obj)
    {
      if (obj is Rect64 r) return this == r;
      return false;
    }

    public readonly override int GetHashCode()
    {
      return HashCode.Combine(left, top, right, bottom);
    }

    public readonly override string ToString()
    {
      return $"({left},{top},{right},{bottom}) ";
    }
  }

  public struct RectD
  {
    public double left;
    public double top;
    public double right;
    public double bottom;

    public RectD(double l, double t, double r, double b)
    {
      left = l;
      top = t;
      right = r;
      bottom = b;
    }

    public RectD(bool isValid = true)
    {
      if (isValid)
      {
        left = 0; top = 0; right = 0; bottom = 0;
      }
      else
      {
        left = double.MaxValue; top = double.MaxValue;
        right = double.MinValue; bottom = double.MinValue;
      }
    }

    public RectD(RectD rec)
    {
      left = rec.left;
      top = rec.top;
      right = rec.right;
      bottom = rec.bottom;
    }

    public static RectD InvalidRect() => new RectD(false);

    public double Width
    {
      readonly get => right - left;
      set => right = left + value;
    }

    public double Height
    {
      readonly get => bottom - top;
      set => bottom = top + value;
    }

    public readonly bool IsEmpty()
    {
      return bottom <= top || right <= left;
    }

    public readonly bool IsValid()
    {
      return left != double.MaxValue;
    }

    public readonly PointD MidPoint()
    {
      return new PointD((left + right) / 2, (top + bottom) / 2);
    }

    public readonly PathD AsPath()
    {
      PathD result = new PathD(4)
      {
        new PointD(left, top),
        new PointD(right, top),
        new PointD(right, bottom),
        new PointD(left, bottom)
      };
      return result;
    }

    public readonly bool Contains(PointD pt)
    {
      return pt.x > left && pt.x < right &&
        pt.y > top && pt.y < bottom;
    }

    public readonly bool Contains(RectD rec)
    {
      return rec.left >= left && rec.right <= right &&
        rec.top >= top && rec.bottom <= bottom;
    }

    public readonly bool Intersects(RectD rec)
    {
      return (Math.Max(left, rec.left) <= Math.Min(right, rec.right)) &&
        (Math.Max(top, rec.top) <= Math.Min(bottom, rec.bottom));
    }

    public void Scale(double scale)
    {
      left *= scale;
      top *= scale;
      right *= scale;
      bottom *= scale;
    }

    public static bool operator ==(RectD lhs, RectD rhs)
    {
      return lhs.left == rhs.left && lhs.right == rhs.right &&
        lhs.top == rhs.top && lhs.bottom == rhs.bottom;
    }

    public static bool operator !=(RectD lhs, RectD rhs) => !(lhs == rhs);

    public static RectD operator +(RectD lhs, RectD rhs)
    {
      return new RectD(
        Math.Min(lhs.left, rhs.left),
        Math.Min(lhs.top, rhs.top),
        Math.Max(lhs.right, rhs.right),
        Math.Max(lhs.bottom, rhs.bottom));
    }

    public readonly override bool Equals(object? obj)
    {
      if (obj is RectD r) return this == r;
      return false;
    }

    public readonly override int GetHashCode()
    {
      return HashCode.Combine(left, top, right, bottom);
    }

    public readonly override string ToString()
    {
      return $"({left},{top},{right},{bottom}) ";
    }
  }

  /// <summary>
  /// Converts and scales a rectangle (the C++ ScaleRect&lt;T1, T2&gt; template).
  /// </summary>
  public static class RectScale
  {
    public static Rect64 ToRect64(RectD rect, double scale)
    {
      // nb: the C++ uses std::round when T1 is integral
      return new Rect64(
        (long) Math.Round(rect.left * scale, MidpointRounding.AwayFromZero),
        (long) Math.Round(rect.top * scale, MidpointRounding.AwayFromZero),
        (long) Math.Round(rect.right * scale, MidpointRounding.AwayFromZero),
        (long) Math.Round(rect.bottom * scale, MidpointRounding.AwayFromZero));
    }

    public static RectD ToRectD(Rect64 rect, double scale)
    {
      return new RectD(
        rect.left * scale,
        rect.top * scale,
        rect.right * scale,
        rect.bottom * scale);
    }
  }

  public class Path64 : List<Point64>
  {
    public Path64() : base() { }
    public Path64(int capacity) : base(capacity) { }
    public Path64(IEnumerable<Point64> path) : base(path) { }

    public override string ToString()
    {
      return string.Join(", ", this);
    }
  }

  public class Paths64 : List<Path64>
  {
    public Paths64() : base() { }
    public Paths64(int capacity) : base(capacity) { }
    public Paths64(IEnumerable<Path64> paths) : base(paths) { }

    public override string ToString()
    {
      return string.Join(Environment.NewLine, this);
    }
  }

  public class PathD : List<PointD>
  {
    public PathD() : base() { }
    public PathD(int capacity) : base(capacity) { }
    public PathD(IEnumerable<PointD> path) : base(path) { }

    public string ToString(int precision)
    {
      return string.Join(", ", ConvertAll(p => p.ToString(precision)));
    }

    public override string ToString() => ToString(2);
  }

  public class PathsD : List<PathD>
  {
    public PathsD() : base() { }
    public PathsD(int capacity) : base(capacity) { }
    public PathsD(IEnumerable<PathD> paths) : base(paths) { }

    public string ToString(int precision)
    {
      return string.Join(Environment.NewLine, ConvertAll(p => p.ToString(precision)));
    }

    public override string ToString() => ToString(2);
  }

  public static class InternalClipper
  {
    internal const long MaxInt64 = 9223372036854775807;
    internal const long MaxCoord = MaxInt64 >> 2;        // == MAX_COORD in C++
    internal const long MinCoord = -MaxCoord;            // == MIN_COORD
    internal const double max_coord = MaxCoord;
    internal const double min_coord = MinCoord;
    internal const long Invalid64 = MaxInt64;            // == INVALID
    internal const double MaxDbl = double.MaxValue;      // == MAX_DBL

    internal const double floatingPointTolerance = 1E-12;
    internal const double defaultMinimumEdgeLength = 0.1;

    internal const int MaxDecimalPrecision = 8; // see Discussions #564

    internal const string precision_range_error = "Precision exceeds the permitted range";
    internal const string range_error = "Values exceed permitted range";
    internal const string scale_error = "Invalid scale (either 0 or too large)";
    internal const string non_pair_error = "There must be 2 values for each coordinate";
    internal const string undefined_error = "There is an undefined error in Clipper2";

    public static double PI => Math.PI;

    /// <summary>
    /// The C++ DoError() function. Always throws (the C++ only throws when
    /// exceptions are enabled, but the exception-free build is unusable from C#).
    /// </summary>
    internal static void DoError(int errorCode)
    {
      switch (errorCode)
      {
        case Clipper2Error.PrecisionError:
          throw new Clipper2Exception(precision_range_error);
        case Clipper2Error.ScaleError:
          throw new Clipper2Exception(scale_error);
        case Clipper2Error.NonPairError:
          throw new Clipper2Exception(non_pair_error);
        case Clipper2Error.UndefinedError:
          throw new Clipper2Exception(undefined_error);
        case Clipper2Error.RangeError:
          throw new Clipper2Exception(range_error);
        default:
          throw new Clipper2Exception("Unknown error");
      }
    }

    /// <summary>
    /// Clamps precision to the permitted range [-8 .. 8] and signals
    /// precision_error_i when clamping was required.
    /// </summary>
    public static void CheckPrecisionRange(ref int precision, ref int errorCode)
    {
      if (precision >= -MaxDecimalPrecision && precision <= MaxDecimalPrecision) return;
      errorCode |= Clipper2Error.PrecisionError; // non-fatal error
      DoError(Clipper2Error.PrecisionError);
      precision = precision > 0 ? MaxDecimalPrecision : -MaxDecimalPrecision;
    }

    public static void CheckPrecisionRange(ref int precision)
    {
      int errorCode = 0;
      CheckPrecisionRange(ref precision, ref errorCode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CheckPrecision(int precision)
    {
      CheckPrecisionRange(ref precision);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsAlmostZero(double value)
    {
      return Math.Abs(value) <= floatingPointTolerance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TriSign(long x) // returns 0, 1 or -1
    {
      return (x > 0 ? 1 : 0) - (x < 0 ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetSign<T>(T val) where T : IComparable<T>
    {
      if (val.CompareTo(default) == 0) return 0;
      return val.CompareTo(default) > 0 ? 1 : -1;
    }

    /// <summary>
    /// The 128 bit unsigned product returned by the C++ MultiplyUInt64().
    /// </summary>
    public struct UInt128Struct
    {
      public ulong lo64;
      public ulong hi64;

      public readonly bool Equals(UInt128Struct other)
      {
        return lo64 == other.lo64 && hi64 == other.hi64;
      }

      public readonly override bool Equals(object? obj)
      {
        return obj is UInt128Struct other && Equals(other);
      }

      public readonly override int GetHashCode()
      {
        return HashCode.Combine(lo64, hi64);
      }

      public static bool operator ==(UInt128Struct lhs, UInt128Struct rhs) => lhs.Equals(rhs);
      public static bool operator !=(UInt128Struct lhs, UInt128Struct rhs) => !lhs.Equals(rhs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UInt128Struct MultiplyUInt64(ulong a, ulong b) // #834, #835
    {
      // .NET has a native 128 bit unsigned integer type, so there's no need
      // for the manual 64x64->128 bit long multiplication used in C++.
      UInt128 result = (UInt128) a * b;
      UInt128Struct res;
      res.lo64 = (ulong) result;
      res.hi64 = (ulong) (result >> 64);
      return res;
    }

    /// <summary>
    /// Returns true if (and only if) a * b == c * d.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ProductsAreEqual(long a, long b, long c, long d)
    {
      // nb: exact in both tiers. When every factor fits in 31 bits (|v| < 2^31)
      // each product fits in 62 bits, so plain 64 bit multiplication is exact;
      // otherwise the full 128 bit products are compared (Math.BigMul is one
      // widening 'imul'). The guard ORs the one's complement magnitudes
      // (v ^ (v >> 63) is |v| for v >= 0 and |v| - 1 for v < 0), so a single
      // compare tests all four factors - see docs/optimization-plan.md 5.2.
      if (FitsIn31Bits(a, b, c, d)) return a * b == c * d;
      long hi1 = Math.BigMul(a, b, out long lo1);
      long hi2 = Math.BigMul(c, d, out long lo2);
      return hi1 == hi2 && lo1 == lo2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FitsIn31Bits(long a, long b, long c, long d)
    {
      long m = (a ^ (a >> 63)) | (b ^ (b >> 63)) | (c ^ (c >> 63)) | (d ^ (d >> 63));
      return (ulong) m < (1UL << 31);
    }

    /// <summary>Returns the sign of a*b - c*d, exactly (see ProductsAreEqual).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ProductsCompare(long a, long b, long c, long d)
    {
      if (FitsIn31Bits(a, b, c, d))
      {
        long ab = a * b, cd = c * d;
        return (ab > cd ? 1 : 0) - (ab < cd ? 1 : 0);
      }
      long hi1 = Math.BigMul(a, b, out long lo1);
      long hi2 = Math.BigMul(c, d, out long lo2);
      if (hi1 != hi2) return hi1 > hi2 ? 1 : -1;
      ulong u1 = (ulong) lo1, u2 = (ulong) lo2;
      return (u1 > u2 ? 1 : 0) - (u1 < u2 ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CrossProductSign(Point64 pt1, Point64 pt2, Point64 pt3)
    {
      long a = pt2.X - pt1.X;
      long b = pt3.Y - pt2.Y;
      long c = pt2.Y - pt1.Y;
      long d = pt3.X - pt2.X;

      // nb: the differences can exceed 32 bits, so the products need the wide
      // type (#834, #835): exact 64 bit products when every difference fits in
      // 31 bits, exact 128 bit products otherwise (ProductsCompare)
      return ProductsCompare(a, b, c, d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CrossProductSign(PointD pt1, PointD pt2, PointD pt3)
    {
      double ab = (pt2.x - pt1.x) * (pt3.y - pt2.y);
      double cd = (pt2.y - pt1.y) * (pt3.x - pt2.x);
      if (ab > cd) return 1;
      if (ab < cd) return -1;
      return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsCollinear(Point64 pt1, Point64 sharedPt, Point64 pt2)
    {
      long a = sharedPt.X - pt1.X;
      long b = pt2.Y - sharedPt.Y;
      long c = sharedPt.Y - pt1.Y;
      long d = pt2.X - sharedPt.X;
      // When checking for collinearity with very large coordinate values
      // then ProductsAreEqual is more accurate than using CrossProduct.
      return ProductsAreEqual(a, b, c, d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsCollinear(PointD pt1, PointD sharedPt, PointD pt2)
    {
      double a = sharedPt.x - pt1.x;
      double b = pt2.y - sharedPt.y;
      double c = sharedPt.y - pt1.y;
      double d = pt2.x - sharedPt.x;
      // When checking for collinearity with very large coordinate values
      // then ProductsAreEqual is more accurate than using CrossProduct.
      return (a * b) == (c * d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double CrossProduct(Point64 pt1, Point64 pt2, Point64 pt3)
    {
      // typecast to double to avoid potential int overflow
      return ((double) (pt2.X - pt1.X) * (pt3.Y - pt2.Y) -
              (double) (pt2.Y - pt1.Y) * (pt3.X - pt2.X));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double CrossProduct(PointD vec1, PointD vec2)
    {
      return (vec1.y * vec2.x - vec2.y * vec1.x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double DotProduct(Point64 pt1, Point64 pt2, Point64 pt3)
    {
      // typecast to double to avoid potential int overflow
      return ((double) (pt2.X - pt1.X) * (pt3.X - pt2.X) +
              (double) (pt2.Y - pt1.Y) * (pt3.Y - pt2.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double DotProduct(PointD vec1, PointD vec2)
    {
      return (vec1.x * vec2.x + vec1.y * vec2.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long CheckCastInt64(double val)
    {
      if ((val >= max_coord) || (val <= min_coord)) return Invalid64;
      return (long) Math.Round(val, MidpointRounding.AwayFromZero);
    }

    // GetLineIntersectPt - a 'true' result is non-parallel. The 'ip' will also
    // be constrained to seg1. However, it's possible that 'ip' won't be inside
    // seg2, even when 'ip' hasn't been constrained (ie 'ip' is inside seg1).

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetLineIntersectPt(Point64 ln1a,
      Point64 ln1b, Point64 ln2a, Point64 ln2b, out Point64 ip)
    {
      double dx1 = (double) (ln1b.X - ln1a.X);
      double dy1 = (double) (ln1b.Y - ln1a.Y);
      double dx2 = (double) (ln2b.X - ln2a.X);
      double dy2 = (double) (ln2b.Y - ln2a.Y);

      double det = dy1 * dx2 - dy2 * dx1;
      if (det == 0.0)
      {
        ip = new Point64();
        return false;
      }

      double t = ((ln1a.X - ln2a.X) * dy2 - (ln1a.Y - ln2a.Y) * dx2) / det;
      if (t <= 0.0) ip = ln1a;
      else if (t >= 1.0) ip = ln1b;
      else
      {
        // avoid using constructor (and rounding too) as they affect performance //664
        ip.X = (long) (ln1a.X + t * dx1);
        ip.Y = (long) (ln1a.Y + t * dy1);
#if USINGZ
        ip.Z = 0;
#endif
      }
      return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetLineIntersectPt(PointD ln1a,
      PointD ln1b, PointD ln2a, PointD ln2b, out PointD ip)
    {
      double dx1 = (ln1b.x - ln1a.x);
      double dy1 = (ln1b.y - ln1a.y);
      double dx2 = (ln2b.x - ln2a.x);
      double dy2 = (ln2b.y - ln2a.y);

      double det = dy1 * dx2 - dy2 * dx1;
      if (det == 0.0)
      {
        ip = new PointD();
        return false;
      }

      double t = ((ln1a.x - ln2a.x) * dy2 - (ln1a.y - ln2a.y) * dx2) / det;
      if (t <= 0.0) ip = ln1a;
      else if (t >= 1.0) ip = ln1b;
      else
      {
        ip.x = (ln1a.x + t * dx1);
        ip.y = (ln1a.y + t * dy1);
#if USINGZ
        ip.z = 0;
#endif
      }
      return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool SegsIntersect(Point64 seg1a,
      Point64 seg1b, Point64 seg2a, Point64 seg2b, bool inclusive = false)
    {
      double dy1 = (seg1b.Y - seg1a.Y);
      double dx1 = (seg1b.X - seg1a.X);
      double dy2 = (seg2b.Y - seg2a.Y);
      double dx2 = (seg2b.X - seg2a.X);
      double cp = dy1 * dx2 - dy2 * dx1;
      if (cp == 0) return false; // ie parallel segments

      if (inclusive)
      {
        //result **includes** segments that touch at an end point
        double t = ((seg1a.X - seg2a.X) * dy2 - (seg1a.Y - seg2a.Y) * dx2);
        if (t == 0) return true;
        if (t > 0)
        {
          if (cp < 0 || t > cp) return false;
        }
        else if (cp > 0 || t < cp) return false; // false when t more neg. than cp

        t = ((seg1a.X - seg2a.X) * dy1 - (seg1a.Y - seg2a.Y) * dx1);
        if (t == 0) return true;
        if (t > 0) return (cp > 0 && t <= cp);
        else return (cp < 0 && t >= cp);        // true when t less neg. than cp
      }
      else
      {
        //result **excludes** segments that touch at an end point
        double t = ((seg1a.X - seg2a.X) * dy2 - (seg1a.Y - seg2a.Y) * dx2);
        if (t == 0) return false;
        if (t > 0)
        {
          if (cp < 0 || t >= cp) return false;
        }
        else if (cp > 0 || t <= cp) return false; // false when t more neg. than cp

        t = ((seg1a.X - seg2a.X) * dy1 - (seg1a.Y - seg2a.Y) * dx1);
        if (t == 0) return false;
        if (t > 0) return (cp > 0 && t < cp);
        else return (cp < 0 && t > cp); // true when t less neg. than cp
      }
    }

    // GetBounds -----------------------------------------------------------------

    public static Rect64 GetBounds(Path64 path)
    {
#if USINGZ
      long xmin = long.MaxValue, ymin = long.MaxValue;
      long xmax = long.MinValue, ymax = long.MinValue;
      foreach (Point64 pt in path)
      {
        if (pt.X < xmin) xmin = pt.X;
        if (pt.X > xmax) xmax = pt.X;
        if (pt.Y < ymin) ymin = pt.Y;
        if (pt.Y > ymax) ymax = pt.Y;
      }
      return new Rect64(xmin, ymin, xmax, ymax);
#else
      // nb: Point64 is a pair of 64 bit integers, so the point buffer can be
      // read as one interleaved coordinate buffer and reduced with SIMD
      if (path.Count == 0)
        return new Rect64(long.MaxValue, long.MaxValue, long.MinValue, long.MinValue);
      ReadOnlySpan<long> vals = MemoryMarshal.Cast<Point64, long>(
        CollectionsMarshal.AsSpan(path));
      BulkOps.MinMaxInterleaved(vals,
        out long xmin, out long xmax, out long ymin, out long ymax);
      return new Rect64(xmin, ymin, xmax, ymax);
#endif
    }

    public static Rect64 GetBounds(Paths64 paths)
    {
      // each path is reduced independently and the partial bounds are then
      // combined in the original order, so the result is deterministic
      int count = paths.Count;
      if (count == 0)
        return new Rect64(long.MaxValue, long.MaxValue, long.MinValue, long.MinValue);

      long xmin = long.MaxValue, ymin = long.MaxValue;
      long xmax = long.MinValue, ymax = long.MinValue;

      if (!BulkOps.ShouldParallelize(count))
      {
        // nb: one pass, no partial array (an empty path contributes nothing,
        // exactly the way its invalid rectangle did in the combining loop)
        for (int i = 0; i < count; i++)
        {
          Path64 path = paths[i];
          if (path.Count == 0) continue;
          ReadOnlySpan<long> vals = MemoryMarshal.Cast<Point64, long>(
            CollectionsMarshal.AsSpan(path));
          BulkOps.MinMaxInterleaved(vals,
            out long x1, out long x2, out long y1, out long y2);
          if (x1 < xmin) xmin = x1;
          if (x2 > xmax) xmax = x2;
          if (y1 < ymin) ymin = y1;
          if (y2 > ymax) ymax = y2;
        }
        return new Rect64(xmin, ymin, xmax, ymax);
      }

      Rect64[] partial = new Rect64[count];
      BulkOps.For(count, i => partial[i] = GetBounds(paths[i]));
      for (int i = 0; i < count; i++)
      {
        if (partial[i].left < xmin) xmin = partial[i].left;
        if (partial[i].right > xmax) xmax = partial[i].right;
        if (partial[i].top < ymin) ymin = partial[i].top;
        if (partial[i].bottom > ymax) ymax = partial[i].bottom;
      }
      return new Rect64(xmin, ymin, xmax, ymax);
    }

    public static RectD GetBounds(PathD path)
    {
#if USINGZ
      double xmin = double.MaxValue, ymin = double.MaxValue;
      double xmax = double.MinValue, ymax = double.MinValue;
      foreach (PointD pt in path)
      {
        if (pt.x < xmin) xmin = pt.x;
        if (pt.x > xmax) xmax = pt.x;
        if (pt.y < ymin) ymin = pt.y;
        if (pt.y > ymax) ymax = pt.y;
      }
      return new RectD(xmin, ymin, xmax, ymax);
#else
      if (path.Count == 0)
        return new RectD(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
      ReadOnlySpan<double> vals = MemoryMarshal.Cast<PointD, double>(
        CollectionsMarshal.AsSpan(path));
      BulkOps.MinMaxInterleaved(vals,
        out double xmin, out double xmax, out double ymin, out double ymax);
      return new RectD(xmin, ymin, xmax, ymax);
#endif
    }

    public static RectD GetBounds(PathsD paths)
    {
      int count = paths.Count;
      if (count == 0)
        return new RectD(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);

      double xmin = double.MaxValue, ymin = double.MaxValue;
      double xmax = double.MinValue, ymax = double.MinValue;

      if (!BulkOps.ShouldParallelize(count))
      {
        // nb: one pass, no partial array
        for (int i = 0; i < count; i++)
        {
          PathD path = paths[i];
          if (path.Count == 0) continue;
          ReadOnlySpan<double> vals = MemoryMarshal.Cast<PointD, double>(
            CollectionsMarshal.AsSpan(path));
          BulkOps.MinMaxInterleaved(vals,
            out double x1, out double x2, out double y1, out double y2);
          if (x1 < xmin) xmin = x1;
          if (x2 > xmax) xmax = x2;
          if (y1 < ymin) ymin = y1;
          if (y2 > ymax) ymax = y2;
        }
        return new RectD(xmin, ymin, xmax, ymax);
      }

      RectD[] partial = new RectD[count];
      BulkOps.For(count, i => partial[i] = GetBounds(paths[i]));
      for (int i = 0; i < count; i++)
      {
        if (partial[i].left < xmin) xmin = partial[i].left;
        if (partial[i].right > xmax) xmax = partial[i].right;
        if (partial[i].top < ymin) ymin = partial[i].top;
        if (partial[i].bottom > ymax) ymax = partial[i].bottom;
      }
      return new RectD(xmin, ymin, xmax, ymax);
    }

    // Scaling -------------------------------------------------------------------

    internal static Path64 ScalePath(PathD path, double scaleX, double scaleY,
      ref int errorCode)
    {
      Path64 result = new Path64(path.Count);
      if (scaleX == 0 || scaleY == 0)
      {
        errorCode |= Clipper2Error.ScaleError;
        DoError(Clipper2Error.ScaleError);
        // if no exception, treat as non-fatal error
        if (scaleX == 0) scaleX = 1.0;
        if (scaleY == 0) scaleY = 1.0;
      }

      foreach (PointD pt in path)
        result.Add(new Point64(pt.x * scaleX, pt.y * scaleY
#if USINGZ
          , pt.z
#endif
        ));
      return result;
    }

    internal static Path64 ScalePath(PathD path, double scale, ref int errorCode)
    {
      return ScalePath(path, scale, scale, ref errorCode);
    }

    internal static PathD ScalePath(Path64 path, double scaleX, double scaleY,
      ref int errorCode)
    {
      PathD result = new PathD(path.Count);
      foreach (Point64 pt in path)
        result.Add(new PointD(pt.X * scaleX, pt.Y * scaleY
#if USINGZ
          , pt.Z
#endif
        ));
      return result;
    }

    internal static PathD ScalePath(Path64 path, double scale, ref int errorCode)
    {
      return ScalePath(path, scale, scale, ref errorCode);
    }

    internal static Paths64 ScalePaths(PathsD paths, double scaleX, double scaleY,
      ref int errorCode)
    {
      Paths64 result = new Paths64();
      if ((GetBounds(paths).left * scaleX) < min_coord ||
        (GetBounds(paths).right * scaleX) > max_coord ||
        (GetBounds(paths).top * scaleY) < min_coord ||
        (GetBounds(paths).bottom * scaleY) > max_coord)
      {
        errorCode |= Clipper2Error.RangeError;
        DoError(Clipper2Error.RangeError);
        return result; // empty paths
      }

      result.Capacity = paths.Count;
      foreach (PathD path in paths)
        result.Add(ScalePath(path, scaleX, scaleY, ref errorCode));
      return result;
    }

    internal static Paths64 ScalePaths(PathsD paths, double scale, ref int errorCode)
    {
      return ScalePaths(paths, scale, scale, ref errorCode);
    }

    internal static PathsD ScalePaths(Paths64 paths, double scaleX, double scaleY,
      ref int errorCode)
    {
      PathsD result = new PathsD(paths.Count);
      foreach (Path64 path in paths)
        result.Add(ScalePath(path, scaleX, scaleY, ref errorCode));
      return result;
    }

    internal static PathsD ScalePaths(Paths64 paths, double scale, ref int errorCode)
    {
      return ScalePaths(paths, scale, scale, ref errorCode);
    }

    // Miscellaneous -------------------------------------------------------------

    internal static Point64 GetClosestPointOnSegment(Point64 offPt,
      Point64 seg1, Point64 seg2)
    {
      if (seg1.X == seg2.X && seg1.Y == seg2.Y) return seg1;
      double dx = (seg2.X - seg1.X);
      double dy = (seg2.Y - seg1.Y);
      double q = ((offPt.X - seg1.X) * dx +
        (offPt.Y - seg1.Y) * dy) / (Sqr(dx) + Sqr(dy));
      if (q < 0) q = 0; else if (q > 1) q = 1;
      // use MidpointRounding.ToEven in order to explicitly match
      // the nearbyint behaviour on the C++ side
      return new Point64(
        seg1.X + (long) Math.Round(q * dx, MidpointRounding.ToEven),
        seg1.Y + (long) Math.Round(q * dy, MidpointRounding.ToEven));
    }

    internal static PointD GetClosestPointOnSegment(PointD offPt,
      PointD seg1, PointD seg2)
    {
      if (seg1.x == seg2.x && seg1.y == seg2.y) return seg1;
      double dx = (seg2.x - seg1.x);
      double dy = (seg2.y - seg1.y);
      double q = ((offPt.x - seg1.x) * dx +
        (offPt.y - seg1.y) * dy) / (Sqr(dx) + Sqr(dy));
      if (q < 0) q = 0; else if (q > 1) q = 1;
      return new PointD(seg1.x + q * dx, seg1.y + q * dy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Sqr(double val) => val * val;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Sqr(long val) => (double) val * (double) val;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double DistanceSqr(Point64 pt1, Point64 pt2)
    {
      return Sqr(pt1.X - pt2.X) + Sqr(pt1.Y - pt2.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double DistanceSqr(PointD pt1, PointD pt2)
    {
      return Sqr(pt1.x - pt2.x) + Sqr(pt1.y - pt2.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Distance(Point64 pt1, Point64 pt2)
    {
      return Math.Sqrt(DistanceSqr(pt1, pt2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Distance(PointD pt1, PointD pt2)
    {
      return Math.Sqrt(DistanceSqr(pt1, pt2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Point64 TranslatePoint(Point64 pt, double dx, double dy)
    {
      return new Point64((long) (pt.X + dx), (long) (pt.Y + dy)
#if USINGZ
        , pt.Z
#endif
      );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PointD TranslatePoint(PointD pt, double dx, double dy)
    {
      return new PointD(pt.x + dx, pt.y + dy
#if USINGZ
        , pt.z
#endif
      );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Point64 ReflectPoint(Point64 pt, Point64 pivot)
    {
      return new Point64(pivot.X + (pivot.X - pt.X), pivot.Y + (pivot.Y - pt.Y)
#if USINGZ
        , pt.Z
#endif
      );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PointD ReflectPoint(PointD pt, PointD pivot)
    {
      return new PointD(pivot.x + (pivot.x - pt.x), pivot.y + (pivot.y - pt.y)
#if USINGZ
        , pt.z
#endif
      );
    }

    internal static bool NearEqual(Point64 pt1, Point64 pt2, double maxDistSqrd)
    {
      return Sqr(pt1.X - pt2.X) + Sqr(pt1.Y - pt2.Y) < maxDistSqrd;
    }

    internal static bool NearEqual(PointD pt1, PointD pt2, double maxDistSqrd)
    {
      return Sqr(pt1.x - pt2.x) + Sqr(pt1.y - pt2.y) < maxDistSqrd;
    }

    /// <summary>
    /// Removes points that are close to each other (C++ StripNearEqual).
    /// </summary>
    public static Path64 StripNearEqual(Path64 path, double maxDistSqrd,
      bool isClosedPath)
    {
      if (path.Count == 0) return new Path64();
      Path64 result = new Path64(path.Count);
      Point64 firstPt = path[0], lastPt = firstPt;
      result.Add(firstPt);
      for (int i = 1; i < path.Count; i++)
      {
        if (!NearEqual(path[i], lastPt, maxDistSqrd))
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      }
      if (!isClosedPath) return result;
      while (result.Count > 1 && NearEqual(result[^1], firstPt, maxDistSqrd))
        result.RemoveAt(result.Count - 1);
      return result;
    }

    public static PathD StripNearEqual(PathD path, double maxDistSqrd,
      bool isClosedPath)
    {
      if (path.Count == 0) return new PathD();
      PathD result = new PathD(path.Count);
      PointD firstPt = path[0], lastPt = firstPt;
      result.Add(firstPt);
      for (int i = 1; i < path.Count; i++)
      {
        if (!NearEqual(path[i], lastPt, maxDistSqrd))
        {
          lastPt = path[i];
          result.Add(lastPt);
        }
      }
      if (!isClosedPath) return result;
      while (result.Count > 1 && NearEqual(result[^1], firstPt, maxDistSqrd))
        result.RemoveAt(result.Count - 1);
      return result;
    }

    public static Paths64 StripNearEqual(Paths64 paths, double maxDistSqrd,
      bool isClosedPath)
    {
      // each path is independent, so the paths can be processed in parallel
      // (the results are written at stable indices, so output order is unchanged)
      Paths64 result = new Paths64(paths.Count);
      for (int i = 0; i < paths.Count; i++) result.Add(null!);
      BulkOps.For(paths.Count, i => result[i] = StripNearEqual(paths[i], maxDistSqrd, isClosedPath));
      return result;
    }

    public static PathsD StripNearEqual(PathsD paths, double maxDistSqrd,
      bool isClosedPath)
    {
      PathsD result = new PathsD(paths.Count);
      for (int i = 0; i < paths.Count; i++) result.Add(null!);
      BulkOps.For(paths.Count, i => result[i] = StripNearEqual(paths[i], maxDistSqrd, isClosedPath));
      return result;
    }

    internal static void StripDuplicates(Path64 path, bool isClosedPath)
    {
      int w = 0;
      for (int i = 0; i < path.Count; i++)
      {
        if (w == 0 || path[i] != path[w - 1])
          path[w++] = path[i];
      }
      path.RemoveRange(w, path.Count - w);
      if (isClosedPath)
        while (path.Count > 1 && path[^1] == path[0]) path.RemoveAt(path.Count - 1);
    }

    internal static void StripDuplicates(PathD path, bool isClosedPath)
    {
      int w = 0;
      for (int i = 0; i < path.Count; i++)
      {
        if (w == 0 || path[i] != path[w - 1])
          path[w++] = path[i];
      }
      path.RemoveRange(w, path.Count - w);
      if (isClosedPath)
        while (path.Count > 1 && path[^1] == path[0]) path.RemoveAt(path.Count - 1);
    }

    internal static void StripDuplicates(Paths64 paths, bool isClosedPath)
    {
      foreach (Path64 path in paths)
        StripDuplicates(path, isClosedPath);
    }

    internal static void StripDuplicates(PathsD paths, bool isClosedPath)
    {
      foreach (PathD path in paths)
        StripDuplicates(path, isClosedPath);
    }

    // PointInPolygon -------------------------------------------------------------

    public static PointInPolygonResult PointInPolygon(Point64 pt, Path64 polygon)
    {
      int len = polygon.Count;
      if (len < 3) return PointInPolygonResult.IsOutside;

      // nb: these scans are the entire cost of a point-in-polygon query, so the
      // points are read through a span: the List<T> indexer reloads the backing
      // array and its size on every access, while the span keeps a single length
      // check that the JIT can hoist out of the loops.
      ReadOnlySpan<Point64> poly = CollectionsMarshal.AsSpan(polygon);

      int start = 0;
      while (start < len && poly[start].Y == pt.Y) ++start;
      if (start == len) // not a proper polygon
        return PointInPolygonResult.IsOutside;

      long ptX = pt.X, ptY = pt.Y;
      bool isAbove = poly[start].Y < ptY, startingAbove = isAbove;
      int val = 0, i = start + 1, end = len;
      while (true)
      {
        if (i == end)
        {
          if (end == 0 || start == 0) break;
          end = start;
          i = 0;
        }

        if (isAbove)
        {
          while (i < end && poly[i].Y < ptY) ++i;
        }
        else
        {
          while (i < end && poly[i].Y > ptY) ++i;
        }

        if (i == end) continue;

        Point64 curr = poly[i], prev;
        if (i > 0) prev = poly[i - 1];
        else prev = poly[len - 1];

        if (curr.Y == ptY)
        {
          if (curr.X == ptX || (curr.Y == prev.Y &&
            ((ptX < prev.X) != (ptX < curr.X))))
            return PointInPolygonResult.IsOn;
          ++i;
          if (i == start) break;
          continue;
        }

        if (ptX < curr.X && ptX < prev.X)
        {
          // we're only interested in edges crossing on the left
        }
        else if (ptX > prev.X && ptX > curr.X)
        {
          val = 1 - val; // toggle val
        }
        else
        {
          int d = CrossProductSign(prev, curr, pt);
          if (d == 0) return PointInPolygonResult.IsOn;
          if ((d < 0) == isAbove) val = 1 - val;
        }
        isAbove = !isAbove;
        ++i;
      }

      if (isAbove == startingAbove)
        return (val == 0) ?
          PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;

      if (i == len) i = 0;
      int cps = (i == 0) ?
        CrossProductSign(poly[len - 1], poly[0], pt) :
        CrossProductSign(poly[i - 1], poly[i], pt);

      if (cps == 0) return PointInPolygonResult.IsOn;
      if ((cps < 0) == isAbove) val = 1 - val;

      return (val == 0) ?
        PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;
    }

    public static PointInPolygonResult PointInPolygon(PointD pt, PathD polygon)
    {
      int len = polygon.Count;
      if (len < 3) return PointInPolygonResult.IsOutside;

      int start = 0;
      while (start < len && polygon[start].y == pt.y) ++start;
      if (start == len) // not a proper polygon
        return PointInPolygonResult.IsOutside;

      bool isAbove = polygon[start].y < pt.y, startingAbove = isAbove;
      int val = 0, i = start + 1, end = len;
      while (true)
      {
        if (i == end)
        {
          if (end == 0 || start == 0) break;
          end = start;
          i = 0;
        }

        if (isAbove)
        {
          while (i < end && polygon[i].y < pt.y) ++i;
        }
        else
        {
          while (i < end && polygon[i].y > pt.y) ++i;
        }

        if (i == end) continue;

        PointD curr = polygon[i], prev;
        if (i > 0) prev = polygon[i - 1];
        else prev = polygon[len - 1];

        if (curr.y == pt.y)
        {
          if (curr.x == pt.x || (curr.y == prev.y &&
            ((pt.x < prev.x) != (pt.x < curr.x))))
            return PointInPolygonResult.IsOn;
          ++i;
          if (i == start) break;
          continue;
        }

        if (pt.x < curr.x && pt.x < prev.x)
        {
          // we're only interested in edges crossing on the left
        }
        else if (pt.x > prev.x && pt.x > curr.x)
        {
          val = 1 - val; // toggle val
        }
        else
        {
          int d = CrossProductSign(prev, curr, pt);
          if (d == 0) return PointInPolygonResult.IsOn;
          if ((d < 0) == isAbove) val = 1 - val;
        }
        isAbove = !isAbove;
        ++i;
      }

      if (isAbove == startingAbove)
        return (val == 0) ?
          PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;

      if (i == len) i = 0;
      int cps = (i == 0) ?
        CrossProductSign(polygon[len - 1], polygon[0], pt) :
        CrossProductSign(polygon[i - 1], polygon[i], pt);

      if (cps == 0) return PointInPolygonResult.IsOn;
      if ((cps < 0) == isAbove) val = 1 - val;

      return (val == 0) ?
        PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;
    }

    /// <summary>
    /// Returns true when path2 contains path1 (C++ Path2ContainsPath1).
    /// Precondition: paths must not intersect, except for transient
    /// (and presumed 'micro') path intersections.
    /// </summary>
    public static bool Path2ContainsPath1(Path64 path1, Path64 path2)
    {
      PointInPolygonResult pip = PointInPolygonResult.IsOn;
      foreach (Point64 pt in path1)
      {
        switch (PointInPolygon(pt, path2))
        {
          case PointInPolygonResult.IsOutside:
            if (pip == PointInPolygonResult.IsOutside) return false;
            pip = PointInPolygonResult.IsOutside;
            break;
          case PointInPolygonResult.IsInside:
            if (pip == PointInPolygonResult.IsInside) return true;
            pip = PointInPolygonResult.IsInside;
            break;
          default:
            break;
        }
      }
      if (pip != PointInPolygonResult.IsInside) return false;
      // result is likely true but check the midpoint
      Point64 mp1 = GetBounds(path1).MidPoint();
      return PointInPolygon(mp1, path2) == PointInPolygonResult.IsInside;
    }

    public static bool Path2ContainsPath1(PathD path1, PathD path2)
    {
      PointInPolygonResult pip = PointInPolygonResult.IsOn;
      foreach (PointD pt in path1)
      {
        switch (PointInPolygon(pt, path2))
        {
          case PointInPolygonResult.IsOutside:
            if (pip == PointInPolygonResult.IsOutside) return false;
            pip = PointInPolygonResult.IsOutside;
            break;
          case PointInPolygonResult.IsInside:
            if (pip == PointInPolygonResult.IsInside) return true;
            pip = PointInPolygonResult.IsInside;
            break;
          default:
            break;
        }
      }
      if (pip != PointInPolygonResult.IsInside) return false;
      // result is likely true but check the midpoint
      PointD mp1 = GetBounds(path1).MidPoint();
      return PointInPolygon(mp1, path2) == PointInPolygonResult.IsInside;
    }
  }
}
