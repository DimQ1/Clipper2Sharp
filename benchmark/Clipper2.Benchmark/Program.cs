using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clipper2.Benchmark
{
  internal static class Program
  {
    private static readonly List<string> Report = new List<string>();
    private static readonly List<BaselineRow> Rows = new List<BaselineRow>();
    private static int _iterations = 3;
    private static string? _only = null;
    private static string? _baselinePath = null;
    private static string? _writeBaselinePath = null;

    /// <summary>One measured workload, kept for the baseline file.</summary>
    private sealed class BaselineRow
    {
      public string Workload = "";
      public double PortMs;
      public double RefMs;
      public double PortMb;
      public double RefMb;
      public double Speedup;
      public double AllocRatio;
      public bool Identical;
    }

    private static bool Wanted(string label)
    {
      return _only == null || label.Contains(_only, StringComparison.OrdinalIgnoreCase);
    }

    private static void Main(string[] args)
    {
      if (args.Length > 0 && int.TryParse(args[0], out int it) && it > 0)
        _iterations = it;
      foreach (string arg in args)
      {
        if (arg.StartsWith("--only=", StringComparison.Ordinal))
          _only = arg.Substring(7);
        else if (arg.StartsWith("--baseline=", StringComparison.Ordinal))
          _baselinePath = arg.Substring(11);
        else if (arg.StartsWith("--write-baseline=", StringComparison.Ordinal))
          _writeBaselinePath = arg.Substring(17);
      }

      Console.WriteLine("Clipper2Sharp benchmark");
      Console.WriteLine($"  runtime      : {Environment.Version} / {(Environment.Is64BitProcess ? "x64" : "x86")}");
      Console.WriteLine($"  processors   : {Environment.ProcessorCount}");
      Console.WriteLine($"  iterations   : {_iterations} (best of)");
      if (_baselinePath != null) Console.WriteLine($"  baseline     : {_baselinePath}");
      if (_writeBaselinePath != null) Console.WriteLine($"  writing      : {_writeBaselinePath}");
      Console.WriteLine();

      List<ClipCase> polygons = TestDataLoader.Load("Polygons.txt");
      List<ClipCase> offsets = TestDataLoader.Load("Offsets.txt");

      Console.WriteLine($"loaded {polygons.Count} polygon cases and {offsets.Count} offset cases");
      Console.WriteLine();

      IClipperAdapter newLib = new NewAdapter();
      IClipperAdapter refLib = new RefAdapter();

      // ---------------------------------------------------------------- boolean ops
      Compare("boolean ops (Polygons.txt, all cases)", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (ClipCase c in polygons)
        {
          OpResult r = a.BooleanOp(c.Subjects.ToArray(), c.Clips.ToArray(),
            c.ClipTypeIndex, c.FillRuleIndex);
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      // ---------------------------------------------------------------- single big union
      List<long[]> unionInput = new List<long[]>();
      foreach (ClipCase c in polygons) unionInput.AddRange(c.Subjects);

      Compare("union: add subject paths only (ingestion)", newLib, refLib, a =>
        a.IngestOnly(unionInput.ToArray()));

      Compare("union of every subject path (Paths64)", newLib, refLib, a =>
        a.BooleanOp(unionInput.ToArray(), null, 2, 1));

      // nb: the same paths translated far apart, so the sweep performs no edge
      // intersection work at all - this isolates the vertex/scanline/output
      // plumbing from the intersection handling when comparing implementations
      long[][] separated = new long[unionInput.Count][];
      for (int i = 0; i < unionInput.Count; i++)
      {
        long[] p = unionInput[i];
        long[] q = new long[p.Length];
        for (int j = 0; j < p.Length; j += 2)
        {
          q[j] = p[j] + i * 100_000L;
          q[j + 1] = p[j + 1] + i * 100_000L;
        }
        separated[i] = q;
      }

      Compare("union of the same paths moved apart (no intersections)", newLib, refLib,
        a => a.BooleanOp(separated, null, 2, 1));

      // ---------------------------------------------------------------- offsets
      Compare("offsets (Offsets.txt, delta = 1, Round/Polygon)", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (ClipCase c in offsets)
        {
          OpResult r = a.Inflate(c.Subjects.ToArray(), 1, "Round", "Polygon", 2.0, 0.0);
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      Compare("offsets (Offsets.txt, delta = 27, Miter/Square)", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (ClipCase c in offsets)
        {
          OpResult r = a.Inflate(c.Subjects.ToArray(), 27, "Miter", "Polygon", 2.0, 0.0);
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      // ---------------------------------------------------------------- rect clipping
      Compare("rect clip (off all subjects into 100,100,700,500)", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (ClipCase c in polygons)
        {
          OpResult r = a.RectClip(100, 100, 700, 500, c.Subjects.ToArray());
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      Compare("rect clip lines (off all subjects into 100,100,700,500)", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (ClipCase c in polygons)
        {
          OpResult r = a.RectClipLines(100, 100, 700, 500, c.Subjects.ToArray());
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      // ---------------------------------------------------------------- simplify
      Compare("simplify paths (all subjects, eps = 0.5)", newLib, refLib, a =>
        a.Simplify(unionInput.ToArray(), 0.5));

      // ---------------------------------------------------------------- triangulation
      long[][] circle = new long[1][];
      {
        List<long> pts = new List<long>();
        int steps = 2000;
        for (int i = 0; i < steps; i++)
        {
          double a = 2 * Math.PI * i / steps;
          pts.Add((long) (100000 * Math.Cos(a)));
          pts.Add((long) (100000 * Math.Sin(a)));
        }
        circle[0] = pts.ToArray();
      }

      Compare("triangulate 2000 vertex polygon", newLib, refLib, a => a.Triangulate(circle));

      // ---------------------------------------------------------------- area / bounds / pip
      Compare("area (Paths64 of every subject+clip)", newLib, refLib, a => a.Areas(unionInput.ToArray()));
      Compare("get bounds (Paths64 of every subject+clip)", newLib, refLib,
        a => new OpResult(a.Bounds(unionInput.ToArray()), 0));

      long[] xs, ys;
      BuildProbePoints(unionInput, out xs, out ys);
      Compare("point in polygon (200k probes)", newLib, refLib,
        a => new OpResult(a.PointInPolygonHits(unionInput.ToArray(), xs, ys), 0));

      Compare("scale round trip (Paths64 -> PathsD -> Paths64, x1000)", newLib, refLib,
        a => a.ScaleRoundTrip(unionInput.ToArray(), 1000));

      // ------------------------------------------------ many independent groups/paths
      long[][] groups = unionInput.ToArray();

      Compare("offset: every subject path as its own group", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (long[] g in groups)
        {
          OpResult r = a.Inflate(new[] { g }, 20, "Round", "Polygon", 2.0, 0.0);
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      Compare("rect clip: every subject path separately", newLib, refLib, a =>
      {
        long count = 0;
        double area = 0;
        foreach (long[] g in groups)
        {
          OpResult r = a.RectClip(100, 100, 700, 500, new[] { g });
          count += r.Count;
          area += r.Area;
        }
        return new OpResult(count, (long) area);
      });

      // ---------------------------------------------------------------- per case diff
      DiffReport(polygons, offsets, newLib, refLib, args);

      Console.WriteLine();
      Console.WriteLine(string.Join(Environment.NewLine, Report));

      string outFile = Path.Combine(AppContext.BaseDirectory,
        $"benchmark-report-{DateTime.Now:yyyyMMdd-HHmmss}.md");
      using (StreamWriter sw = new StreamWriter(outFile))
      {
        sw.WriteLine("# Clipper2Sharp benchmark");
        sw.WriteLine();
        sw.WriteLine($"- runtime: {Environment.Version} ({(Environment.Is64BitProcess ? "x64" : "x86")}), {Environment.ProcessorCount} logical processors");
        sw.WriteLine($"- iterations: {_iterations} (best of)");
        sw.WriteLine("- reference: upstream C# sources (E:\\Learning\\AI\\Clipper2\\CSharp\\Clipper2Lib)");
        sw.WriteLine("  compiled by this repository's toolchain and target framework (net10.0)");
        sw.WriteLine();
        sw.Write(string.Join(Environment.NewLine, Report));
      }
      Console.WriteLine($"report written to {outFile}");

      if (_writeBaselinePath != null) WriteBaseline(_writeBaselinePath);
      if (_baselinePath != null) CompareWithBaseline(_baselinePath);
    }

    /// <summary>
    /// Writes the measured rows to a baseline file so a later run can be compared
    /// against a known starting point. Rows are merged by workload (a run filtered
    /// with --only= updates just the workloads it measured, it does not drop the
    /// rest), and the sections the benchmark does not own - the native C++ numbers,
    /// the verification status, the list of remaining targets - are preserved.
    /// </summary>
    private static void WriteBaseline(string path)
    {
      JsonObject root = new JsonObject();
      if (File.Exists(path))
      {
        try { root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? root; }
        catch (JsonException) { root = new JsonObject(); }
      }

      // keep the recorded order, and update rows in place
      List<string> order = new List<string>();
      Dictionary<string, JsonObject> byWorkload = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
      if (root["managed"] is JsonArray previous)
      {
        foreach (JsonNode? node in previous)
        {
          if (node is JsonObject obj && obj["workload"] != null)
          {
            string key = obj["workload"]!.GetValue<string>();
            order.Add(key);
            byWorkload[key] = obj;
          }
        }
      }
      foreach (BaselineRow row in Rows)
      {
        if (!byWorkload.ContainsKey(row.Workload)) order.Add(row.Workload);
        byWorkload[row.Workload] = new JsonObject
        {
          ["workload"] = row.Workload,
          ["portMs"] = Math.Round(row.PortMs, 4),
          ["refMs"] = Math.Round(row.RefMs, 4),
          ["portMB"] = Math.Round(row.PortMb, 4),
          ["refMB"] = Math.Round(row.RefMb, 4),
          ["speedupVsRefCSharp"] = Math.Round(row.Speedup, 4),
          ["allocRatioVsRefCSharp"] = Math.Round(row.AllocRatio, 4),
          ["resultsIdentical"] = row.Identical
        };
      }

      JsonArray managed = new JsonArray();
      foreach (string key in order)
        if (byWorkload.TryGetValue(key, out JsonObject? row))
          managed.Add(row.DeepClone());

      root["recordedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
      root["environment"] = new JsonObject
      {
        ["runtime"] = Environment.Version.ToString(),
        ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        ["processors"] = Environment.ProcessorCount,
        ["iterationsPerRun"] = _iterations,
        ["machineNote"] = "development machine runs at 75-85% background CPU: time ratios of the short rows " +
          "move +-10-15% between sessions, allocation is deterministic"
      };
      root["managed"] = managed;

      Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
      File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
      Console.WriteLine();
      Console.WriteLine($"baseline written to {path} ({Rows.Count} workloads)");
    }

    /// <summary>Prints the current run against a previously recorded baseline.</summary>
    private static void CompareWithBaseline(string path)
    {
      if (!File.Exists(path))
      {
        Console.WriteLine($"baseline {path} not found - skipping the comparison");
        return;
      }
      JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
      Dictionary<string, JsonNode> was = new Dictionary<string, JsonNode>();
      if (root["managed"] is JsonArray array)
        foreach (JsonNode? node in array)
          if (node != null && node["workload"] != null)
            was[node["workload"]!.GetValue<string>()] = node;

      Console.WriteLine();
      Console.WriteLine($"against baseline ({root["recordedAtUtc"]?.GetValue<string>()}):");
      Console.WriteLine($"{ "workload",-52} {"now",9} {"baseline",9} {"delta",9}  {"speed vs ref now/was",22}");
      double worst = 0;
      string worstName = "";
      foreach (BaselineRow row in Rows)
      {
        if (!was.TryGetValue(row.Workload, out JsonNode? before)) continue;
        double wasMs = before["portMs"]!.GetValue<double>();
        double wasSpeed = before["speedupVsRefCSharp"]!.GetValue<double>();
        double delta = wasMs > 0 ? (row.PortMs - wasMs) / wasMs * 100.0 : 0;
        if (delta > worst) { worst = delta; worstName = row.Workload; }
        Console.WriteLine($"{row.Workload,-52} {row.PortMs,9:0.00} {wasMs,9:0.00} {delta,8:+0.0;-0.0;0.0}%  " +
          $"{row.Speedup,8:0.00}x / {wasSpeed:0.00}x");
      }
      if (worstName.Length > 0)
        Console.WriteLine($"largest regression: {worstName} {worst:+0.0}% (treat anything under ~15% as machine noise)");
    }

    /// <summary>
    /// Counts the cases whose results differ between the two implementations and
    /// lists the largest differences (only a port of the newer C++ 2.0.1 sources
    /// can legitimately differ from the older upstream C# 2.0.0 port).
    /// </summary>
    private static void DiffReport(List<ClipCase> polygons, List<ClipCase> offsets,
      IClipperAdapter newLib, IClipperAdapter refLib, string[] args)
    {
      bool verbose = Array.IndexOf(args, "--diff") >= 0;
      List<string> lines = new List<string>();

      int differing = 0;
      int cases = 0;
      foreach (ClipCase c in polygons)
      {
        cases++;
        OpResult a = newLib.BooleanOp(c.Subjects.ToArray(), c.Clips.ToArray(),
          c.ClipTypeIndex, c.FillRuleIndex);
        OpResult b = refLib.BooleanOp(c.Subjects.ToArray(), c.Clips.ToArray(),
          c.ClipTypeIndex, c.FillRuleIndex);
        if (!a.Equals(b))
        {
          differing++;
          if (verbose || differing <= 12)
            lines.Add($"  boolean case {c.Number} ({c.ClipType}/{c.FillRule}): new {a} vs ref {b}");
        }
      }

      int offsetDiffering = 0;
      foreach (ClipCase c in offsets)
      {
        OpResult a = newLib.Inflate(c.Subjects.ToArray(), 1, "Round", "Polygon", 2.0, 0.0);
        OpResult b = refLib.Inflate(c.Subjects.ToArray(), 1, "Round", "Polygon", 2.0, 0.0);
        if (!a.Equals(b))
        {
          offsetDiffering++;
          lines.Add($"  offset case {c.Number}: new {a} vs ref {b}");
        }
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine("### Differences against the upstream C# 2.0.0 port");
      sb.AppendLine();
      sb.AppendLine($"- boolean cases: {differing} of {cases} differ");
      sb.AppendLine($"- offset cases : {offsetDiffering} of {offsets.Count} differ");
      sb.AppendLine();
      if (lines.Count > 0)
      {
        sb.AppendLine("```");
        foreach (string l in lines) sb.AppendLine(l);
        sb.AppendLine("```");
        sb.AppendLine();
      }
      Report.Add(sb.ToString());

      Console.WriteLine($"per-case differences vs upstream: boolean {differing}/{cases}, offsets {offsetDiffering}/{offsets.Count}");
      if (verbose)
        foreach (string l in lines) Console.WriteLine(l);
    }

    private static void BuildProbePoints(List<long[]> paths, out long[] xs, out long[] ys)
    {
      long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
      foreach (long[] p in paths)
        for (int i = 0; i < p.Length; i += 2)
        {
          if (p[i] < minX) minX = p[i];
          if (p[i] > maxX) maxX = p[i];
          if (p[i + 1] < minY) minY = p[i + 1];
          if (p[i + 1] > maxY) maxY = p[i + 1];
        }
      const int count = 200_000;
      Random rnd = new Random(12345);
      xs = new long[count];
      ys = new long[count];
      for (int i = 0; i < count; i++)
      {
        xs[i] = minX + (long) (rnd.NextDouble() * (maxX - minX));
        ys[i] = minY + (long) (rnd.NextDouble() * (maxY - minY));
      }
    }

    private static void Compare(string label, IClipperAdapter newLib, IClipperAdapter refLib,
      Func<IClipperAdapter, OpResult> workload)
    {
      if (!Wanted(label)) return;

      // warm-up (JIT, tiered compilation, pooled buffers) for both libraries
      OpResult newResult = workload(newLib);
      OpResult refResult = workload(refLib);

      // nb: the two libraries are measured in alternating rounds (and the order
      // is swapped every round), so background load and thermal drift on the
      // machine hit both of them, and each library supplies its own best round.
      // Measuring one implementation to completion and then the other can easily
      // differ by 20% on a busy machine even when the code is identical.
      (double ms, long bytes) newStats = (double.MaxValue, 0);
      (double ms, long bytes) refStats = (double.MaxValue, 0);
      for (int iter = 0; iter < _iterations; iter++)
      {
        if ((iter & 1) == 0)
        {
          newStats = Better(newStats, MeasureOnce(() => newResult = workload(newLib)));
          refStats = Better(refStats, MeasureOnce(() => refResult = workload(refLib)));
        }
        else
        {
          refStats = Better(refStats, MeasureOnce(() => refResult = workload(refLib)));
          newStats = Better(newStats, MeasureOnce(() => newResult = workload(newLib)));
        }
      }

      bool match = newResult.Equals(refResult);
      double speedup = newStats.ms > 0 ? refStats.ms / newStats.ms : 1.0;
      double allocRatio = refStats.bytes > 0 ? (double) newStats.bytes / refStats.bytes : 1.0;

      Rows.Add(new BaselineRow
      {
        Workload = label,
        PortMs = newStats.ms,
        RefMs = refStats.ms,
        PortMb = newStats.bytes / 1048576.0,
        RefMb = refStats.bytes / 1048576.0,
        Speedup = speedup,
        AllocRatio = allocRatio,
        Identical = match
      });

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"### {label}");
      sb.AppendLine();
      sb.AppendLine("| implementation | time (ms) | allocated (MB) | result |");
      sb.AppendLine("|---|---:|---:|---|");
      sb.AppendLine($"| {newLib.Name} | {newStats.ms:0.00} | {newStats.bytes / 1048576.0:0.00} | {newResult} |");
      sb.AppendLine($"| {refLib.Name} | {refStats.ms:0.00} | {refStats.bytes / 1048576.0:0.00} | {refResult} |");
      sb.AppendLine();
      sb.AppendLine($"speed-up **{speedup:0.00}x**, allocation **{allocRatio:0.000}x** of the reference, results **{(match ? "identical" : "DIFFERENT")}**");
      sb.AppendLine();
      Report.Add(sb.ToString());

      Console.WriteLine($"{label,-58} new {newStats.ms,9:0.00} ms / {newStats.bytes / 1048576.0,7:0.00} MB   " +
        $"ref {refStats.ms,9:0.00} ms / {refStats.bytes / 1048576.0,7:0.00} MB   " +
        $"speedup {speedup,5:0.00}x  alloc {allocRatio,5:0.000}x  {(match ? "OK" : "MISMATCH")}");
    }

    private static (double ms, long bytes) Better((double ms, long bytes) a, (double ms, long bytes) b)
    {
      return b.ms < a.ms ? b : a;
    }

    /// <summary>
    /// Runs the workload repeatedly (long enough to be measured reliably) and
    /// returns the time of its fastest uninterrupted run together with the
    /// average allocation per run. The first run is skipped because it starts
    /// with a cold cache (the other library was just measured), and the fastest
    /// (rather than the average) run is used because it is the one that least
    /// suffered from the machine's background load.
    /// </summary>
    private static (double ms, long bytes) MeasureOnce(Func<OpResult> workload)
    {
      long before = GC.GetTotalAllocatedBytes(true);
      Stopwatch sw = new Stopwatch();
      double totalMs = 0, bestMs = double.MaxValue;
      int reps = 0;
      do
      {
        sw.Restart();
        workload();
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;
        if (reps > 0 && ms < bestMs) bestMs = ms;
        totalMs += ms;
        reps++;
      } while (totalMs < 400.0 && reps < 100_000);
      long allocated = GC.GetTotalAllocatedBytes(true) - before;
      if (bestMs == double.MaxValue) bestMs = totalMs / reps;
      return (bestMs, (long) (allocated / (double) reps));
    }
  }
}
