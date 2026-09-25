# Benchmark baseline (known starting point)

Recorded 09/25/2026 10:52:28 on this machine (12 logical processors,
.NET 10.0.12, Microsoft Windows 10.0.26200).

Everything below is also in `Results/baseline.json` (machine readable, and the file the
benchmark reads with `--baseline=`), together with `Results/cpp-native.log` (the native
side) and the two fidelity dumps.

## Reproduce / compare

```powershell
# compare the current build against the recorded point (prints now / baseline / delta / speed)
dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --baseline=Results/baseline.json

# record a new known point (keeps the native/verification/targets sections)
dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --write-baseline=Results/baseline.json

pwsh Results/record-baseline.ps1   # re-pair the native rows and regenerate this file
```

Method: interleaved A/B in one process (order swapped every round), fastest uninterrupted repetition per round (first repetition dropped), best of 7 rounds; native side: same workload definitions in benchmark/Clipper2.CppBenchmark, best of 3 processes, allocation counted with a replacing operator new.
Caveat: this machine runs at 75-85% background CPU, so time ratios of the short rows move by +-10-15% between sessions; allocation numbers are deterministic

## Port against the upstream C# 2.0.0 port (same process, interleaved rounds)

| workload | port ms | ref ms | speed-up | port MB | ref MB | allocation | vs C# 2.0.0 |
|---|---:|---:|---:|---:|---:|---:|---|
| boolean ops (Polygons.txt, all cases) | 17.4419 | 23.6351 | 1.3551x | 7.8233 | 16.3343 | 0.479x | differs |
| union: add subject paths only (ingestion) | 0.0731 | 0.081 | 1.1081x | 0.2973 | 0.4496 | 0.6612x | identical |
| union of every subject path (Paths64) | 165.2154 | 356.0123 | 2.1548x | 12.5377 | 32.6792 | 0.3837x | differs |
| union of the same paths moved apart (no intersections) | 4.4585 | 5.6145 | 1.2593x | 1.7716 | 4.1502 | 0.4269x | differs |
| offsets (Offsets.txt, delta = 1, Round/Polygon) | 1.1936 | 1.3377 | 1.1207x | 0.4849 | 2.596 | 0.1868x | differs |
| offsets (Offsets.txt, delta = 27, Miter/Square) | 3.0846 | 2.9038 | 0.9414x | 0.4625 | 1.0716 | 0.4316x | identical |
| rect clip (off all subjects into 100,100,700,500) | 0.4347 | 0.5731 | 1.3184x | 0.3799 | 0.6225 | 0.6103x | identical |
| rect clip lines (off all subjects into 100,100,700,500) | 0.2668 | 0.3399 | 1.274x | 0.4 | 0.5975 | 0.6695x | identical |
| simplify paths (all subjects, eps = 0.5) | 0.0587 | 0.0553 | 0.9421x | 0.1651 | 0.2127 | 0.7763x | identical |
| triangulate 2000 vertex polygon | 0.9472 | 1.3041 | 1.3768x | 0.9698 | 1.0925 | 0.8877x | identical |
| area (Paths64 of every subject+clip) | 0.017 | 0.0169 | 0.9941x | 0.0854 | 0.0854 | 1.0004x | identical |
| get bounds (Paths64 of every subject+clip) | 0.0156 | 0.016 | 1.0256x | 0.0854 | 0.0854 | 1.0004x | identical |
| point in polygon (200k probes) | 710.7713 | 1026.1654 | 1.4437x | 0.0854 | 0.0854 | 1x | identical |
| scale round trip (Paths64 -> PathsD -> Paths64, x1000) | 0.053 | 0.0644 | 1.2151x | 0.2526 | 0.2523 | 1.0011x | identical |
| offset: every subject path as its own group | 15.5394 | 18.2953 | 1.1773x | 3.5669 | 12.7531 | 0.2797x | differs |
| rect clip: every subject path separately | 0.4828 | 0.6419 | 1.3295x | 0.4273 | 0.6633 | 0.6441x | identical |

## Port against native C++ (same time window)

| workload | C++ ms | port ms | port/C++ |
|---|---:|---:|---:|
| boolean ops (Polygons.txt, all cases) | 22.03 | 17.4419 | 1.26x |
| union: add subject paths only (ingestion) | 0.12 | 0.0731 | 1.64x |
| union of every subject path (Paths64) | 187.6 | 165.2154 | 1.14x |
| union of the same paths moved apart (no intersections) | 4.87 | 4.4585 | 1.09x |
| offsets (Offsets.txt, delta = 1, Round/Polygon) | 1.25 | 1.1936 | 1.05x |
| offsets (Offsets.txt, delta = 27, Miter/Square) | 2.46 | 3.0846 | 0.8x |
| rect clip (off all subjects into 100,100,700,500) | 0.45 | 0.4347 | 1.04x |
| rect clip lines (off all subjects into 100,100,700,500) | 0.39 | 0.2668 | 1.46x |
| simplify paths (all subjects, eps = 0.5) | 0.08 | 0.0587 | 1.36x |
| triangulate 2000 vertex polygon | 1.26 | 0.9472 | 1.33x |
| area (Paths64 of every subject+clip) | 0.04 | 0.017 | 2.35x |
| get bounds (Paths64 of every subject+clip) | 0.04 | 0.0156 | 2.56x |
| point in polygon (200k probes) | 441.1 | 710.7713 | 0.62x |
| scale round trip (Paths64 -> PathsD -> Paths64, x1000) | 0.09 | 0.053 | 1.7x |
| offset: every subject path as its own group | 14.9 | 15.5394 | 0.96x |
| rect clip: every subject path separately | 0.5 | 0.4828 | 1.04x |

C++ build: MinGW g++ 13.1, -O3 -DNDEBUG -march=native -static, Clipper2 2.0.1 default build (no CLIPPER2_HI_PRECISION).

``port/C++`` above 1.00 means the port is faster. Pairs are best-of runs taken in one
window; the short rows still move by +-10-15% between windows on this machine. The rows
below 1.00 are listed with their profile under 'Where the remaining time goes'.

## Verification status

- tests: 35/35 MSTest
- golden output hashes: Results/golden-hashes.txt: 6409 hashes of the complete output (every coordinate, path order, polytree nesting) over the test corpus and a 4000 case fuzz corpus, recorded from the round 2 build; the round 3 build reproduces all 6409 (dotnet run -c Release --project benchmark/Clipper2.PortProfile -- golden verify)
- Z flavour: Clipper2ZLib (USINGZ): 1500 Z-callback cases (booleans, polytrees, offsets, ClipperD) hash identically to the round 2 build
- vs C++: 197 of 197 cases identical to the C++ -O2 build (path count and area); the -march=native build differs from its own -O2 sibling on 8 of them (FMA contraction), and the port matches the non-contracting builds. Dumps: `Results/fidelity-cpp.txt vs Results/fidelity-port.txt (diff them case by case)`
- vs upstream C# 2.0.0: 193 of 195 polygon cases identical to the upstream C# 2.0.0 port; the 2 remaining boolean cases and both offset cases differ where 2.0.0 and 2.0.1 legitimately diverge (the port matches C++)
- public surface: Clipper2Lib namespace, Clipper64/ClipperD/ClipperOffset/PolyTree/Paths/Point/JoinType... as in the upstream C# port; see docs/porting-notes.md 2.1 for the JoinType member numbering; round 3 adds PointInPolygonLocator, Clipper.PointInPolygon(polygon, points, results) and Clipper.BooleanOpParallel
- profiler: dotnet-trace collect (default sampling profile) on benchmark/Clipper2.PortProfile, which runs the port alone + dotnet-trace report topN

## Where the remaining time goes (round 3 profile, docs/porting-notes.md 4.6)

| area | measured share | status |
|---|---|---|
| offset, Miter, delta = 27 (2 large cases) | BuildIntersectList 30%, DoHorizontal 15%, DoTopOfScanbeam 8%, InsertLeftEdge 8%, scanline heap 6% | 0.80x of C++: long AEL walks over the actives arena; candidates are a hot/cold split of Active and fewer duplicate scanline pushes |
| point in polygon, single call | ~15 ns per call on 18-vertex paths vs ~9 ns in C++; a 2-points-per-compare SIMD Y scan measured no gain | 0.62x of C++ per call; for many probes use PointInPolygonLocator / Clipper.PointInPolygon(polygon, points, results) - identical answers, 9x faster on this workload |
| intersection list sort | 25% of the union of all 249 paths | already sorts plain 24 byte nodes; the algorithm itself is fixed (tie order decides bit-exactness), a branchless comparer measured no gain |
| triangulation | GC ~30%, ForceLegal ~33% of a run | 1.33x of C++; the per vertex edge lists (List<Edge>) and per triangle objects are what is left to pool |

## Logs

- `Results/benchmark-final.log` (8.3 KB)
- `Results/benchmark-run1.log` (8.3 KB)
- `Results/benchmark-run2.log` (8.3 KB)
- `Results/benchmark-v5-1.log` (9.9 KB)
- `Results/benchmark-v5-2.log` (9.9 KB)
- `Results/benchmark-v6-baseline.log` (10 KB)
- `Results/benchmark-v7-round3.log` (10 KB)
- `Results/cpp-native.log` (1.9 KB)
- `Results/fidelity-cpp.txt` (5.4 KB)
- `Results/fidelity-port.txt` (5.2 KB)
- `Results/golden-hashes.txt` (191.1 KB)

