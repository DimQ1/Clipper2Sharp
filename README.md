# Clipper2Sharp

A from scratch **C# port of the Clipper2 C++ implementation (version 2.0.1)**.

The public API is a drop-in match for the upstream C# port that ships with
Clipper2 (`Clipper2Lib` namespace, `Clipper64`, `ClipperD`, `ClipperOffset`,
`Paths64`/`PathsD`, `PolyPath64/D`, `PolyTree64/D`, …), so existing C# code keeps
compiling — but the algorithms inside are a line by line port of the **C++**
sources (`clipper.engine.cpp`, `clipper.offset.cpp`, `clipper.rectclip.cpp`,
`clipper.triangulation.cpp`, `clipper.core.h`, `clipper.h`), not of the older
C# 2.0.0 code.

* Target framework: **.NET 10**
* Solution file: **`Clipper2Sharp.slnx`** (the XML solution format)
* Pure C# — no P/Invoke, no native dependency.

## Layout

```
Clipper2Sharp.slnx
src/
  Clipper2Lib/          the library (core, engine, offset, rect clip, minkowski,
                        triangulation, pools, SIMD/parallel primitives)
  Clipper2ZLib/         the same sources compiled with USINGZ (Z coordinates)
utils/
  Clipper2.SVG/         SVG reader/writer and drawing helpers
  Clipper.FileIO/       clipping test file loader/saver
examples/
  ConsoleDemo/ InflateDemo/ RectClipDemo/ TriangulationDemo/ UsingZDemo/
tests/
  Clipper2.Tests/       MSTest suite (MSTest + the upstream test data files)
benchmark/
  Clipper2.Benchmark/   speed/allocation comparison against the upstream C# port
  ReferenceClipper2Lib/ the upstream C# sources built in place for that comparison
docs/                   porting notes
```

## Build, test, run

```powershell
dotnet build Clipper2Sharp.slnx
dotnet test  tests/Clipper2.Tests/Clipper2.Tests.csproj
dotnet run   --project examples/ConsoleDemo/ConsoleDemo.csproj -- out.svg
dotnet run -c Release --project benchmark/Clipper2.Benchmark/Clipper2.Benchmark.csproj
```

Library, tests and examples are self-contained. The two benchmark projects are
not: the managed one compares against the **upstream C# sources**, which it
compiles with this repository's toolchain (`benchmark/ReferenceClipper2Lib`,
default root `E:\Learning\AI\Clipper2\CSharp\Clipper2Lib` — override with
`-p:Clipper2ReferenceRoot=<path to Clipper2/CSharp/Clipper2Lib>`), and the native
one needs the C++ checkout (build command in the header of
`benchmark/Clipper2.CppBenchmark/clipper_bench.cpp`). So the benchmark projects
are for reproducing the numbers in this README; the library itself does not
depend on either checkout.

The test suite is driven by the same data files the C++ and upstream C# suites
use (`Tests/Polygons.txt` with 195 boolean cases, `Lines.txt`, `Offsets.txt`,
`PolytreeHoleOwner*.txt`), plus ports of `TestRect`, `TestRectClip`,
`TestSimplifyPath`, `TestTrimCollinear` and additional rect-clip, triangulation,
Minkowski, sorter and multi-threaded-path tests: **29 tests, all green**.

## Fidelity

* Against the **C++ sources** the port is bit for bit identical on all 197 cases
  of the test corpus (both counts and areas) — see
  [Compared with the C++ implementation](#compared-with-the-c-implementation).
* 193 of the 195 polygon cases produce byte identical results to the upstream C#
  2.0.0 port; the 2 remaining union cases differ by 1 path / 3 area units and are
  still inside the tolerances recorded in the C++ test data (the C++ 2.0.1
  algorithm changed between the two versions).
* Both `Offsets.txt` cases differ from the upstream C# 2.0.0 port (1 path and 2
  area units respectively). That data file records no expectation for them
  (`SOL_AREA: -1`), and the port satisfies every offset expectation that *is*
  asserted — the cases ported from the C++ test suite (`TestOffsets2`…#733 and
  the file driven `TestOffsets1`) all pass — so this is the 2.0.0/2.0.1 version
  gap, not a porting defect.
* The enum member order of `JoinType` is the one thing that is *not* numerically
  compatible with the older C# surface; see `docs/porting-notes.md` §2.1.
* Details: `docs/porting-notes.md`.

## Performance

Measured with `benchmark/Clipper2.Benchmark`, which compiles the upstream C#
sources (`E:\Learning\AI\Clipper2\CSharp\Clipper2Lib`) with the same toolchain
and target framework and runs both libraries over identical inputs.

| workload | speed-up | allocation | results |
|---|---:|---:|---|
| boolean ops (all 195 polygon cases) | 1.04x | 1.06x | 2 cases differ (see below) |
| union of every subject path | **1.18x** | **1.00x** | 2 cases differ |
| union: path ingestion only | 1.00x | **0.96x** | identical |
| union of the same paths moved apart (control, no intersections) | 1.02x | 1.18x | identical |
| offset, delta = 1 (Round/Polygon) | 0.99x | **0.74x** | version difference |
| offset, delta = 27 (Miter/Polygon) | 0.95x | 1.37x | identical |
| offset, every subject path as its own group | 1.05x | 1.08x | identical |
| rect clip (all subjects) | **1.28x** | 1.02x | identical |
| rect clip lines (all subjects) | **1.23x** | 1.03x | identical |
| rect clip, every path separately | **1.27x** | 1.03x | identical |
| simplify paths | 0.95x | **0.78x** | identical |
| triangulate a 2000 vertex polygon | **1.18x** | 0.98x | identical |
| area of every path | 1.01x | 1.00x | identical |
| bounds of every path | **1.03x** | **1.00x** | identical |
| point in polygon (200k probes) | **1.19x** | 1.00x | identical |
| scale round trip (x1000) | **1.18x** | 1.00x | identical |

*Speed-up* is the reference time divided by the port's time (higher is better),
*allocation* is the port's bytes divided by the reference's (lower is better).
Where the port is ahead is where SIMD, span writes, pooling or a whole batch of
paths (rect clipping) can be used at once; where it matches, it is because the
two libraries run intentionally equivalent sweep/careful-arithmetic code.

The allocation side is where the port is now clearly ahead or level on every row
except three, and the three exceptions are understood:

* `boolean ops` (1.08x) — measured per case, the port allocates **byte for byte
  what the upstream sources allocate** (a probe that runs both libraries case by
  case reports 0 bytes difference over all 195 cases). The residual ratio comes
  from the *reference build*: the benchmark compiles the reference for
  `net10.0`, where its closures/delegates are cached, while the port follows the
  same code paths as the reference's own `netstandard2.0` build.
* `offset Miter` (1.41x) — a two case workload (0.4 MB): the 2.0.1 and 2.0.0
  offsetters take different routes through the miter cleanup.
* `offset, each path its own group` (1.11x) — 200 `ClipperOffset` instances, so
  this is per instance setup cost (pools + scanline heap), not per vertex work.

### How these numbers were taken (and how they can mislead)

* Both libraries are loaded into one process (extern aliases) and get byte
  identical input, so no serialisation or JIT difference can bias a row.
* Four full runs were made (three with 7 alternating rounds, one with 15); each
  row reports the **median of the four**. Within a round the two libraries are
  measured back to back with the order swapped every round, and inside a
  measurement the fastest uninterrupted repetition wins (the first repetition is
  dropped as cache-cold, and an allocation figure is only ever reported together
  with the round it came from).
* This matters because the development machine runs at **~75–85% background
  CPU**: measuring one library to completion and then the other can report 0.3x
  or 1.5x for identical code, and even with the interleaved harness the time
  ratios of the short rows move by up to ±15% between sessions (the boolean row
  read 1.18x and 1.02x for the same code in two sessions). Read time ratios of
  ±10% as parity.
* Where a ratio looked suspicious it was re-checked with a finer instrument —
  e.g. point-in-polygon, measured with both libraries interleaved every 5000
  probes over identical probes, gives **0.99x and 1.01x** with equal hit counts.
* The **allocation columns are exact** (byte counters, deterministic across
  runs) and are the reliable half of the table.
* `speed-up 1.0x` on this table is a *parity* result, not a defeat: the two
  libraries implement different releases (C++ 2.0.1 here, C# 2.0.0 there), and
  the numerically sensitive inner loops — `CrossProductSign`, `IsCollinear`,
  `Coincident`, `ProductsAreEqual` — are executed in both. The port uses .NET's
  exact 128 bit integer type (`Int128`) for the products, where the older port
  decomposes a 64x64 product by hand into shifts and two halves. The rows where
  SIMD, span writes, pooling or one worker per path apply (rect clipping,
  triangulation, scaling, path ingestion, bounds) are the ones that gain.
* A trap worth repeating: the two libraries number `JoinType` differently, so
  passing the numeric value compares different operations (see
  `docs/porting-notes.md` §2.1). The benchmark passes the *names*.

### The recorded baseline (a known starting point)

`Results/BASELINE.md` and `Results/baseline.json` record the state this README
describes, so a later optimisation round can be measured from it instead of from
memory: the per workload times and allocations against the upstream C# port, the
same rows against native C++, the verification status (tests, bit-exact
fidelity) and the list of places the profiler says the remaining time is. The
benchmark reads and writes it:

```powershell
# compare this build against the recorded point (prints now / baseline / delta / speed)
dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --baseline=Results/baseline.json

# record a new known point
dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --write-baseline=Results/baseline.json
pwsh Results/record-baseline.ps1     # re-pair the native C++ rows, regenerate BASELINE.md
```

Rows are merged by workload, so a run filtered with `--only=` updates only what
it measured, and the sections the benchmark does not own (the native C++ rows,
the verification status, the remaining targets) survive a rewrite. The helper
script behaves the same way: it re-pairs the native rows and rewrites
`BASELINE.md`, but it leaves the `method` / `verification` / `nextTargets`
sections in the JSON alone unless it is called with `-RefreshDefaults`, so
numbers referenced by hand-written text are not silently reset. The evidence
the baseline quotes is in the same folder: `Results/cpp-native.log` (native
numbers), `Results/fidelity-cpp.txt` vs `Results/fidelity-port.txt` (the per case
dumps that prove the bit-exact result) and the managed benchmark logs.

### Why there is no `float` and no `Half` here
`System.Half` (binary16) is the one type this port deliberately does **not** use,
and the same goes for `float` in the geometry. Two measurements explain why:

* **Precision.** Geometry in Clipper2 lives in `long` coordinates; the only
  floating point values are the predicates (`CrossProductSign`, `InCircleTest`,
  `ShortestDistFromSegment`, `GetAngle`) and the arc trigonometry. Evaluating the
  turn test on the repository's own test data gives (against the exact 128 bit
  result, the reference):

  | coordinate range | double | float | Half |
  |---|---|---|---|
  | up to ~5e6 (test data) | 0 flipped signs | 0 | **0.25%** |
  | up to ~5e9 (micrometre CAD) | 0 | 0 | **58%** |
  | up to ~5e11 | 0.10% | 0.19% | **100%** |

  and the in-circle determinant of the triangulator flips **96.8%** of its signs
  with `Half` even at the smallest scale. A flipped sign is not a rounding
  difference: it moves a vertex to the other side of an edge, which changes the
  clipping/offset/triangulation *result*.
* **Speed.** x64 has no binary16 arithmetic — `Half` operations are converted to
  `float` and back. A 20 million iteration multiply-add loop on this machine:
  double 77 ms, float 52 ms, **Half 381 ms (4.9x slower than double)**.

`Half` would also cap coordinates at ±65504 (binary16's maximum) while Clipper2
tests use coordinates in the millions. The port therefore starts from `long` and
*adds* width where exactness needs it (`Int128` for the 64x64 products), which is
the opposite trade to `Half`.


Raw logs: `Results/benchmark-v2-*.log` (the optimised build; `benchmark-final.log`
and `benchmark-run*.log` are the previous round) — the per-case data is embedded
in every report the benchmark writes next to its binary.

## Compared with the C++ implementation

The C++ sources are the thing being ported, so they are the real yardstick. Both
sides were built on this machine and run on the same test data, with the same
workload definitions (`benchmark/Clipper2.CppBenchmark/clipper_bench.cpp` mirrors
`benchmark/Clipper2.Benchmark`):

* C++: MinGW g++ 13.1, `-O3 -DNDEBUG -march=native -static`, Clipper2 2.0.1
  sources (default build, i.e. without `CLIPPER2_HI_PRECISION` — which is also
  what the port implements), allocation counted by replacing global
  `operator new`/`delete`, best of three processes.
* Port: .NET 10 Release, best of two runs after the profiler round
  (`Results/benchmark-v5-*.log`).

**Speed** (ratio above 1.00x means the port is faster than native C++):

| workload | C++ | port | port/C++ |
|---|---:|---:|---:|
| bounds of every path | 0.06 ms | 0.02 ms | **3.00x** |
| area of every path | 0.06 ms | 0.03 ms | **2.00x** |
| scale round trip | 0.13 ms | 0.07 ms | **1.86x** |
| rect clip lines | 0.57 ms | 0.39 ms | **1.46x** |
| path ingestion | 0.18 ms | 0.12 ms | **1.50x** |
| boolean ops (all 195 cases) | 41.2 ms | 31.3 ms | **1.31x** |
| triangulate a 2000 vertex circle | 1.92 ms | 1.56 ms | **1.23x** |
| simplify paths | 0.12 ms | 0.10 ms | **1.20x** |
| rect clip, every path separately | 0.76 ms | 0.63 ms | **1.21x** |
| union of the same paths moved apart (control) | 8.9 ms | 7.6 ms | **1.17x** |
| rect clip (all subjects) | 0.68 ms | 0.64 ms | 1.06x |
| offset, delta = 1 (Round) | 1.93 ms | 1.91 ms | 1.01x |
| offset, every path its own group | 23.0 ms | 23.0 ms | 1.00x |
| point in polygon (200k probes) | 866 ms | 1029 ms | 0.84x |
| union of every subject path | 318 ms | 469 ms | **0.68x** |

The shape of this is easy to read: the port wins wherever a batch can be split
into independent pieces (one worker per path / per group) or a span can be walked
with SIMD, and it wins the allocation-dominated rows because .NET's bump-pointer
allocation and the object pools beat `new`/`delete` per intersected vertex. It
loses where one huge single threaded sweep dominates (the union of all 249 paths,
1.5x slower, and point-in-polygon) — a tighter inner loop is the honest advantage
of the native build there. The two rows that used to look much worse (union 0.59x,
point-in-polygon 0.85x) were the two things the profiler fixed; see below.

**Allocation** (port bytes / C++ bytes, below 1.00 means the port allocates less):

| workload | C++ | port | ratio |
|---|---:|---:|---:|
| area / bounds of every path | 0.14 MB | 0.09 MB | 0.64x |
| simplify paths | 0.18 MB | 0.17 MB | 0.94x |
| rect clip, every path separately | 0.72 MB | 0.69 MB | 0.96x |
| triangulate | 0.84 MB | 1.07 MB | 1.27x |
| union of every subject path | 25.5 MB | 32.8 MB | 1.28x |
| offset, every path its own group | 10.6 MB | 14.2 MB | 1.34x |
| boolean ops (all 195 cases) | 12.9 MB | 17.7 MB | 1.37x |

**Results** — 195 boolean cases plus the 2 offset cases, compared per case
(count + area), port against C++:

| | cases |
|---|---:|
| **identical to the C++ sources** (`-O2`, and `-O3 -ffp-contract=off`) | **197 of 197** |
| identical to the `-O3 -march=native` build | 189 of 197 |
| C++ against itself (`-O2` vs `-march=native`, i.e. FP contraction on) | 8 of 197 differ |
| offsets | **both exact** (`6 / -172785`, `2 / -157766`) |
| simplify, area, bounds, scale round trip, triangulate | **exact** (identical checksums) |
| rect clip, rect clip lines, per path rect clip | identical path counts and areas |
| union of every subject path | identical (`6078` paths, same area) |

The port now reproduces the C++ **bit for bit on every case** of the test corpus.
The 8 cases where the `-march=native` build of the C++ differs from the port are
exactly the 8 cases where that build differs from its own `-O2` sibling: with
`-march=native` GCC is free to contract `a*b - c*d` into an FMA, which rounds
differently and moves an intersection point by one unit. So the port agrees with
the C++ *as written*; the only disagreement left is between C++ build flags.

Two useful confirmations from the same run:

* the offsets match the C++ exactly on both cases, including `offset 1` where the
  older C# 2.0.0 gives a different answer (3 paths / -15080) — the port follows
  the C++ 2.0.1 behaviour, as the porting notes claimed;
* `CrossProductSign` in the C++ uses `__int128` on GCC/Clang, exactly as the port
  uses `Int128`, so the two agree by construction rather than by luck.

The optimisations behind these numbers, all verified by the test suite:

| area | technique |
|---|---|
| engine objects | pooled `Vertex`/`OutPt`/`OutRec` blocks reused across operations, allocated on first use |
| pool growth | the vertex block is sized once per batch (`EnsureCapacity(count + points)`) instead of doubling from 64 |
| path ingestion | per path `Span<Point64>` writes, duplicate skipping and pool hand-out without any intermediate list |
| scanline queue | allocation free `long[]` max-heap instead of `PriorityQueue<long,long>` |
| sorting | `Clipper.Sorts.cs`: one introsort (libstdc++'s `std::sort` algorithm) generic over a struct comparer, used for the intersection list and both triangulator lists — the generic `Sort` overloads dispatch through a delegate or an interface on every comparison |
| point in polygon | the polygon is read through a `Span<Point64>` so the `List<T>` indexer's reload and double bounds check leave the scan loops |
| bounding boxes | `Vector<long>`/`Vector<double>` SIMD min/max over interleaved coordinates, folded in a single pass for a path set |
| path conversion | `CollectionsMarshal` + `Span` writes into pre-sized paths (no `List<T>` growth garbage) |
| simplification | scratch `flags`/`dsq` buffers rented from `ArrayPool` (only the flags need clearing) |
| parallel path sets | `Area`, `GetBounds`, `SimplifyPaths`, `ScalePaths*`, `Paths64/PathsD`, `ReversePaths`, `TranslatePaths`, `StripDuplicates`, `StripNearEqual` |
| parallel clipping | `RectClip64`/`RectClipLines64` batches (one worker per path partition) |
| parallel offsetting | one worker per offset group, merged in group order |
| determinism | every parallel operation writes to stable indices and is reduced in the original order, so results never depend on the thread count |
