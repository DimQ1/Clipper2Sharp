using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Clipper2Lib;

namespace Clipper2.WebDemo.Demo;

/// <summary>
/// The few SVG helpers the demo needs. Every number is formatted with the
/// invariant culture: the browser's culture would otherwise be able to turn
/// "12.5" into "12,5" and quietly break the path data.
/// </summary>
public static class Svg
{
  public static string Number(double value) =>
    value.ToString("0.###", CultureInfo.InvariantCulture);

  public static string Number(double value, string format) =>
    value.ToString(format, CultureInfo.InvariantCulture);

  /// <summary>A closed path as an SVG "d" attribute.</summary>
  public static string PathData(Path64 path)
  {
    if (path.Count == 0) return string.Empty;
    StringBuilder sb = new(path.Count * 12);
    for (int i = 0; i < path.Count; i++)
    {
      sb.Append(i == 0 ? 'M' : ' ');
      sb.Append(path[i].X.ToString(CultureInfo.InvariantCulture));
      sb.Append(',');
      sb.Append(path[i].Y.ToString(CultureInfo.InvariantCulture));
    }
    sb.Append(" Z");
    return sb.ToString();
  }

  /// <summary>All paths of a set, as one "d" attribute (even-odd holes included).</summary>
  public static string PathsData(Paths64 paths)
  {
    if (paths.Count == 0) return string.Empty;
    StringBuilder sb = new(paths.Count * 64);
    foreach (Path64 path in paths)
    {
      string d = PathData(path);
      if (d.Length == 0) continue;
      if (sb.Length > 0) sb.Append(' ');
      sb.Append(d);
    }
    return sb.ToString();
  }

  /// <summary>A polyline through world points, for the chart.</summary>
  public static string Polyline(IReadOnlyList<(double X, double Y)> points)
  {
    if (points.Count == 0) return string.Empty;
    StringBuilder sb = new(points.Count * 16);
    for (int i = 0; i < points.Count; i++)
    {
      if (i > 0) sb.Append(' ');
      sb.Append(Number(points[i].X));
      sb.Append(',');
      sb.Append(Number(points[i].Y));
    }
    return sb.ToString();
  }
}
