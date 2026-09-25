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
* **100 % managed C#**: one assembly (`Clipper2Lib.dll`), **no P/Invoke and no
  native library**, no `unsafe` code (only the portable
  `System.Runtime.CompilerServices.Unsafe` helpers for span access) and **zero
  NuGet dependencies**.
* **Platform independent**: the same assembly runs on Windows, Linux and macOS on
  x64 and arm64, and in the browser as WebAssembly (the live demo is exactly
  that). Nothing in the library is OS or CPU specific — the vectorised paths use
  the portable `Vector<T>`/`Vector128`/`Vector256` APIs instead of hardware
  intrinsics such as `Sse2`/`Avx2`/`AdvSimd`, and the exact 64×64 products use
  `System.Int128`, which every .NET runtime provides. The CI builds, tests and
  packs it on Linux.
* **Live demo: <https://dimq1.github.io/Clipper2Sharp/>** — the port running as
  WebAssembly in the browser: drag the shapes, run every operation, play the
  animations and watch the timings. Same code as the package.

## Layout

```
Clipper2Sharp.slnx
src/
  Clipper2Lib/          the library (core, engine, offset, rect clip, minkowski,
                        triangulation, point locator, parallel booleans,
                        pools, SIMD/parallel primitives)
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
  Clipper2.PortProfile/ port-only workloads (profile these), the golden output
                        check and the C++ fidelity dump
  Clipper2.PortProfileZ/ the golden output check of the Z flavour
  Clipper2.MicroBench/  the micro benchmarks the optimisation decisions rest on
docs/                   porting notes, optimisation plan
```

## Build, test, run

```powershell
dotnet build Clipper2Sharp.slnx
dotnet test  tests/Clipper2.Tests/Clipper2.Tests.csproj
dotnet run   --project examples/ConsoleDemo/ConsoleDemo.csproj -- out.svg
dotnet run -c Release --project benchmark/Clipper2.Benchmark/Clipper2.Benchmark.csproj

# the bit-exactness guards (both must report 0 differences)
dotnet run -c Release --project benchmark/Clipper2.PortProfile -- golden verify
dotnet run -c Release --project benchmark/Clipper2.PortProfile -- fidelity
dotnet run -c Release --project benchmark/Clipper2.PortProfileZ
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
Minkowski, sorter, multi-threaded-path, point locator, parallel boolean and
reusable-data tests: **35 tests, all green**.

## Packaging and CI

The library ships as the **`Clipper2Sharp`** NuGet package (assembly and namespace
stay `Clipper2Lib`, so it is a drop-in for the upstream C# port). Two GitHub Actions
workflows cover it:

* `ci.yml` — on every push and pull request: build, the 35 tests, the
  **bit-exactness gates** (`golden verify` on the whole corpus, `fidelity` against
  the C++ dump, the Z flavour's golden), `dotnet pack`, an inspection of the
  `.nupkg`, and a scratch project that consumes the package and calls it;
* `publish.yml` — on a `v*` tag: the same gates, then push to nuget.org, using
  trusted publishing (OIDC, keyless) or a `NUGET_API_KEY` secret.
* `pages.yml` — on a change to the demo or the library: build
  `examples/Clipper2.WebDemo` (Blazor WebAssembly, AOT when the `wasm-tools`
  workload is available, interpreted otherwise) and deploy it to GitHub Pages,
  which is what the link on the package page points at.

Both build `Clipper2Sharp.Ci.slnx`, the full solution minus the A/B benchmark, which
needs the upstream C# checkout. The one-time nuget.org setup, the release steps and
a troubleshooting table are in `docs/nuget-release.md`; to build a package locally:

```powershell
dotnet pack src/Clipper2Lib/Clipper2Lib.csproj -c Release -o artifacts
```

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
* Every optimisation is checked against **golden output hashes**
  (`Results/golden-hashes.txt`): 6409 hashes of the *complete* output — every
  coordinate, the path order, the polytree nesting — of booleans, polytrees,
  `ClipperD`, offsets with every join/end type, rect clipping, simplification,
  triangulation, point-in-polygon and Minkowski over the test corpus, plus a
  deterministic 4000 case fuzz corpus (tiny grids full of coincident and
  horizontal edges, coordinates beyond 2^31, open paths). The current build
  reproduces all 6409, and the Z flavour has its own hash
  (`benchmark/Clipper2.PortProfileZ`).
* Two opt-in additions are *not* bit-identical by design and say so:
  `Clipper.BooleanOpParallel` (independent clusters clipped in parallel — same
  regions up to integer rounding, see `src/Clipper2Lib/Clipper.Parallel.cs`) is
  a separate entry point; `PointInPolygonLocator` and the batch
  `Clipper.PointInPolygon(polygon, points, results)` *are* identical to the
  single call.
* The enum member order of `JoinType` is the one thing that is *not* numerically
  compatible with the older C# surface; see `docs/porting-notes.md` §2.1.
* Details: `docs/porting-notes.md`.

## Performance

Measured with `benchmark/Clipper2.Benchmark`, which compiles the upstream C#
sources (`E:\Learning\AI\Clipper2\CSharp\Clipper2Lib`) with the same toolchain
and target framework and runs both libraries over identical inputs. These are
the numbers recorded in `Results/BASELINE.md` (round 3 of the optimisation work,
see `docs/optimization-plan.md`):

| workload | speed-up | allocation | results |
|---|---:|---:|---|
| boolean ops (all 195 polygon cases) | **1.36x** | **0.48x** | 2.0.0 vs 2.0.1 differences |
| union of every subject path | **2.15x** | **0.38x** | 2.0.0 vs 2.0.1 differences |
| union: path ingestion only | 1.11x | **0.66x** | identical |
| union of the same paths moved apart (control, no intersections) | **1.26x** | **0.43x** | 2.0.0 vs 2.0.1 differences |
| offset, delta = 1 (Round/Polygon) | 1.12x | **0.19x** | version difference |
| offset, delta = 27 (Miter/Polygon) | 0.94x | **0.43x** | identical |
| offset, every subject path as its own group | **1.18x** | **0.28x** | version difference |
| rect clip (all subjects) | **1.32x** | **0.61x** | identical |
| rect clip lines (all subjects) | **1.27x** | **0.67x** | identical |
| rect clip, every path separately | **1.33x** | **0.64x** | identical |
| simplify paths | 0.94x | **0.78x** | identical |
| triangulate a 2000 vertex polygon | **1.38x** | **0.89x** | identical |
| area of every path | 0.99x | 1.00x | identical |
| bounds of every path | 1.03x | 1.00x | identical |
| point in polygon (200k probes, one call each) | **1.44x** | 1.00x | identical |
| scale round trip (x1000) | **1.22x** | 1.00x | identical |

*Speed-up* is the reference time divided by the port's time (higher is better),
*allocation* is the port's bytes divided by the reference's (lower is better).
"Differences" are the cases where the C++ 2.0.1 algorithm (which the port follows
bit for bit) legitimately differs from the C# 2.0.0 port.

What moved these rows in round 3, in order of effect (details in
`docs/porting-notes.md` §4.6):

* **the sweep runs on arenas instead of objects**: active edges, out-points and
  input vertices are structs in arrays, linked by int handles. Relinking the
  AEL/SEL or an output ring then writes plain ints (no GC write barrier: 3.4x
  cheaper per swap, measured), the intersection nodes become plain data (their
  sort is 2x faster), and the out-point and vertex arenas hold no references at
  all, so the GC never scans them and they are rented from the array pool. The
  union of all 249 paths runs 1.84x faster than the round 2 build (alternating
  processes on the same machine: 287 ms -> 156 ms) and allocates 1.9 MB per
  operation instead of 32.7 MB;
* **no merge passes over an ordered scanbeam** (if no edge overtook its
  neighbour, the intersection search could not find anything);
* **the library's own one-shot engines are reused**: `ClipperOffset`'s final
  union and `Clipper.BooleanOp` take a per-thread `Clipper64`, and offset paths go
  straight into it (offset allocation dropped to a quarter);
* **exact incremental area** in the self-intersection clean-up (Int128 shoelace
  sum kept in step with every split, with a certified error bound that falls
  back to the C++'s double computation whenever the double result could decide
  differently);
* **exact two-tier predicates** (plain 64 bit products when every factor fits in
  31 bits, `Math.BigMul` otherwise), pooled rect-clip out-points and
  triangulation without per-call arrays.

For many point-in-polygon queries against one polygon, `PointInPolygonLocator`
(or `Clipper.PointInPolygon(polygon, points, results)`) answers exactly like the
single call after a SIMD bounding box test and a Y-bucketed edge table: the
benchmark's 200k x 249 probes take **82 ms instead of 711 ms**. For inputs made of
independent clusters, `Clipper.BooleanOpParallel` clips the clusters separately
(400 clusters of 8 ellipses: **33 ms instead of 1775 ms**).

The additional entry points:

| API | result |
|---|---|
| `PointInPolygonLocator(Path64)`: `Locate`, `Locate(span, span)`, `Contains`, `Bounds`, `Count` | identical to `Clipper.PointInPolygon(Point64, Path64)` |
| `PointInPolygonLocatorD(PathD, precision)`: `Locate`, `Locate(span, span)`, `Contains`, `Bounds` | identical to `Clipper.PointInPolygon(PointD, PathD, precision)` |
| `Clipper.PointInPolygon(Path64 / PathD polygon, points, results[, precision])` | identical to the single calls |
| `Clipper.BooleanOpParallel(ct, fr, Paths64 subject, clip)` / `UnionParallel(Paths64, fr)` | same regions up to integer rounding |
| `Clipper.BooleanOpParallel(ct, fr, subject, clip, PolyTree64)` | clusters' trees merged under one root |
| `Clipper.BooleanOpParallel(ct, fr, PathsD subject, clip[, PolyTreeD], precision)` / `UnionParallel(PathsD, fr, precision)` | scaled exactly like `ClipperD` |

### How these numbers were taken (and how they can mislead)

* Both libraries are loaded into one process (extern aliases) and get byte
  identical input, so no serialisation or JIT difference can bias a row.
* Each row is the best of 7 alternating rounds: within a round the two libraries
  are measured back to back with the order swapped every round, and inside a
  measurement the fastest uninterrupted repetition wins (the first repetition is
  dropped as cache-cold, and an allocation figure is only ever reported together
  with the round it came from). The process runs at high priority
  (`--no-priority` to disable).
* Changes to the port itself are measured with `benchmark/Clipper2.PortProfile`
  against a build of the previous commit, alternating the two processes; that
  is the instrument behind the per change numbers in the porting notes.
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


Raw logs: `Results/benchmark-v7-round3.log` (this round), `Results/benchmark-v6-baseline.log`
and `benchmark-v5-*.log` (round 2), `benchmark-final.log` / `benchmark-run*.log`
(round 1) — the per-case data is embedded in every report the benchmark writes
next to its binary.

## Compared with the C++ implementation

The C++ sources are the thing being ported, so they are the real yardstick. Both
sides were built on this machine and run on the same test data, with the same
workload definitions (`benchmark/Clipper2.CppBenchmark/clipper_bench.cpp` mirrors
`benchmark/Clipper2.Benchmark`):

* C++: MinGW g++ 13.1, `-O3 -DNDEBUG -march=native -static`, Clipper2 2.0.1
  sources (default build, i.e. without `CLIPPER2_HI_PRECISION` — which is also
  what the port implements), allocation counted by replacing global
  `operator new`/`delete`, best of three processes.
* Port: .NET 10 Release, the recorded baseline run; the three C++ processes ran
  immediately before and after it, so the pairs share one time window.

**Speed** (ratio above 1.00x means the port is faster than native C++):

| workload | C++ | port | port/C++ | round 2 |
|---|---:|---:|---:|---:|
| bounds of every path | 0.04 ms | 0.016 ms | **2.56x** | 2.83x |
| area of every path | 0.04 ms | 0.017 ms | **2.35x** | 2.46x |
| scale round trip | 0.09 ms | 0.053 ms | **1.70x** | 2.01x |
| path ingestion | 0.12 ms | 0.073 ms | **1.64x** | 1.52x |
| rect clip lines | 0.39 ms | 0.27 ms | **1.46x** | 1.53x |
| simplify paths | 0.08 ms | 0.06 ms | **1.36x** | 1.48x |
| triangulate a 2000 vertex circle | 1.26 ms | 0.95 ms | **1.33x** | 1.27x |
| boolean ops (all 195 cases) | 22.0 ms | 17.4 ms | **1.26x** | 1.48x |
| union of every subject path | 187.6 ms | 165.2 ms | **1.14x** | 0.73x |
| union of the same paths moved apart (control) | 4.87 ms | 4.46 ms | **1.09x** | 1.16x |
| offset, delta = 1 (Round) | 1.25 ms | 1.19 ms | 1.05x | 0.99x |
| rect clip (all subjects) | 0.45 ms | 0.43 ms | 1.04x | 1.12x |
| rect clip, every path separately | 0.50 ms | 0.48 ms | 1.04x | 1.22x |
| offset, every path its own group | 14.9 ms | 15.5 ms | 0.96x | 1.07x |
| offset, delta = 27 (Miter) | 2.46 ms | 3.08 ms | 0.80x | 0.89x |
| point in polygon (200k probes, one call each) | 441 ms | 711 ms | 0.62x | 0.72x |

The *round 2* column is the previous recorded pair. Read it with care: that
session ran under heavy background load (the C++ union took 318 ms there, 188 ms
here), so the native side was slowed far more than the managed side on several
short rows. What the two columns do show reliably is the union: the one row
that was clearly behind the native build (0.73x) is now ahead of it, on the
same kind of measurement. The rows still below 1.00x — the Miter offset (long
AEL walks), the single point-in-polygon call (call overhead on 18-vertex paths;
the batch locator above is the answer there) — are profiled in
`Results/BASELINE.md`.

**Allocation** (port bytes / C++ bytes, below 1.00 means the port allocates less):

| workload | C++ | port | ratio |
|---|---:|---:|---:|
| offset, delta = 1 | 1.53 MB | 0.48 MB | **0.32x** |
| offset, every path its own group | 10.6 MB | 3.57 MB | **0.34x** |
| union of every subject path | 25.5 MB | 12.5 MB | **0.49x** |
| union of the same paths moved apart | 3.74 MB | 1.77 MB | **0.47x** |
| rect clip, every path separately | 0.72 MB | 0.43 MB | **0.59x** |
| area / bounds of every path | 0.14 MB | 0.09 MB | **0.61x** |
| boolean ops (all 195 cases) | 12.9 MB | 7.82 MB | **0.61x** |
| simplify paths | 0.18 MB | 0.17 MB | 0.94x |
| triangulate | 0.84 MB | 0.97 MB | 1.15x |

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

The port reproduces the C++ **bit for bit on every case** of the test corpus,
and round 3 changed none of it: the golden hashes of the complete output are
identical before and after. The 8 cases where the `-march=native` build of the
C++ differs from the port are exactly the 8 cases where that build differs from
its own `-O2` sibling: with `-march=native` GCC is free to contract `a*b - c*d`
into an FMA, which rounds differently and moves an intersection point by one
unit. So the port agrees with the C++ *as written*; the only disagreement left is
between C++ build flags.

The optimisations behind these numbers, all verified by the test suite and the
golden hashes:

| area | technique |
|---|---|
| active edges | `Active` is a struct in an arena rented from the array pool, addressed by byte-offset handles (`load + add` per link, no multiply), explicit layout with the AEL walk fields in the first 64 bytes |
| out-points | `OutPt` is a 32 byte struct without references (its out-rec is an index): the arena is never scanned by the GC and ring relinking has no write barrier |
| vertices | `Vertex` is a struct in a `VertexStore` array; `ReuseableDataContainer64` hands the engine a copy of its vertices with shifted links |
| intersection list | plain data nodes (point + two handles) sorted by the libstdc++ introsort (same tie order as the C++) |
| scanbeams | no merge passes when the edges are still in X order at the top of the beam |
| AEL insertion | the X comparison of `IsValidAelOrder` inlined into the walks |
| self-intersection clean-up | exact incremental Int128 ring area with a certified fallback to the C++'s double area |
| predicates | exact 64 bit tier / `Math.BigMul` for `CrossProductSign` and `ProductsAreEqual` |
| one-shot engines | per-thread `Clipper64` for `ClipperOffset` and `Clipper.BooleanOp`; offset paths fed straight into it |
| output paths | built in a scratch path and copied at their exact size; out-rec paths created only for polytrees |
| sorting | `Clipper.Sorts.cs` introsort and a struct-comparer stable merge sort with a reused buffer (local minima, horizontal segments) |
| rect clipping | out-points and scratch paths recycled across a batch (per-thread working state) |
| triangulation | no per-call arrays in `ForceLegal`, inline triangle edges, presized working lists |
| point in polygon | `PointInPolygonLocator`: SIMD bounding box test + Y-bucketed edges, answers identical to the scan |
| clusters | `Clipper.BooleanOpParallel`: union-find over bounding box overlap, clusters clipped in parallel (opt-in) |
| bounding boxes | `Vector<long>`/`Vector<double>` SIMD min/max over interleaved coordinates |
| path conversion | `CollectionsMarshal` + `Span` writes into pre-sized paths |
| parallel path sets | `Area`, `GetBounds`, `SimplifyPaths`, `ScalePaths*`, `Paths64/PathsD`, `ReversePaths`, `TranslatePaths`, `StripDuplicates`, `StripNearEqual`, `RectClip64` batches, offset groups |
| determinism | every parallel operation writes to stable indices and is reduced in the original order, so results never depend on the thread count |
