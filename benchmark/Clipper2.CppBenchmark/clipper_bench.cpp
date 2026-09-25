// Clipper2 C++ benchmark - mirrors benchmark/Clipper2.Benchmark so that the native
// implementation and the C# port can be compared on identical workloads.
//
// Allocation accounting: global operator new/delete are replaced with counting
// versions, the C++ analogue of GC.GetTotalAllocatedBytes.
//
// Build (MinGW g++ 13.1, Clipper2 C++ checkout at CLIPPER2_CPP):
//   PATH=<mingw>/bin:$PATH
//   g++ -O3 -DNDEBUG -march=native -std=c++17 -static \
//       -I $CLIPPER2_CPP/Clipper2Lib/include -o clipper_bench.exe clipper_bench.cpp \
//       $CLIPPER2_CPP/Clipper2Lib/src/clipper.engine.cpp \
//       $CLIPPER2_CPP/Clipper2Lib/src/clipper.offset.cpp \
//       $CLIPPER2_CPP/Clipper2Lib/src/clipper.rectclip.cpp \
//       $CLIPPER2_CPP/Clipper2Lib/src/clipper.triangulation.cpp
//
//   clipper_bench.exe                 # timings + allocation per workload
//   clipper_bench.exe --dump out.txt  # per case results (count + area) for a diff
//                                     # against the C# port's dump
//
// nb: the C++ comes in two numeric flavours - CLIPPER2_HI_PRECISION adds the
// origin-shifted GetLineIntersectPt. This benchmark (like the port) uses the
// default build; on GCC/Clang both use __int128 for the sign tests.
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <new>
#include <sstream>
#include <string>
#include <vector>

#include "clipper2/clipper.h"
#include "clipper2/clipper.triangulation.h"

using namespace Clipper2Lib;

// ------------------------------------------------------------------ allocation
static size_t g_allocated = 0;
void* operator new(size_t n)
{
  g_allocated += n;
  void* p = std::malloc(n);
  if (!p) throw std::bad_alloc();
  return p;
}
void* operator new[](size_t n)
{
  g_allocated += n;
  void* p = std::malloc(n);
  if (!p) throw std::bad_alloc();
  return p;
}
void operator delete(void* p) noexcept { std::free(p); }
void operator delete[](void* p) noexcept { std::free(p); }
void operator delete(void* p, size_t) noexcept { std::free(p); }
void operator delete[](void* p, size_t) noexcept { std::free(p); }

// ------------------------------------------------------------------ test data
struct ClipCase
{
  int number = 0;
  ClipType clipType = ClipType::NoClip;
  FillRule fillRule = FillRule::NonZero;
  std::vector<std::vector<int64_t>> subjects, clips;
};

static bool ParseCoords(const std::string& line, std::vector<int64_t>& out)
{
  size_t i = 0;
  while (i < line.size())
  {
    while (i < line.size() && (line[i] < 33 || line[i] == ',')) i++;
    if (i >= line.size()) break;
    bool neg = line[i] == '-';
    if (neg) i++;
    size_t start = i;
    int64_t v = 0;
    while (i < line.size() && line[i] >= '0' && line[i] <= '9') v = v * 10 + (line[i++] - '0');
    if (i == start) break;
    out.push_back(neg ? -v : v);
  }
  return !out.empty();
}

static std::vector<ClipCase> LoadCases(const std::string& file)
{
  std::vector<ClipCase> result;
  std::ifstream in(file);
  if (!in) { std::printf("cannot open %s\n", file.c_str()); std::exit(1); }
  std::string line;
  ClipCase* cur = nullptr;
  int section = 0;
  while (std::getline(in, line))
  {
    if (!line.empty() && line.back() == '\r') line.pop_back();
    if (line.rfind("CAPTION: ", 0) == 0)
    {
      result.push_back(ClipCase());
      cur = &result.back();
      cur->number = (int) result.size();
      section = 0;
      continue;
    }
    if (!cur) continue;
    if (line.rfind("CLIPTYPE: ", 0) == 0)
    {
      std::string s = line.substr(10);
      s.erase(std::remove_if(s.begin(), s.end(), [](char c) { return c == ' ' || c == '\t'; }), s.end());
      cur->clipType = (s == "INTERSECTION") ? ClipType::Intersection :
        (s == "UNION") ? ClipType::Union :
        (s == "DIFFERENCE") ? ClipType::Difference : ClipType::Xor;
      continue;
    }
    if (line.rfind("FILLRULE: ", 0) == 0 || line.rfind("FILLTYPE: ", 0) == 0)
    {
      std::string s = line.substr(10);
      s.erase(std::remove_if(s.begin(), s.end(), [](char c) { return c == ' ' || c == '\t'; }), s.end());
      cur->fillRule = (s == "EVENODD") ? FillRule::EvenOdd :
        (s == "NONZERO") ? FillRule::NonZero :
        (s == "POSITIVE") ? FillRule::Positive : FillRule::Negative;
      continue;
    }
    if (line.rfind("SOL_", 0) == 0) continue;
    if (line.rfind("SUBJECTS_OPEN", 0) == 0) { section = 2; continue; }
    if (line.rfind("SUBJECTS", 0) == 0) { section = 1; continue; }
    if (line.rfind("CLIPS", 0) == 0) { section = 3; continue; }
    if (section != 1 && section != 3) continue;
    if (line.empty() || line[0] < 33) continue;
    std::vector<int64_t> pts;
    if (!ParseCoords(line, pts) || pts.size() < 6) continue;
    (section == 1 ? cur->subjects : cur->clips).push_back(std::move(pts));
  }
  return result;
}

// path construction mirrors the C# adapters (built inside the measured lambda)
static Paths64 ToPaths(const std::vector<std::vector<int64_t>>& raw)
{
  Paths64 paths;
  paths.reserve(raw.size());
  for (const auto& p : raw)
  {
    Path64 path;
    path.reserve(p.size() / 2);
    for (size_t i = 0; i + 1 < p.size(); i += 2) path.emplace_back(p[i], p[i + 1]);
    paths.push_back(std::move(path));
  }
  return paths;
}

static std::vector<std::vector<int64_t>> Flatten(const std::vector<ClipCase>& cases, bool clips)
{
  std::vector<std::vector<int64_t>> all;
  for (const auto& c : cases)
    for (const auto& p : (clips ? c.clips : c.subjects)) all.push_back(p);
  return all;
}

// ------------------------------------------------------------------ measurement
struct Stat { double ms = 0; double mb = 0; };

template <typename F>
static Stat Measure(F body, int minReps = 2)
{
  body(); // warm-up
  long long totalNs = 0;
  double bestMs = -1;
  size_t before = g_allocated;
  int reps = 0;
  auto t0 = std::chrono::steady_clock::now();
  while (true)
  {
    auto r0 = std::chrono::steady_clock::now();
    body();
    auto r1 = std::chrono::steady_clock::now();
    double ms = std::chrono::duration<double, std::milli>(r1 - r0).count();
    if (reps > 0 && (bestMs < 0 || ms < bestMs)) bestMs = ms; // skip the cache-cold first rep
    totalNs += (long long) std::chrono::duration_cast<std::chrono::nanoseconds>(r1 - r0).count();
    reps++;
    if (reps >= minReps && std::chrono::duration<double, std::milli>(r1 - t0).count() >= 400.0) break;
    if (reps > 100000) break;
  }
  size_t allocated = g_allocated - before;
  Stat st;
  st.ms = bestMs >= 0 ? bestMs : (double) totalNs / 1e6 / reps;
  st.mb = (double) allocated / (1024.0 * 1024.0) / reps;
  return st;
}

struct Result { long long count = 0; double area = 0; };

static void Report(const char* label, const Stat& st, const Result& r)
{
  std::printf("%-58s cpp %9.2f ms / %7.2f MB   %lld paths / area %.0f\n",
    label, st.ms, st.mb, r.count, r.area);
}

// ------------------------------------------------------------------ workloads
static void DumpCases(const std::string& dir, const char* outFile)
{
  std::vector<ClipCase> polygons = LoadCases(dir + "Polygons.txt");
  std::vector<ClipCase> offsets = LoadCases(dir + "Offsets.txt");
  FILE* f = std::fopen(outFile, "w");
  if (!f) { std::printf("cannot write %s\n", outFile); std::exit(1); }
  for (const auto& c : polygons)
  {
    Clipper64 clipper;
    clipper.AddSubject(ToPaths(c.subjects));
    if (!c.clips.empty()) clipper.AddClip(ToPaths(c.clips));
    Paths64 sol;
    clipper.Execute(c.clipType, c.fillRule, sol);
    std::fprintf(f, "boolean %d %zu %.6f\n", c.number, sol.size(), Area(sol));
  }
  for (const auto& c : offsets)
  {
    ClipperOffset co(2.0, 0.0);
    co.AddPaths(ToPaths(c.subjects), JoinType::Round, EndType::Polygon);
    Paths64 sol;
    co.Execute(1.0, sol);
    std::fprintf(f, "offset %d %zu %.6f\n", c.number, sol.size(), Area(sol));
  }
  std::fclose(f);
  std::printf("wrote %s\n", outFile);
}

int main(int argc, char** argv)
{
  const std::string dir = "E:\\Learning\\AI\\Clipper2\\Tests\\";
  if (argc > 2 && std::string(argv[1]) == "--dump")
  {
    DumpCases(dir, argv[2]);
    return 0;
  }
  std::vector<ClipCase> polygons = LoadCases(dir + "Polygons.txt");
  std::vector<ClipCase> offsets = LoadCases(dir + "Offsets.txt");
  auto allSubjects = Flatten(polygons, false);
  auto allSubjectsAndClips = Flatten(polygons, false);
  {
    auto clips = Flatten(polygons, true);
    allSubjectsAndClips.insert(allSubjectsAndClips.end(), clips.begin(), clips.end());
  }
  std::printf("loaded %zu polygon cases and %zu offset cases (%zu subject paths)\n\n",
    polygons.size(), offsets.size(), allSubjects.size());

  // ---- boolean ops over every case
  {
    Result res;
    Stat st = Measure([&] {
      long long count = 0; double area = 0;
      for (const auto& c : polygons)
      {
        Clipper64 clipper;
        clipper.AddSubject(ToPaths(c.subjects));
        if (!c.clips.empty()) clipper.AddClip(ToPaths(c.clips));
        Paths64 sol;
        clipper.Execute(c.clipType, c.fillRule, sol);
        count += (long long) sol.size();
        area += Area(sol);
      }
      res = { count, area };
    });
    Report("boolean ops (Polygons.txt, all cases)", st, res);
  }

  // ---- union: ingestion only
  {
    Result res;
    Stat st = Measure([&] {
      Clipper64 clipper;
      clipper.AddSubject(ToPaths(allSubjects));
      res = { 0, 0 };
    });
    Report("union: add subject paths only (ingestion)", st, res);
  }

  // ---- union of every subject path
  {
    Result res;
    Stat st = Measure([&] {
      Clipper64 clipper;
      clipper.AddSubject(ToPaths(allSubjects));
      Paths64 sol;
      clipper.Execute(ClipType::Union, FillRule::NonZero, sol);
      res = { (long long) sol.size(), Area(sol) };
    });
    Report("union of every subject path (Paths64)", st, res);
  }

  // ---- offsets (delta = 1, Round/Polygon)
  for (double delta : { 1.0, 27.0 })
  {
    const char* label = delta < 2 ? "offsets (Offsets.txt, delta = 1, Round/Polygon)"
                                  : "offsets (Offsets.txt, delta = 27, Miter/Polygon)";
    JoinType jt = delta < 2 ? JoinType::Round : JoinType::Miter;
    Result res;
    Stat st = Measure([&] {
      long long count = 0; double area = 0;
      for (const auto& c : offsets)
      {
        ClipperOffset co(2.0, 0.0);
        co.AddPaths(ToPaths(c.subjects), jt, EndType::Polygon);
        Paths64 sol;
        co.Execute(delta, sol);
        count += (long long) sol.size();
        area += Area(sol);
      }
      res = { count, area };
    });
    Report(label, st, res);
  }

  // ---- offset: every subject path as its own group
  {
    Result res;
    Stat st = Measure([&] {
      long long count = 0; double area = 0;
      for (const auto& g : allSubjects)
      {
        ClipperOffset co(2.0, 0.0);
        co.AddPaths(ToPaths({ g }), JoinType::Round, EndType::Polygon);
        Paths64 sol;
        co.Execute(20, sol);
        count += (long long) sol.size();
        area += Area(sol);
      }
      res = { count, area };
    });
    Report("offset: every subject path as its own group", st, res);
  }

  // ---- rect clip (all subjects into 100,100,700,500)
  {
    Rect64 rect(100, 100, 700, 500);
    Result res;
    Stat st = Measure([&] {
      long long count = 0; double area = 0;
      for (const auto& c : polygons)
      {
        Paths64 sol = RectClip(rect, ToPaths(c.subjects));
        count += (long long) sol.size();
        area += Area(sol);
      }
      res = { count, area };
    });
    Report("rect clip (all subjects into 100,100,700,500)", st, res);
  }

  // ---- rect clip lines
  {
    Rect64 rect(100, 100, 700, 500);
    Result res;
    Stat st = Measure([&] {
      long long count = 0; double area = 0;
      for (const auto& c : polygons)
      {
        Paths64 sol = RectClipLines(rect, ToPaths(c.subjects));
        count += (long long) sol.size();
        area += Area(sol);
      }
      res = { count, area };
    });
    Report("rect clip lines (all subjects into 100,100,700,500)", st, res);
  }

  // ---- rect clip: every subject path separately
  {
    Rect64 rect(100, 100, 700, 500);
    Result res;
    Stat st = Measure([&] {
      long long count = 0; double area = 0;
      for (const auto& g : allSubjects)
      {
        Paths64 sol = RectClip(rect, ToPaths({ g }));
        count += (long long) sol.size();
        area += Area(sol);
      }
      res = { count, area };
    });
    Report("rect clip: every subject path separately", st, res);
  }

  // ---- simplify paths
  {
    Result res;
    Stat st = Measure([&] {
      Paths64 sol = SimplifyPaths(ToPaths(allSubjects), 0.5);
      res = { (long long) sol.size(), Area(sol) };
    });
    Report("simplify paths (all subjects, eps = 0.5)", st, res);
  }

  // ---- triangulate a 2000 vertex polygon (the same circle the C# benchmark uses)
  {
    const int steps = 2000;
    Paths64 circlePaths;
    Path64 circle;
    circle.reserve(steps);
    for (int i = 0; i < steps; i++)
    {
      double a = 2 * PI * i / steps;
      circle.emplace_back((int64_t) (100000 * std::cos(a)), (int64_t) (100000 * std::sin(a)));
    }
    circlePaths.push_back(circle);
    Result res;
    Stat st = Measure([&] {
      Paths64 sol;
      Triangulate(circlePaths, sol);
      res = { (long long) sol.size(), Area(sol) };
    });
    Report("triangulate 2000 vertex polygon", st, res);
  }

  // ---- area / bounds of every path
  {
    Result res;
    Stat st = Measure([&] {
      Paths64 p = ToPaths(allSubjectsAndClips);
      res = { (long long) p.size(), Area(p) };
    });
    Report("area (Paths64 of every subject+clip)", st, res);
  }
  {
    Result res;
    Stat st = Measure([&] {
      Rect64 r = GetBounds(ToPaths(allSubjectsAndClips));
      res = { r.Width() + r.Height(), 0 };
    });
    Report("get bounds (Paths64 of every subject+clip)", st, res);
  }

  // ---- point in polygon (200k probes)
  {
    Paths64 paths = ToPaths(allSubjects);
    long long minX = INT64_MAX, maxX = INT64_MIN, minY = INT64_MAX, maxY = INT64_MIN;
    for (const auto& p : paths) for (const auto& pt : p)
    {
      minX = std::min(minX, pt.x); maxX = std::max(maxX, pt.x);
      minY = std::min(minY, pt.y); maxY = std::max(maxY, pt.y);
    }
    const int n = 200000;
    std::vector<int64_t> xs(n), ys(n);
    uint64_t seed = 0x9E3779B97F4A7C15ull;
    auto next = [&seed]() { seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17; return seed; };
    for (int i = 0; i < n; i++)
    {
      xs[i] = minX + (int64_t) (next() % (uint64_t) (maxX - minX + 1));
      ys[i] = minY + (int64_t) (next() % (uint64_t) (maxY - minY + 1));
    }
    Result res;
    Stat st = Measure([&] {
      long long hits = 0;
      for (int i = 0; i < n; i++)
      {
        Point64 pt(xs[i], ys[i]);
        for (const auto& path : paths)
          if (PointInPolygon(pt, path) != PointInPolygonResult::IsOutside) hits++;
      }
      res = { hits, 0 };
    });
    Report("point in polygon (200k probes)", st, res);
  }

  // ---- scale round trip
  {
    Result res;
    Stat st = Measure([&] {
      int err = 0;
      Paths64 p = ToPaths(allSubjects);
      PathsD d = ScalePaths<double, int64_t>(p, 1000.0, err);
      Paths64 back = ScalePaths<int64_t, double>(d, 1.0 / 1000.0, err);
      res = { (long long) back.size(), Area(back) };
    });
    Report("scale round trip (Paths64 -> PathsD -> Paths64, x1000)", st, res);
  }

  return 0;
}
