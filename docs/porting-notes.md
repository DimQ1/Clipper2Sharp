# Porting notes — C++ Clipper2 → C#

This document records how the C++ sources map onto `src/Clipper2Lib` and which
decisions were taken where C# and C++ differ.

## 1. Source mapping

| C++ file (Clipper2 2.0.1) | C# file | Notes |
|---|---|---|
| `include/clipper2/clipper.core.h` | `Clipper.Core.cs` | points, rects, paths, geometry helpers, `PointInPolygon` |
| `include/clipper2/clipper.engine.h` + `src/clipper.engine.cpp` | `Clipper.Engine.cs` | scanline clipper, `ClipperBase`, `Clipper64/ClipperD`, `PolyPath*`, `PolyTree*` |
| `include/clipper2/clipper.offset.h` + `src/clipper.offset.cpp` | `Clipper.Offset.cs` | `ClipperOffset`, `JoinType`, `EndType` |
| `include/clipper2/clipper.rectclip.h` + `src/clipper.rectclip.cpp` | `Clipper.RectClip.cs` | `RectClip64`, `RectClipLines64` |
| `include/clipper2/clipper.minkowski.h` | `Clipper.Minkowski.cs` | Minkowski sum/difference |
| `include/clipper2/clipper.triangulation.h` + `src/clipper.triangulation.cpp` | `Clipper.Triangulation.cs` | constrained Delaunay triangulation |
| `include/clipper2/clipper.h` | `Clipper.cs` | simple (static) API + `Utils`-level helpers |
| `CPP/Utils/clipper.svg.*`, `clipfileload/save.*` | `utils/Clipper2.SVG`, `utils/Clipper.FileIO` | SVG reader/writer, test file IO |

Pointer based C++ structures map onto C# classes (`Vertex`, `OutPt`, `OutRec`,
`Active`, `HorzSegment`), so aliasing behaves exactly like the C++ code.
`IntersectNode` is a struct (as in the C++ `std::vector<IntersectNode>`), and the
scanline priority queue is a hand written max-heap over `long[]`
(`ScanlineHeap`) mirroring `std::priority_queue<int64_t>`.

## 2. Public API

The public surface is a drop-in match for the upstream C# port (`Clipper2Lib`
namespace, `Point64`/`PointD`, `Path64/Paths64/PathD/PathsD`, `Clipper64`,
`ClipperD`, `ClipperOffset`, `PolyPath64/D`, `PolyTree64/D`, `ClipperBase`,
`ReuseableDataContainer64`, `RectClip64`, `RectClipLines64`, `Delaunay`,
`TriangulateResult`, `JoinType`, `EndType`, `ClipType`, `FillType`…), so existing
C# code that targets Clipper2 keeps compiling. `USINGZ` builds the same sources
into a second assembly (`Clipper2ZLib`) with Z coordinates and the Z callbacks,
exactly like the C++ `USINGZ` build.

Two deliberate additions relative to the upstream C# port (both taken from the
C++ headers): `Clipper.Length`, `Clipper.NearCollinear`,
`Clipper.Path2ContainsPath1`, `Clipper.CheckPolytreeFullyContainsChildren`,
`Clipper.Ellipse(Rect64/RectD, steps)` and the `C++`-style
`BooleanOp(clipType, fillRule, subject, clip)` overloads.

### 2.1 Enum member order (`JoinType`)

Every enum kept its C++ 2.0.1 member order. `ClipType`, `FillRule`, `EndType` and
`PointInPolygonResult` are numbered exactly like the upstream C# ones, but
`JoinType` is **not**:

| member | C++ 2.0.1 / this port | upstream C# (2.0.0) |
|---|---:|---:|
| `Square` | 0 | 1 |
| `Bevel` | 1 | 2 |
| `Round` | 2 | 3 |
| `Miter` | 3 | 0 |

Named uses (`JoinType.Round`) behave identically in both libraries, so ordinary
source compatibility is unaffected, but code that casts a number to `JoinType`
(or compares the numeric value) must be reviewed when moving between the two.
This is why the benchmark passes the join and end types **by name**: an earlier
revision passed the numeric value and silently compared our `Round` against the
reference's `Bevel` — the size of that mistake (2.8x the allocation, half the
speed, different results) is a good illustration of the trap.

## 3. Behavioural differences against the upstream C# port

The C# port in `E:\Learning\AI\Clipper2\CSharp` implements Clipper2 **2.0.0**,
while the C++ sources ported here are **2.0.1**. Running the same 195 polygon
cases through both shows:

* **193 of 195 cases are identical** (same path count and same area).
* 2 union cases differ slightly (case 173: 130 paths vs 131; case 174: 122 vs
  122 with a 3 unit area difference). Both results stay inside the tolerances
  recorded in the `Tests/Polygons.txt` data set (counts within 6 for cases
  ≥ 120), which is what the C++ test suite itself allows.
* The 2 offset cases differ by 1–2 area units (sub-rounding differences).

Everything else — including the 700 KB polytree fixture, rect clipping,
triangulation, simplification and the file driven offset/orientation tests —
matches, and the ported test suite (`tests/Clipper2.Tests`, 28 tests) passes.

## 4. Memory and performance work

The C++ engine owns its vertices in one contiguous `Vertex[]` block. C#
reference types cannot do that, so equivalence is reached by pooling:

* `Clipper.Pools.cs` — growable pools of `Vertex`, `OutPt` and `OutRec` objects
  that are reused by every successive operation of the same `ClipperBase`
  (mirroring the upstream C# `PooledList`/`VertexPoolList` design). A reused
  `OutRec` always gets a fresh `Path64` because out-rec paths are handed back to
  the caller.
* `Clipper.BulkOps.cs` — SIMD, span and threading primitives:
  * `ScanlineHeap` — allocation free max-heap (replaces `PriorityQueue<long,long>`).
  * `MinMaxInterleaved` — `Vector<long>`/`Vector<double>` bounding box reduction
    used by `GetBounds` for single paths; multi-path bounds and areas are
    computed on several threads and reduced in the original order, so they stay
    bit-identical to the single-threaded results.
  * `GrowUninitialized` — writes result paths straight into pre-sized list
    storage via `CollectionsMarshal`, removing the repeated growth of `List<T>`
    in every scaling/conversion helper.
  * `Scratch<T>` — pooled scratch buffers for `SimplifyPath`.
  * `ParallelThreshold`/`For`/`ForRanges` — work threshold and range splitting
    used by the path-set helpers, `RectClip64.Execute`, `ClipperOffset` groups
    and `MinkowskiInternal`.
* Parallel work is always written to stable indices and the final reduction is
  serial, so results do not depend on the number of threads.

`benchmark/Clipper2.Benchmark` measures the port against the upstream C# sources
compiled by the same toolchain and target framework; see `Results/` for the
generated reports.

### 4.1 Measuring against the upstream port

The benchmark loads both libraries into one process through extern aliases
(`newcli`/`refcli`), feeds them byte identical input and compares a checksum
(path count + area) with the timing. Three details turned out to matter:

* **Interleave the two libraries.** Measuring the port to completion and then
  the reference reports nonsense on a loaded machine (the dev box runs at
  75–85% background CPU): identical code came out 0.3x and 1.5x in consecutive
  runs. Each row is therefore measured in alternating rounds — port first in one
  round, reference first in the next — and keeps the best of 7 rounds.
* **Keep the fastest repetition, not the average.** Inside a round each library
  runs the workload repeatedly for ≥400 ms; the first repetition is dropped
  (cache cold, the other library was just measured) and the fastest remaining
  one is reported, because it is the one least disturbed by background load.
* **Pass enum values by name.** `JoinType` is numbered differently in the two
  libraries (see §2.1). An early revision passed `2`, which is `Round` here and
  `Bevel` in the reference — the offset rows then showed "2.8x the allocation,
  half the speed, different results" and it was the benchmark, not the port.
  After the fix the same workload reads 1.14x allocation and identical results.

Allocation is measured with `GC.GetTotalAllocatedBytes(true)` and is
deterministic: it is the reliable half of the comparison. Time ratios of the
short rows move by up to ±15% between sessions even with the interleaved
harness, so anything inside ±10% of 1.00x is parity.

### 4.2 Optimisation results (best of the last two runs)

| workload | speed-up | allocation |
|---|---:|---:|
| boolean ops (195 cases) | 1.04x | 1.06x |
| union of every subject path | 1.18x | 1.00x |
| union: path ingestion only | 1.00x | 0.96x |
| union of the same paths moved apart (control) | 1.02x | 1.18x |
| rect clip / rect clip lines / per path | 1.28x / 1.23x / 1.27x | 1.02x / 1.03x / 1.03x |
| scale round trip | 1.18x | 1.00x |
| triangulate | 1.18x | 0.98x |
| point in polygon (200k probes) | 1.19x | 1.00x |
| bounds of every path | 1.03x | 1.00x |
| area of every path | 1.01x | 1.00x |
| simplify paths | 0.95x | 0.78x |
| offset (Round, delta = 1) | 0.99x | 0.74x |
| offset (Miter, delta = 27) | 0.97x | 1.41x |
| offset, every path its own group | 1.01x | 1.11x |

The second optimisation round (after the pooling and parallelism work) was about
per instance and per batch cost rather than per vertex cost:

* **Lazy pool blocks.** `PooledList<T>` no longer allocates its block in the
  constructor; a `ClipperBase` that only receives paths never allocates the
  out-point or out-rec block at all, and `ScanlineHeap` allocates its `long[]`
  on the first push. A bare `Clipper64` now costs 408 bytes — exactly what the
  reference's bare instance costs (it was ~2 KB before).
* **One-shot vertex pool growth.** `AddPaths_` knows the batch size, so it calls
  `EnsureCapacity(count + totalPoints)` once (mirroring the reference) instead of
  letting the block double from 64; every intermediate block used to be garbage.
  This is what fixed the "ingestion only" row (1.17x → 0.96x allocation).
* **Single pass bounds.** `GetBounds(Paths64/PathsD)` used to build a `Rect64[]`
  of per path results and reduce it; the sequential case now folds the per path
  SIMD min/max into the running rectangle in one pass (0.92x → 1.03x, 1.09x →
  1.00x allocation). Empty paths contribute nothing either way.
* **Devirtualised sort.** The intersection list is sorted through a concrete
  `IComparer<IntersectNode>` struct instead of a `Comparison<T>` delegate, so
  every comparison is inlined into the introsort.
* **No redundant clears.** `SimplifyPath` rents its distance buffer without
  clearing it (every element is written before it is read); only the `flags`
  buffer still has to be zeroed.
* **Rejected: pooling `HorzSegment`.** It costs a block per instance and only
  pays off when the same engine runs many operations; the boolean row went from
  1.064x to 1.087x allocation, so it was reverted.

Trying to win the point-in-polygon row by hand was instructive: a 64 bit fast
path in `CrossProductSign` (skipping `Int128` when all four factors are below
2^31) looked obvious and was wrong — the guard tested the OR of the four values,
and an OR bit can come from any single operand, so coordinates beyond 2^31
slipped through and produced wrong answers. `TestPointInPolygonWideCoordinates`
now pins that behaviour (9e9 sized coordinates, the same shape next to the
origin), which is a useful test even though the honest measurement showed the
port was never slower than the reference there to begin with.

### 4.3 Numeric types: `long`, `Int128`, `double` — and why not `float`/`Half`

The port keeps the C++ numeric model: integer coordinates (`long`), `double`
for the predicates and arc trigonometry, and — where the C++ uses `__int128` —
`System.Int128` (a single widening multiply, cheaper than the hand rolled 64x64
decomposition of the older C# port). Narrowing any of that to `float`, or to
`System.Half` (binary16, 10 bit mantissa), is not an option, and this was measured
rather than assumed (`Results/` scratch probe, 400k random sign tests per row,
compared against the exact 128 bit result):

| coordinate range | turn test: double | float | Half | in-circle: float | Half |
|---|---:|---:|---:|---:|---:|
| up to ~5e6 (the test data) | 0 | 0 | 0.25% | 0.03% | 96.8% |
| up to ~5e9 (micrometre CAD) | 0 | 0 | 58.4% | 0.03% | 100% |
| up to ~5e11 | 0.10% | 0.19% | 100% | 0.19% | 100% |

A flipped sign is not noise: it moves a vertex across an edge, which changes the
clip/offset/triangulation result. On top of that, `Half` has no binary16
arithmetic on x64 (every operation converts to `float` and back): a 20 million
iteration multiply-add loop took 77 ms in `double`, 52 ms in `float` and
**381 ms in `Half`** — 4.9x slower than the widening type it was meant to
replace. `Half` would also cap coordinates at ±65504 while the test data already
reaches ~5e6. The port therefore widens where exactness demands it and keeps
doubles everywhere else.

### 4.4 Against the C++ implementation itself

`benchmark/Clipper2.CppBenchmark/clipper_bench.cpp` mirrors the managed benchmark
workload for workload, so the port can be measured against the sources it was
ported from (MinGW g++ 13.1, `-O3 -DNDEBUG -march=native -static`, default build,
allocation counted with a counting `operator new`).

Speed, port / C++ (above 1.00x = the port is faster):

| workload | ratio | | workload | ratio |
|---|---:|---|---|---:|
| area, bounds (all paths) | 3.00x | | triangulate 2000 points | 1.14x |
| scale round trip | 2.00x | | rect clip (all) | 1.03x |
| rect clip lines | 1.49x | | offset (Round, delta 1) | 0.98x |
| path ingestion | 1.42x | | offset, every path a group | 0.96x |
| simplify paths | 1.38x | | point in polygon | 0.85x |
| boolean ops (195 cases) | 1.18x | | union of every subject path | 0.59x |
| rect clip, per path | 1.16x | | | |

The split is systematic: the port wins the rows that can be split into independent
batches (a worker per path or per offset group), the span/SIMD rows and the
allocation-dominated rows (pools plus a bump-allocating GC beat `new`/`delete` per
vertex), and it loses where one big single-threaded sweep dominates — the union of
all 249 paths (1.7x slower) and point-in-polygon.

Results, per case (count and area), over the 195 boolean cases and the 2 offset
cases — **197 of 197 identical to the C++ sources** once the port had its own
sorter (§4.5): the same count *and* the same area for every case, offsets
included (`6 / -172785`, `2 / -157766`), and the same checksums for simplify,
area, bounds, scale round trip, triangulation, rect clipping and the union of all
249 paths.

Before that sorter change the picture was 159 identical, 26 differing in the area
only (≤ 138 ppm) and 10 by 1–3 sliver paths — all of it tie-breaking inside the
intersection sort, which is also why `offset 1` now matches the C++ exactly
(4 paths / -15019) where the C# 2.0.0 port answers 3 paths / -15080.

The only remaining disagreements are between *C++ builds*: rebuilt `-O2` vs
`-O3 -march=native`, the C++ differs from itself on 8 of the 197 cases, because
`-march=native` lets GCC contract `a*b - c*d` into an FMA and that rounds
differently. Against those builds the port agrees with the ones that do not
contract (`-O2`, `-O3 -ffp-contract=off`) on all 197 and differs only on that same
set of 8. The port therefore reproduces the C++ **as written**.

Note on flavours: `CLIPPER2_HI_PRECISION` only replaces `GetLineIntersectPt` with
an origin-shifted variant (and `CrossProductSign` already uses `__int128` on
GCC/Clang), so the default C++ build is the one to compare against — and the port
implements exactly that variant of both functions.

### 4.5 Profiler round (dotnet-trace on every heavy workload)

With `dotnet-trace collect --profile dotnet-sampled-thread-time` plus
`dotnet-trace report <trace> topN` (installed with a nuget.org-only config, because
the machine's NuGet sources include a corporate feed that fails the install) the
six heaviest workloads were profiled. What it found, in order of cost:

| finding | measured share | fixed by |
|---|---:|---|
| the intersection list was sorted with `MemoryExtensions.Sort(span, struct IComparer)`, which **boxes the comparer** and calls `Compare` through the interface | 34% of a union (22.5% inside the sort + 11.6% inside one `Compare` frame) | `Clipper.Sorts.cs`: one introsort over a struct comparer type parameter (direct, inlinable comparison), the libstdc++ algorithm |
| the triangulator sorted its edge and vertex lists with `List<T>.Sort(delegate)` (delegate call per comparison, shared generic code) | 14% of a triangulation | same sorter, `EdgeLess`/`Vertex2Less` |
| `PointInPolygon` read the polygon through the `List<Point64>` indexer, which reloads the backing array and its size on every access inside the Y-scan loops | the scans are ~84% of the row | read the points through `CollectionsMarshal.AsSpan` |
| small/fast operations are allocation-throughput bound: the GC poll worker plus `Buffer.Memmove` is 50% of `boolean ops`, 48% of `offsets`, 85% of `rect clip` | — | not addressed; they are the rows where the port already beats the C++ (see §4.4) and the fixed rows above gave the bigger wins |

A useful detail for reading such a report: the reference library and the port both
print as `Clipper2Lib.<Type>` (same namespace, different assembly), so a hot frame
like `IntroSort(Span<IntersectNode>, IComparer<IntersectNode>)` was the *reference's*
sort (`struct IntersectListSort : IComparer<IntersectNode>` + `_intersectList.Sort`)
and not the port's once the port had its own sorter — the port's own sort is
recognisable by its method names.

Measured effect of the round (same-window pairs against native C++):

| workload | before | after |
|---|---:|---:|
| union of every subject path | 0.59x | **0.68x** |
| point in polygon | 0.85x | **0.84x** but 1.19x of the *C# 2.0.0 port* (was 0.95–1.01x) |
| triangulate | 1.14x | **1.23x** |
| boolean ops | 1.18x | **1.31x** |
| rect clip lines / rect clip per path | 1.49x / 1.16x | 1.46x / 1.21x |

What is left, with the share the profiler attributes to it:

* **the union's sweep itself** (`BuildIntersectList` 12%, `IntersectEdges` 7%,
  `BuildPaths` 5%, `DoIntersections` 4%): this is the engine doing its work, and the
  remaining gap to C++ (~1.5x) is plain managed-code overhead in the tight AEL/
  scanline loops (field access through object references, no cross-method inlining).
  Closing it would mean restructuring the sweep around index-based structs, which
  would put the bit-exact result of §4.4 at risk for a row that is already the
  worst case.
* **allocation on the small rows**: `booleans` 1.06x, `offset groups` 1.08x,
  `union` 1.00x, `triangulate` 0.98x of the reference bytes — the pools already
  cover the engine, and what is left is `BuildPaths`' per outrec `Path64` and the
  temporary paths in `ClipperOffset`/`RectClip64`.
* **`Delaunay.ForceLegal`** (32% of a triangulation, two frames): the Delaunay
  legalisation loop is the triangulator's hot spot; the port already beats the C++
  there (1.23x).


