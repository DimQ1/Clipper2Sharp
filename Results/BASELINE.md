# Benchmark baseline (known starting point)

Recorded 09/25/2026 07:25:40 on this machine (12 logical processors,
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
| boolean ops (Polygons.txt, all cases) | 27.7608 | 29.9706 | 1.0796x | 17.3732 | 16.3343 | 1.0636x | differs |
| union: add subject paths only (ingestion) | 0.1186 | 0.1134 | 0.9562x | 0.4336 | 0.4496 | 0.9643x | identical |
| union of every subject path (Paths64) | 437.055 | 490.2661 | 1.1217x | 32.7463 | 32.6796 | 1.002x | differs |
| union of the same paths moved apart (no intersections) | 7.2685 | 7.3705 | 1.014x | 4.8866 | 4.1502 | 1.1774x | differs |
| offsets (Offsets.txt, delta = 1, Round/Polygon) | 1.8339 | 1.7949 | 0.9787x | 1.9308 | 2.5963 | 0.7437x | differs |
| offsets (Offsets.txt, delta = 27, Miter/Square) | 4.0103 | 3.9493 | 0.9848x | 1.4697 | 1.0716 | 1.3715x | identical |
| rect clip (off all subjects into 100,100,700,500) | 0.6075 | 0.7686 | 1.2652x | 0.6382 | 0.6225 | 1.0252x | identical |
| rect clip lines (off all subjects into 100,100,700,500) | 0.3732 | 0.4695 | 1.258x | 0.6183 | 0.5975 | 1.0349x | identical |
| simplify paths (all subjects, eps = 0.5) | 0.0812 | 0.0766 | 0.9433x | 0.1651 | 0.2127 | 0.7763x | identical |
| triangulate 2000 vertex polygon | 1.5105 | 1.7987 | 1.1908x | 1.0728 | 1.0925 | 0.9819x | identical |
| area (Paths64 of every subject+clip) | 0.0244 | 0.0237 | 0.9713x | 0.0854 | 0.0854 | 1.0004x | identical |
| get bounds (Paths64 of every subject+clip) | 0.0212 | 0.022 | 1.0377x | 0.0854 | 0.0854 | 1.0004x | identical |
| point in polygon (200k probes) | 1207.5223 | 1175.9904 | 0.9739x | 0.0854 | 0.0854 | 1x | identical |
| scale round trip (Paths64 -> PathsD -> Paths64, x1000) | 0.0647 | 0.0739 | 1.1422x | 0.2526 | 0.2523 | 1.0011x | identical |
| offset: every subject path as its own group | 22.1028 | 22.1669 | 1.0029x | 13.7857 | 12.7531 | 1.081x | differs |
| rect clip: every subject path separately | 0.6168 | 0.7746 | 1.2558x | 0.6851 | 0.6633 | 1.0328x | identical |

## Port against native C++ (same time window)

| workload | C++ ms | port ms | port/C++ |
|---|---:|---:|---:|
| boolean ops (Polygons.txt, all cases) | 41.2 | 27.7608 | 1.48x |
| union: add subject paths only (ingestion) | 0.18 | 0.1186 | 1.52x |
| union of every subject path (Paths64) | 318.37 | 437.055 | 0.73x |
| union of the same paths moved apart (no intersections) | 8.42 | 7.2685 | 1.16x |
| offsets (Offsets.txt, delta = 1, Round/Polygon) | 1.81 | 1.8339 | 0.99x |
| offsets (Offsets.txt, delta = 27, Miter/Square) | 3.58 | 4.0103 | 0.89x |
| rect clip (off all subjects into 100,100,700,500) | 0.68 | 0.6075 | 1.12x |
| rect clip lines (off all subjects into 100,100,700,500) | 0.57 | 0.3732 | 1.53x |
| simplify paths (all subjects, eps = 0.5) | 0.12 | 0.0812 | 1.48x |
| triangulate 2000 vertex polygon | 1.92 | 1.5105 | 1.27x |
| area (Paths64 of every subject+clip) | 0.06 | 0.0244 | 2.46x |
| get bounds (Paths64 of every subject+clip) | 0.06 | 0.0212 | 2.83x |
| point in polygon (200k probes) | 865.51 | 1207.5223 | 0.72x |
| scale round trip (Paths64 -> PathsD -> Paths64, x1000) | 0.13 | 0.0647 | 2.01x |
| offset: every subject path as its own group | 23.61 | 22.1028 | 1.07x |
| rect clip: every subject path separately | 0.75 | 0.6168 | 1.22x |

C++ build: MinGW g++ 13.1, -O3 -DNDEBUG -march=native -static, Clipper2 2.0.1 default build (no CLIPPER2_HI_PRECISION).

``port/C++`` above 1.00 means the port is faster; the two rows below 1.00 (the single
union of all 249 paths and point-in-polygon) are the ones where the native build's
tight single-threaded loop still wins - see docs/porting-notes.md 4.5 for the shares.

## Verification status

- tests: 29/29 MSTest
- vs C++: 197 of 197 cases identical to the C++ -O2 build (path count and area); the -march=native build differs from its own -O2 sibling on 8 of them (FMA contraction), and the port matches the non-contracting builds. Dumps: `Results/fidelity-cpp.txt vs Results/fidelity-port.txt (diff them case by case)`
- vs upstream C# 2.0.0: 193 of 195 polygon cases identical to the upstream C# 2.0.0 port; the 2 remaining boolean cases and both offset cases differ where 2.0.0 and 2.0.1 legitimately diverge (the port matches C++)
- public surface: Clipper2Lib namespace, Clipper64/ClipperD/ClipperOffset/PolyTree/Paths/Point/JoinType... as in the upstream C# port; see docs/porting-notes.md 2.1 for the JoinType member numbering
- profiler: dotnet-trace collect --profile dotnet-sampled-thread-time + dotnet-trace report topN

## Where the remaining time goes (profiler round, docs/porting-notes.md 4.5)

| area | measured share | status |
|---|---|---|
| union sweep (all 249 paths in one call) | BuildIntersectList 12%, IntersectEdges 7%, BuildPaths 5%, DoIntersections 4% of the row | 0.73x of C++ in the recorded window (0.68x in the profiler round's own window): remaining gap is managed overhead in the tight AEL/scanline loops; needs an index/struct based sweep and would put the 197/197 fidelity at risk |
| allocation on the small rows | boolean 1.06x, offset groups 1.08x, union 1.00x, triangulate 0.98x of the reference bytes (deterministic) | engine pools cover vertices/out-points/out-recs; left: the per-outrec Path64 in BuildPaths and the temporaries in ClipperOffset and RectClip64 |
| Delaunay.ForceLegal | 32% of a triangulation (two frames) | already 1.27x faster than C++ in the recorded window; the legalisation loop is the next candidate if triangulation matters |
| point in polygon | the Y-scan loops are ~84% of the row | 0.72x of C++, 0.97x of the C# 2.0.0 port in the recorded window (the profiler round's session read 0.84x and 1.19x for the same code) - the row that moves most between sessions, so re-measure it before optimising; further gains need a SIMD Y scan |
| GC throughput on tiny operations | GC poll worker + Buffer.Memmove: 42%+10% of boolean ops, 26%+22% of offsets, 85% of rect clip | these are the rows where the port already beats C++; cutting per-operation allocation is the lever |

## Logs

- `Results/benchmark-final.log` (8.3 KB)
- `Results/benchmark-run1.log` (8.3 KB)
- `Results/benchmark-run2.log` (8.3 KB)
- `Results/benchmark-v5-1.log` (9.9 KB)
- `Results/benchmark-v5-2.log` (9.9 KB)
- `Results/benchmark-v6-baseline.log` (10 KB)
- `Results/cpp-native.log` (1.9 KB)
- `Results/fidelity-cpp.txt` (5.4 KB)
- `Results/fidelity-port.txt` (5.4 KB)

