using System;
using Clipper2Lib;

namespace Clipper2.WebDemo.Demo;

/// <summary>
/// Deterministic shape generators. Everything lives in the demo's world
/// coordinates (the SVG viewBox), so a shape can be drawn exactly as it comes
/// out of the library - no scaling in between.
///
/// All generators wind their vertices the same way (increasing angle, y down),
/// which is what the NonZero fill rule needs to union them.
/// </summary>
public static class Shapes
{
  public const long WorldWidth = 1000;
  public const long WorldHeight = 640;
  public const int ProbeSpacing = 25;

  public static Point64 Centre => new(WorldWidth / 2, WorldHeight / 2);

  /// <summary>A regular polygon.</summary>
  public static Path64 Regular(Point64 centre, double radius, int vertices, double rotationDegrees = 0)
  {
    Path64 path = new(vertices);
    double start = rotationDegrees * Math.PI / 180.0;
    for (int i = 0; i < vertices; i++)
    {
      double a = start + 2 * Math.PI * i / vertices;
      path.Add(Polar(centre, radius, a));
    }
    return path;
  }

  /// <summary>A star with <paramref name="points"/> spikes.</summary>
  public static Path64 Star(Point64 centre, double outer, double inner, int points, double rotationDegrees = 0)
  {
    Path64 path = new(points * 2);
    double start = rotationDegrees * Math.PI / 180.0;
    for (int i = 0; i < points * 2; i++)
    {
      double radius = (i % 2 == 0) ? outer : inner;
      path.Add(Polar(centre, radius, start + Math.PI * i / points));
    }
    return path;
  }

  /// <summary>An irregular closed path, the kind of input a real drawing gives.</summary>
  public static Path64 Blob(Random rnd, Point64 centre, double radius, int vertices)
  {
    Path64 path = new(vertices);
    for (int i = 0; i < vertices; i++)
    {
      double a = 2 * Math.PI * i / vertices;
      path.Add(Polar(centre, radius * (0.65 + 0.7 * rnd.NextDouble()), a));
    }
    return path;
  }

  /// <summary>
  /// A dense outline that is smooth in the large and jittery in the small - the
  /// shape simplification exists for. The jitter is what the epsilon sweep removes.
  /// </summary>
  public static Path64 NoisyOutline(Random rnd, Point64 centre, double radius, int vertices, double jitter)
  {
    Path64 path = new(vertices);
    for (int i = 0; i < vertices; i++)
    {
      double a = 2 * Math.PI * i / vertices;
      // a few low frequency lobes make the outline interesting, the jitter makes it dense
      double lobes = 1 + 0.28 * Math.Sin(3 * a + 0.7) + 0.18 * Math.Sin(7 * a);
      double noise = jitter * (rnd.NextDouble() * 2 - 1);
      path.Add(Polar(centre, radius * lobes + noise, a));
    }
    return path;
  }

  /// <summary>A rectangle, wound like <see cref="Regular"/>.</summary>
  public static Path64 Rect(long left, long top, long width, long height)
  {
    Path64 path = new(4)
    {
      new Point64(left, top),
      new Point64(left + width, top),
      new Point64(left + width, top + height),
      new Point64(left, top + height)
    };
    return path;
  }

  /// <summary>A grid of squares, handy to show how many paths the engine handles.</summary>
  public static Paths64 Grid(int columns, int rows, long size, long gap, Point64 origin)
  {
    Paths64 paths = new(columns * rows);
    double step = size + gap;
    for (int y = 0; y < rows; y++)
      for (int x = 0; x < columns; x++)
      {
        // a slight wobble keeps the shapes from being a perfectly degenerate case
        long dx = (long) ((y % 2 == 0 ? 0 : step * 0.5));
        paths.Add(Rect(origin.X + dx + (long) (x * step), origin.Y + (long) (y * step), size, size));
      }
    return paths;
  }

  /// <summary>
  /// Scattered overlapping shapes - the workload the speed panel measures.
  /// </summary>
  public static Paths64 RandomPaths(int seed, int count, int vertices, double radius,
    long spreadX, long spreadY)
  {
    Random rnd = new(seed);
    Paths64 paths = new(count);
    for (int i = 0; i < count; i++)
    {
      long cx = rnd.NextInt64((long) radius, spreadX - (long) radius);
      long cy = rnd.NextInt64((long) radius, spreadY - (long) radius);
      paths.Add(Blob(rnd, new Point64(cx, cy), radius, vertices));
    }
    return paths;
  }

  /// <summary>The probe grid the point-in-polygon page draws.</summary>
  public static (long[] Xs, long[] Ys) ProbeGrid()
  {
    int cols = (int) (WorldWidth / ProbeSpacing) + 1;
    int rows = (int) (WorldHeight / ProbeSpacing) + 1;
    long[] xs = new long[cols * rows];
    long[] ys = new long[cols * rows];
    int n = 0;
    for (int y = 0; y < rows; y++)
      for (int x = 0; x < cols; x++)
      {
        xs[n] = x * (long) ProbeSpacing + 5;
        ys[n] = y * (long) ProbeSpacing + 5;
        n++;
      }
    return (xs, ys);
  }

  private static Point64 Polar(Point64 centre, double radius, double angle) =>
    new(centre.X + (long) Math.Round(radius * Math.Cos(angle)),
        centre.Y + (long) Math.Round(radius * Math.Sin(angle)));
}
