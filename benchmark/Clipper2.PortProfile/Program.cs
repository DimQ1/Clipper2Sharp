using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Clipper2Lib;

namespace Clipper2.PortProfile
{
  /// <summary>
  /// Port-only driver.
  ///
  ///   golden record|verify [file]  - hashes the complete output (every coordinate,
  ///                                  path order, polytree nesting) of a large set of
  ///                                  operations over the test corpus plus a
  ///                                  deterministic fuzz corpus. "verify" compares with
  ///                                  a recorded file: an optimisation that is meant to
  ///                                  be bit-exact must report 0 differences.
  ///   fidelity [cppDump [portDump]] - the per case count/area dump of the 195 boolean
  ///                                  and 2 offset cases, diffed with the C++ dump
  ///                                  (and written to portDump when given).
  ///   union|bool|offset|rect|tri|pip|simplify [reps] - one workload of the benchmark,
  ///                                  port only (profile this, not the A/B benchmark:
  ///                                  both libraries print as Clipper2Lib.* in a trace).
  /// </summary>
  internal static class Program
  {
    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private static int Main(string[] args)
    {
      string mode = args.Length > 0 ? args[0] : "golden";
      switch (mode)
      {
        case "golden":
          return Golden(args.Length > 1 ? args[1] : "verify",
            args.Length > 2 ? args[2] : FindRepoFile("Results", "golden-hashes.txt"));
        case "fidelity":
          return Fidelity(args.Length > 1 ? args[1] : FindRepoFile("Results", "fidelity-cpp.txt"),
            args.Length > 2 ? args[2] : null);
        default:
          return Workload(mode, args.Length > 1 ? int.Parse(args[1]) : 20);
      }
    }

    private static string FindRepoFile(string dir, string file)
    {
      string? d = AppContext.BaseDirectory;
      while (d != null)
      {
        string candidate = Path.Combine(d, dir);
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(d, "Clipper2Sharp.slnx")))
          return Path.Combine(candidate, file);
        d = Path.GetDirectoryName(d);
      }
      return Path.Combine(dir, file);
    }

    // ------------------------------------------------------------------ corpus

    internal sealed class Case
    {
      public int Num;
      public ClipType Ct;
      public FillRule Fr;
      public Paths64 Subj = new Paths64(), SubjOpen = new Paths64(), Clip = new Paths64();
    }

    internal static List<Case> LoadCases(string file)
    {
      List<Case> result = new List<Case>();
      for (int i = 1; ; i++)
      {
        Case c = new Case { Num = i };
        if (!ClipperFileIO.LoadTestNum(file, i, c.Subj, c.SubjOpen, c.Clip,
          out c.Ct, out c.Fr, out _, out _, out _)) break;
        result.Add(c);
      }
      return result;
    }

    // ------------------------------------------------------------------ hashing

    private sealed class Hasher
    {
      private ulong _h = 14695981039346656037UL;
      public void Add(long v)
      {
        ulong x = (ulong) v;
        for (int i = 0; i < 8; i++) { _h ^= (x & 0xFF); _h *= 1099511628211UL; x >>= 8; }
      }
      public void Add(double v) => Add(BitConverter.DoubleToInt64Bits(v));
      public void Add(Paths64 paths)
      {
        Add(paths.Count);
        foreach (Path64 p in paths)
        {
          Add(p.Count);
          foreach (Point64 pt in p) { Add(pt.X); Add(pt.Y); }
        }
      }
      public void Add(PathsD paths)
      {
        Add(paths.Count);
        foreach (PathD p in paths)
        {
          Add(p.Count);
          foreach (PointD pt in p) { Add(pt.x); Add(pt.y); }
        }
      }
      public void Add(PolyPath64 pp)
      {
        Add(pp.Count);
        if (pp.Polygon != null) Add(new Paths64 { pp.Polygon });
        for (int i = 0; i < pp.Count; i++) Add(pp[i]);
      }
      public void Add(PolyPathD pp)
      {
        Add(pp.Count);
        if (pp.Polygon != null) Add(new PathsD { pp.Polygon });
        for (int i = 0; i < pp.Count; i++) Add(pp[i]);
      }
      public string Value => _h.ToString("x16");
    }

    private static void Run(List<(string, string)> lines, string name, Action<Hasher> body)
    {
      Hasher h = new Hasher();
      try { body(h); }
      catch (Exception ex) { h.Add(ex.GetType().Name.GetHashCode()); h.Add(-12345); }
      lines.Add((name, h.Value));
    }

    // ------------------------------------------------------------------ fuzz corpus

    private static Paths64 RandomPaths(Random r, int mode, int count, int minPts, int maxPts)
    {
      Paths64 result = new Paths64();
      for (int k = 0; k < count; k++)
      {
        int n = r.Next(minPts, maxPts + 1);
        Path64 p = new Path64(n);
        long cx = 0, cy = 0, rad;
        switch (mode)
        {
          case 0: rad = 20; break;                          // tiny grid: coincident / collinear / horizontal
          case 1: rad = 1000; break;
          case 2: rad = 4_000_000_000_000L; break;          // coordinate differences beyond 2^31
          default: rad = 100_000; break;
        }
        if (mode == 3) { cx = r.Next(-200_000, 200_000); cy = r.Next(-200_000, 200_000); }
        for (int i = 0; i < n; i++)
        {
          long x, y;
          if (mode == 3)
          {
            // star-like polygons with shared horizontals
            double a = 2 * Math.PI * i / n;
            double rr = rad * (0.3 + r.NextDouble());
            x = cx + (long) (rr * Math.Cos(a));
            y = (i % 3 == 0 && i > 0) ? p[i - 1].Y : cy + (long) (rr * Math.Sin(a));
          }
          else
          {
            x = (long) ((r.NextDouble() * 2 - 1) * rad);
            y = (long) ((r.NextDouble() * 2 - 1) * rad);
          }
          p.Add(new Point64(x, y));
        }
        result.Add(p);
      }
      return result;
    }

    // ------------------------------------------------------------------ golden

    private static List<(string, string)> BuildGolden()
    {
      List<(string, string)> lines = new List<(string, string)>();
      List<Case> polys = LoadCases(Data("Polygons.txt"));
      List<Case> lineCases = LoadCases(Data("Lines.txt"));
      List<Case> offsetCases = LoadCases(Data("Offsets.txt"));

      foreach (Case c in polys)
      {
        Run(lines, $"poly {c.Num} paths", h =>
        {
          Clipper64 cl = new Clipper64();
          cl.AddSubject(c.Subj); cl.AddOpenSubject(c.SubjOpen); cl.AddClip(c.Clip);
          Paths64 sol = new Paths64(), open = new Paths64();
          h.Add(cl.Execute(c.Ct, c.Fr, sol, open) ? 1 : 0);
          h.Add(sol); h.Add(open);
          // a second operation on the same (pooled) instance
          sol = new Paths64();
          cl.Execute(ClipType.Xor, FillRule.EvenOdd, sol);
          h.Add(sol);
        });
        Run(lines, $"poly {c.Num} tree", h =>
        {
          Clipper64 cl = new Clipper64();
          cl.AddSubject(c.Subj); cl.AddClip(c.Clip);
          PolyTree64 tree = new PolyTree64();
          Paths64 open = new Paths64();
          h.Add(cl.Execute(c.Ct, c.Fr, tree, open) ? 1 : 0);
          h.Add(tree); h.Add(open);
        });
        Run(lines, $"poly {c.Num} reverse+collinear", h =>
        {
          Clipper64 cl = new Clipper64 { ReverseSolution = true, PreserveCollinear = false };
          cl.AddSubject(c.Subj); cl.AddClip(c.Clip);
          Paths64 sol = new Paths64();
          cl.Execute(c.Ct, c.Fr, sol);
          h.Add(sol);
        });
        Run(lines, $"poly {c.Num} clipperD", h =>
        {
          ClipperD cd = new ClipperD(3);
          cd.AddSubject(Clipper.ScalePathsD(c.Subj, 0.013));
          cd.AddClip(Clipper.ScalePathsD(c.Clip, 0.013));
          PathsD sol = new PathsD();
          cd.Execute(c.Ct, c.Fr, sol);
          h.Add(sol);
          PolyTreeD tree = new PolyTreeD();
          cd.Execute(c.Ct, c.Fr, tree);
          h.Add(tree);
        });
        if (c.Num % 3 == 0)
          foreach (JoinType jt in new[] { JoinType.Square, JoinType.Bevel, JoinType.Round, JoinType.Miter })
            foreach (double delta in new[] { -7.0, 4.5, 23.0 })
              Run(lines, $"poly {c.Num} offset {jt} {delta}", h =>
              {
                h.Add(Clipper.InflatePaths(c.Subj, delta, jt, EndType.Polygon));
                if (c.Num % 9 == 0)
                  foreach (EndType et in new[] { EndType.Joined, EndType.Butt, EndType.Square, EndType.Round })
                    h.Add(Clipper.InflatePaths(c.Subj, delta, jt, et));
              });
        Run(lines, $"poly {c.Num} rectclip", h =>
        {
          Rect64 b = Clipper.GetBounds(c.Subj);
          Rect64 r1 = new Rect64(b.left + b.Width / 4, b.top + b.Height / 3, b.right - b.Width / 5, b.bottom - b.Height / 6);
          h.Add(Clipper.RectClip(r1, c.Subj));
          h.Add(Clipper.RectClipLines(r1, c.Subj));
          h.Add(Clipper.RectClip(new Rect64(100, 100, 700, 500), c.Subj));
          h.Add(Clipper.RectClipLines(new Rect64(100, 100, 700, 500), c.Subj));
        });
        Run(lines, $"poly {c.Num} simplify", h =>
        {
          h.Add(Clipper.SimplifyPaths(c.Subj, 0.5));
          h.Add(Clipper.SimplifyPaths(c.Subj, 3.0, false));
          h.Add(Clipper.RamerDouglasPeucker(c.Subj, 2.0));
          foreach (Path64 p in c.Subj) h.Add(new Paths64 { Clipper.TrimCollinear(p) });
        });
        Run(lines, $"poly {c.Num} triangulate", h =>
        {
          Paths64 u = Clipper.Union(c.Subj, FillRule.NonZero);
          h.Add((long) Clipper.Triangulate(u, out Paths64 tri));
          h.Add(tri);
          h.Add((long) Clipper.Triangulate(u, out Paths64 tri2, false));
          h.Add(tri2);
        });
        Run(lines, $"poly {c.Num} pip", h =>
        {
          Random r = new Random(c.Num);
          Rect64 b = Clipper.GetBounds(c.Subj);
          for (int i = 0; i < 300; i++)
          {
            Point64 pt = new Point64(b.left + (long) (r.NextDouble() * (b.Width + 1)),
              b.top + (long) (r.NextDouble() * (b.Height + 1)));
            foreach (Path64 p in c.Subj) h.Add((long) Clipper.PointInPolygon(pt, p));
          }
          // probes exactly on vertices
          foreach (Path64 p in c.Subj) foreach (Point64 v in p) h.Add((long) Clipper.PointInPolygon(v, c.Subj[0]));
        });
        if (c.Num % 5 == 0 && c.Subj.Count > 0)
          Run(lines, $"poly {c.Num} minkowski", h =>
          {
            Path64 pattern = Clipper.MakePath(new long[] { -3, -3, 3, -3, 3, 3, -3, 3 });
            h.Add(Clipper.MinkowskiSum(pattern, c.Subj[0], true));
            h.Add(Clipper.MinkowskiDiff(pattern, c.Subj[0], false));
          });
      }

      foreach (Case c in lineCases)
        Run(lines, $"lines {c.Num}", h =>
        {
          Clipper64 cl = new Clipper64();
          cl.AddSubject(c.Subj); cl.AddOpenSubject(c.SubjOpen); cl.AddClip(c.Clip);
          Paths64 sol = new Paths64(), open = new Paths64();
          cl.Execute(c.Ct, c.Fr, sol, open);
          h.Add(sol); h.Add(open);
          PolyTree64 tree = new PolyTree64();
          open = new Paths64();
          cl.Execute(c.Ct, c.Fr, tree, open);
          h.Add(tree); h.Add(open);
        });

      foreach (Case c in offsetCases)
        foreach (JoinType jt in new[] { JoinType.Square, JoinType.Bevel, JoinType.Round, JoinType.Miter })
          Run(lines, $"offsets {c.Num} {jt}", h =>
          {
            ClipperOffset co = new ClipperOffset(2.0, 0.0);
            co.AddPaths(c.Subj, jt, EndType.Polygon);
            Paths64 sol = new Paths64();
            co.Execute(1, sol); h.Add(sol);
            co.Execute(27, sol); h.Add(sol);
            co.Execute(-4, sol); h.Add(sol);
            PolyTree64 tree = new PolyTree64();
            co.Execute(5, tree); h.Add(tree);
          });

      foreach (string f in new[] { "PolytreeHoleOwner.txt", "PolytreeHoleOwner2.txt" })
        foreach (Case c in LoadCases(Data(f)))
          Run(lines, $"{f} {c.Num}", h =>
          {
            Clipper64 cl = new Clipper64();
            cl.AddSubject(c.Subj); cl.AddOpenSubject(c.SubjOpen); cl.AddClip(c.Clip);
            PolyTree64 tree = new PolyTree64();
            Paths64 open = new Paths64();
            cl.Execute(c.Ct, c.Fr, tree, open);
            h.Add(tree); h.Add(open);
          });

      // the big workloads of the benchmark
      Paths64 all = new Paths64();
      foreach (Case c in polys) all.AddRange(c.Subj);
      Run(lines, "union all subjects", h => h.Add(Clipper.Union(all, FillRule.NonZero)));
      Run(lines, "union all subjects tree", h =>
      {
        PolyTree64 t = new PolyTree64();
        Clipper.BooleanOp(ClipType.Union, FillRule.EvenOdd, all, null, t);
        h.Add(t);
      });
      Run(lines, "offset each subject", h =>
      {
        foreach (Path64 p in all) h.Add(Clipper.InflatePaths(new Paths64 { p }, 20, JoinType.Round, EndType.Polygon));
      });
      Run(lines, "triangulate circle", h =>
      {
        Path64 circle = new Path64();
        for (int i = 0; i < 2000; i++)
        {
          double a = 2 * Math.PI * i / 2000;
          circle.Add(new Point64((long) (100000 * Math.Cos(a)), (long) (100000 * Math.Sin(a))));
        }
        h.Add((long) Clipper.Triangulate(new Paths64 { circle }, out Paths64 tri)); h.Add(tri);
      });

      // deterministic fuzz corpus
      ClipType[] cts = { ClipType.Intersection, ClipType.Union, ClipType.Difference, ClipType.Xor };
      FillRule[] frs = { FillRule.EvenOdd, FillRule.NonZero, FillRule.Positive, FillRule.Negative };
      for (int i = 0; i < 4000; i++)
      {
        int seed = i;
        Run(lines, $"fuzz {i}", h =>
        {
          Random r = new Random(seed);
          int mode = seed % 4;
          Paths64 s = RandomPaths(r, mode, r.Next(1, 5), 3, mode == 0 ? 12 : 40);
          Paths64 c = RandomPaths(r, mode, r.Next(0, 4), 3, mode == 0 ? 12 : 40);
          Paths64 o = (seed % 7 == 0) ? RandomPaths(r, mode, r.Next(1, 3), 2, 10) : new Paths64();
          ClipType ct = cts[r.Next(4)];
          FillRule fr = frs[r.Next(4)];
          Clipper64 cl = new Clipper64();
          cl.AddSubject(s); cl.AddOpenSubject(o); cl.AddClip(c);
          Paths64 sol = new Paths64(), open = new Paths64();
          cl.Execute(ct, fr, sol, open);
          h.Add(sol); h.Add(open);
          if (seed % 2 == 0)
          {
            Clipper64 c2 = new Clipper64();
            c2.AddSubject(s); c2.AddClip(c);
            PolyTree64 tree = new PolyTree64();
            c2.Execute(ct, fr, tree);
            h.Add(tree);
          }
          if (seed % 5 == 0 && mode != 2)
            h.Add(Clipper.InflatePaths(s, (seed % 3 == 0) ? -3 : 6, (JoinType) (seed % 4), EndType.Polygon));
          if (seed % 11 == 0)
          {
            Rect64 b = Clipper.GetBounds(s);
            Rect64 rr = new Rect64(b.left / 2, b.top / 2, b.right / 2, b.bottom / 2);
            h.Add(Clipper.RectClip(rr, s));
            h.Add(Clipper.RectClipLines(rr, s));
          }
          if (seed % 13 == 0)
          {
            h.Add((long) Clipper.Triangulate(sol, out Paths64 tri));
            h.Add(tri);
          }
          if (mode != 3)
            foreach (Path64 p in s)
              for (int k = 0; k < 20; k++)
                h.Add((long) Clipper.PointInPolygon(
                  (k < p.Count) ? p[k] : new Point64(r.NextInt64(-50, 50) * (mode == 2 ? 80_000_000_000L : (mode == 1 ? 20 : 1)),
                    r.NextInt64(-50, 50) * (mode == 2 ? 80_000_000_000L : (mode == 1 ? 20 : 1))), p));
        });
      }
      return lines;
    }

    private static int Golden(string action, string file)
    {
      Stopwatch sw = Stopwatch.StartNew();
      List<(string name, string hash)> lines = BuildGolden();
      Console.WriteLine($"{lines.Count} golden entries computed in {sw.Elapsed.TotalSeconds:0.0} s");
      if (action == "record")
      {
        StringBuilder sb = new StringBuilder();
        foreach ((string n, string h) in lines) sb.Append(h).Append(' ').Append(n).Append('\n');
        File.WriteAllText(file, sb.ToString());
        Console.WriteLine($"recorded {file}");
        return 0;
      }
      Dictionary<string, string> was = new Dictionary<string, string>();
      foreach (string l in File.ReadAllLines(file))
        if (l.Length > 17) was[l.Substring(17)] = l.Substring(0, 16);
      int diff = 0, missing = 0;
      foreach ((string n, string h) in lines)
      {
        if (!was.TryGetValue(n, out string? old)) { missing++; continue; }
        if (old != h)
        {
          if (++diff <= 40) Console.WriteLine($"  DIFF {n}");
        }
      }
      Console.WriteLine($"golden verify: {diff} differ, {missing} new, {lines.Count - diff - missing} identical");
      return diff == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ fidelity vs C++

    private static int Fidelity(string cppDump, string? portDump)
    {
      List<Case> polys = LoadCases(Data("Polygons.txt"));
      List<Case> offsets = LoadCases(Data("Offsets.txt"));
      List<string> mine = new List<string>();
      foreach (Case c in polys)
      {
        Clipper64 cl = new Clipper64();
        cl.AddSubject(c.Subj);
        if (c.Clip.Count > 0) cl.AddClip(c.Clip);
        Paths64 sol = new Paths64();
        cl.Execute(c.Ct, c.Fr, sol);
        mine.Add("boolean " + c.Num + " " + sol.Count + " " +
          Clipper.Area(sol).ToString("F6", CultureInfo.InvariantCulture));
      }
      foreach (Case c in offsets)
      {
        ClipperOffset co = new ClipperOffset(2.0, 0.0);
        co.AddPaths(c.Subj, JoinType.Round, EndType.Polygon);
        Paths64 sol = new Paths64();
        co.Execute(1.0, sol);
        mine.Add("offset " + c.Num + " " + sol.Count + " " +
          Clipper.Area(sol).ToString("F6", CultureInfo.InvariantCulture));
      }
      if (portDump != null) File.WriteAllText(portDump, string.Join("\n", mine) + "\n");
      string[] cpp = File.ReadAllLines(cppDump);
      int same = 0, differ = 0;
      for (int i = 0; i < Math.Min(cpp.Length, mine.Count); i++)
      {
        if (cpp[i].Trim() == mine[i]) same++;
        else { differ++; Console.WriteLine($"  cpp: {cpp[i]}   port: {mine[i]}"); }
      }
      Console.WriteLine($"fidelity vs C++: {same} identical, {differ} differ (of {mine.Count})");
      return differ == 0 && cpp.Length == mine.Count ? 0 : 1;
    }

    // ------------------------------------------------------------------ workloads

    private static int Workload(string mode, int reps)
    {
      List<Case> polys = LoadCases(Data("Polygons.txt"));
      List<Case> offsets = LoadCases(Data("Offsets.txt"));
      Paths64 all = new Paths64();
      foreach (Case c in polys) all.AddRange(c.Subj);
      Rect64 b = Clipper.GetBounds(all);
      Random rnd = new Random(12345);
      Point64[] probes = new Point64[200_000];
      for (int i = 0; i < probes.Length; i++)
        probes[i] = new Point64(b.left + (long) (rnd.NextDouble() * (b.right - b.left)),
          b.top + (long) (rnd.NextDouble() * (b.bottom - b.top)));
      Path64 circle = new Path64();
      for (int i = 0; i < 2000; i++)
      {
        double a = 2 * Math.PI * i / 2000;
        circle.Add(new Point64((long) (100000 * Math.Cos(a)), (long) (100000 * Math.Sin(a))));
      }
      // the benchmark's 'moved apart' input: every subject path shifted far away
      Paths64 apart = new Paths64(all.Count);
      for (int i = 0; i < all.Count; i++) apart.Add(Clipper.TranslatePath(all[i], i * 100_000L, i * 100_000L));

      Func<long> work = mode switch
      {
        "union" => () => Clipper.Union(all, FillRule.NonZero).Count,
        "bool" => () =>
        {
          long n = 0;
          foreach (Case c in polys)
          {
            Clipper64 cl = new Clipper64();
            cl.AddSubject(c.Subj); cl.AddClip(c.Clip);
            Paths64 sol = new Paths64();
            cl.Execute(c.Ct, c.Fr, sol);
            n += sol.Count;
          }
          return n;
        },
        "offset" => () =>
        {
          long n = 0;
          foreach (Case c in offsets) n += Clipper.InflatePaths(c.Subj, 1, JoinType.Round, EndType.Polygon).Count;
          foreach (Case c in offsets) n += Clipper.InflatePaths(c.Subj, 27, JoinType.Miter, EndType.Polygon).Count;
          foreach (Path64 p in all) n += Clipper.InflatePaths(new Paths64 { p }, 20, JoinType.Round, EndType.Polygon).Count;
          return n;
        },
        "offset1" => () =>
        {
          long n = 0;
          foreach (Case c in offsets) n += Clipper.InflatePaths(c.Subj, 1, JoinType.Round, EndType.Polygon).Count;
          return n;
        },
        "offset27" => () =>
        {
          long n = 0;
          foreach (Case c in offsets) n += Clipper.InflatePaths(c.Subj, 27, JoinType.Miter, EndType.Polygon).Count;
          return n;
        },
        "offsetgroups" => () =>
        {
          long n = 0;
          foreach (Path64 p in all) n += Clipper.InflatePaths(new Paths64 { p }, 20, JoinType.Round, EndType.Polygon).Count;
          return n;
        },
        "rect" => () =>
        {
          long n = 0;
          Rect64 r = new Rect64(100, 100, 700, 500);
          foreach (Case c in polys) n += Clipper.RectClip(r, c.Subj).Count + Clipper.RectClipLines(r, c.Subj).Count;
          return n;
        },
        "tri" => () => { Clipper.Triangulate(new Paths64 { circle }, out Paths64 t); return t.Count; },
        "simplify" => () => Clipper.SimplifyPaths(all, 0.5).Count,
        "apart" => () => Clipper.Union(apart, FillRule.NonZero).Count,
        "apartpar" => () => Clipper.BooleanOpParallel(ClipType.Union, FillRule.NonZero, apart).Count,
        "pipbatch" => () =>
        {
          long hits = 0;
          PointInPolygonResult[] res = new PointInPolygonResult[probes.Length];
          foreach (Path64 p in all)
          {
            Clipper.PointInPolygon(p, probes, res);
            foreach (PointInPolygonResult r in res) if (r != PointInPolygonResult.IsOutside) hits++;
          }
          return hits;
        },
        "pip" => () =>
        {
          long hits = 0;
          foreach (Point64 pt in probes)
            foreach (Path64 p in all)
              if (Clipper.PointInPolygon(pt, p) != PointInPolygonResult.IsOutside) hits++;
          return hits;
        },
        _ => throw new ArgumentException("unknown mode " + mode)
      };

      // warm up (tiered JIT + PGO need a few hundred calls of the small workloads)
      Stopwatch warm = Stopwatch.StartNew();
      do work(); while (warm.Elapsed.TotalMilliseconds < 700);
      double best = double.MaxValue, total = 0;
      long result = 0;
      long bytes = GC.GetTotalAllocatedBytes(true);
      int done = 0;
      Stopwatch wall = Stopwatch.StartNew();
      // at least 'reps' repetitions and at least 1.5 s of measuring
      while (done < reps || wall.Elapsed.TotalMilliseconds < 1500)
      {
        Stopwatch sw = Stopwatch.StartNew();
        result = work();
        sw.Stop();
        total += sw.Elapsed.TotalMilliseconds;
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        done++;
      }
      reps = done;
      bytes = GC.GetTotalAllocatedBytes(true) - bytes;
      Console.WriteLine($"{mode}: result {result}, best {best:0.000} ms, mean {total / reps:0.000} ms, " +
        $"{bytes / (double) reps / 1048576.0:0.000} MB/rep");
      return 0;
    }
  }
}
