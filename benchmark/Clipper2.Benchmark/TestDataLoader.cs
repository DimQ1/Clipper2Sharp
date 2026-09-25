using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Clipper2.Benchmark
{
  /// <summary>A single clipping test parsed from Polygons.txt / Offsets.txt.</summary>
  internal sealed class ClipCase
  {
    public int Number;
    public string Caption = "";
    public string ClipType = "INTERSECTION";
    public string FillRule = "EVENODD";
    public long StoredArea;
    public long StoredCount;
    public List<long[]> Subjects = new List<long[]>();
    public List<long[]> OpenSubjects = new List<long[]>();
    public List<long[]> Clips = new List<long[]>();

    public int ClipTypeIndex => ClipType switch
    {
      "INTERSECTION" => 1,
      "UNION" => 2,
      "DIFFERENCE" => 3,
      _ => 4
    };

    public int FillRuleIndex => FillRule switch
    {
      "EVENODD" => 0,
      "POSITIVE" => 2,
      "NEGATIVE" => 3,
      _ => 1
    };
  }

  internal static class TestDataLoader
  {
    public static List<ClipCase> Load(string fileName)
    {
      List<ClipCase> result = new List<ClipCase>();
      string path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
      if (!File.Exists(path)) throw new FileNotFoundException(path);

      ClipCase? current = null;
      int section = 0; // 0 = none, 1 = subjects, 2 = open subjects, 3 = clips
      foreach (string line in File.ReadLines(path))
      {
        if (line.StartsWith("CAPTION: ", StringComparison.Ordinal))
        {
          current = new ClipCase
          {
            Number = result.Count + 1,
            Caption = line.Substring(9)
          };
          result.Add(current);
          section = 0;
          continue;
        }
        if (current == null) continue;

        if (line.StartsWith("CLIPTYPE: ", StringComparison.Ordinal))
        {
          current.ClipType = line.Substring(10).Trim().ToUpperInvariant();
          continue;
        }
        if (line.StartsWith("FILLRULE: ", StringComparison.Ordinal) ||
            line.StartsWith("FILLTYPE: ", StringComparison.Ordinal))
        {
          current.FillRule = line.Substring(10).Trim().ToUpperInvariant();
          continue;
        }
        if (line.StartsWith("SOL_AREA: ", StringComparison.Ordinal))
        {
          current.StoredArea = long.Parse(line.Substring(10), CultureInfo.InvariantCulture);
          continue;
        }
        if (line.StartsWith("SOL_COUNT: ", StringComparison.Ordinal))
        {
          current.StoredCount = long.Parse(line.Substring(11), CultureInfo.InvariantCulture);
          continue;
        }
        if (line.StartsWith("SUBJECTS_OPEN", StringComparison.Ordinal)) { section = 2; continue; }
        if (line.StartsWith("SUBJECTS", StringComparison.Ordinal)) { section = 1; continue; }
        if (line.StartsWith("CLIPS", StringComparison.Ordinal)) { section = 3; continue; }

        if (section == 0) continue;
        long[]? pts = ParseCoords(line);
        if (pts == null || pts.Length < 6) continue;
        switch (section)
        {
          case 1: current.Subjects.Add(pts); break;
          case 2: current.OpenSubjects.Add(pts); break;
          case 3: current.Clips.Add(pts); break;
        }
      }
      return result;
    }

    private static long[]? ParseCoords(string line)
    {
      List<long> values = new List<long>();
      int i = 0, len = line.Length;
      while (i < len)
      {
        while (i < len && (line[i] < 33 || line[i] == ',')) i++;
        if (i >= len) break;
        bool neg = line[i] == '-';
        if (neg) i++;
        int start = i;
        long val = 0;
        while (i < len && line[i] >= '0' && line[i] <= '9')
          val = val * 10 + (line[i++] - '0');
        if (i == start) break;
        values.Add(neg ? -val : val);
      }
      return values.Count == 0 ? null : values.ToArray();
    }

    /// <summary>All subject paths of all cases, flattened (used by the geometric micro-benchmarks).</summary>
    public static List<long[]> AllPaths(List<ClipCase> cases)
    {
      List<long[]> all = new List<long[]>();
      foreach (ClipCase c in cases)
      {
        all.AddRange(c.Subjects);
        all.AddRange(c.Clips);
      }
      return all;
    }
  }
}
