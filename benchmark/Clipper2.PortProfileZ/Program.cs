// Z golden: hashes Z-aware results (X, Y and Z of every output point) of
// booleans with a Z callback, open paths, polytrees, offsets (whose union
// forwards Z) and ClipperD, so the USINGZ flavour is guarded like the 2D one.
//
//   dotnet run -c Release --project benchmark/Clipper2.PortProfileZ            # verify
//   dotnet run -c Release --project benchmark/Clipper2.PortProfileZ -- record  # re-record
using System;
using System.IO;
using Clipper2ZLib;

ulong h = 14695981039346656037UL;
void Add(long v) { ulong x = (ulong) v; for (int i = 0; i < 8; i++) { h ^= x & 0xFF; h *= 1099511628211UL; x >>= 8; } }
void AddPaths(Paths64 ps) { Add(ps.Count); foreach (Path64 p in ps) { Add(p.Count); foreach (Point64 pt in p) { Add(pt.X); Add(pt.Y); Add(pt.Z); } } }
void AddPathsD(PathsD ps) { Add(ps.Count); foreach (PathD p in ps) { Add(p.Count); foreach (PointD pt in p) { Add(BitConverter.DoubleToInt64Bits(pt.x)); Add(BitConverter.DoubleToInt64Bits(pt.y)); Add(pt.z); } } }
void AddTree(PolyPath64 pp) { Add(pp.Count); if (pp.Polygon != null) AddPaths(new Paths64 { pp.Polygon }); for (int i = 0; i < pp.Count; i++) AddTree(pp[i]); }

Paths64 Rand(Random r, int count, int pts, long rad, long zBase)
{
  Paths64 res = new Paths64();
  for (int k = 0; k < count; k++)
  {
    Path64 p = new Path64();
    for (int i = 0; i < pts; i++)
      p.Add(new Point64(r.NextInt64(-rad, rad), r.NextInt64(-rad, rad), zBase + k * 1000 + i));
    res.Add(p);
  }
  return res;
}

int n = 0;
for (int seed = 0; seed < 1500; seed++)
{
  Random r = new Random(seed);
  long rad = (seed % 3) switch { 0 => 12, 1 => 1000, _ => 100000 };
  Paths64 s = Rand(r, r.Next(1, 4), r.Next(3, 25), rad, 1);
  Paths64 c = Rand(r, r.Next(1, 3), r.Next(3, 25), rad, 500000);
  Clipper64 cl = new Clipper64();
  cl.DefaultZ = -7;
  cl.SetZCallback((Point64 b1, Point64 t1, Point64 b2, Point64 t2, ref Point64 ip) =>
    ip.Z = b1.Z * 3 + t1.Z * 5 + b2.Z * 7 + t2.Z * 11 + (ip.Z == -7 ? 1 : 0));
  cl.AddSubject(s);
  cl.AddClip(c);
  if (seed % 5 == 0) cl.AddOpenSubject(Rand(r, 1, 6, rad, 900000));
  Paths64 sol = new Paths64(), open = new Paths64();
  cl.Execute((ClipType) (1 + seed % 4), (FillRule) (seed % 4), sol, open);
  AddPaths(sol); AddPaths(open);
  if (seed % 3 == 0)
  {
    PolyTree64 tree = new PolyTree64();
    cl.Execute(ClipType.Union, FillRule.NonZero, tree);
    AddTree(tree);
  }
  if (seed % 4 == 0 && rad > 12)
  {
    ClipperOffset co = new ClipperOffset();
    co.ZCallback = (Point64 b1, Point64 t1, Point64 b2, Point64 t2, ref Point64 ip) => ip.Z = b1.Z + t2.Z;
    co.AddPaths(s, JoinType.Round, EndType.Polygon);
    Paths64 off = new Paths64();
    co.Execute(rad / 20.0 + 1, off);
    AddPaths(off);
  }
  if (seed % 7 == 0)
  {
    ClipperD cd = new ClipperD(2);
    cd.SetZCallback((PointD b1, PointD t1, PointD b2, PointD t2, ref PointD ip) => ip.z = b1.z + b2.z + 1);
    cd.AddSubject(Clipper.ScalePathsD(s, 0.01));
    cd.AddClip(Clipper.ScalePathsD(c, 0.01));
    PathsD sd = new PathsD();
    cd.Execute(ClipType.Intersection, FillRule.NonZero, sd);
    AddPathsD(sd);
  }
  n++;
}
string hash = h.ToString("x16");
Console.WriteLine($"{n} cases, hash {hash}");
string? dir = AppContext.BaseDirectory;
while (dir != null && !File.Exists(Path.Combine(dir, "Clipper2Sharp.slnx"))) dir = Path.GetDirectoryName(dir);
string file = Path.Combine(dir ?? ".", "Results", "golden-z-hash.txt");
if (args.Length > 0 && args[0] == "record")
{
  File.WriteAllText(file, hash + "\n");
  Console.WriteLine($"recorded {file}");
  return 0;
}
string expected = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
bool same = expected == hash;
Console.WriteLine(same ? "Z golden: identical" : $"Z golden: DIFFERS (recorded {expected})");
return same ? 0 : 1;
