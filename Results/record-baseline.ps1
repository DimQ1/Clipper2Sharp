<#
.SYNOPSIS
  Turns the benchmark output into the recorded baseline (the "known starting
  point" future optimisation work is measured against).

.DESCRIPTION
  Reads Results/baseline.json (written by
  `dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --write-baseline=Results/baseline.json`)
  and adds the sections the benchmark itself does not own:

    * method        - how the numbers were produced
    * nativeCpp     - the same workloads against the native C++ benchmark,
                      paired from Results/cpp-native.log
    * verification  - test and fidelity status
    * nextTargets   - where the profiler says the remaining time is

  Those sections survive later --write-baseline runs (the benchmark replaces
  only recordedAtUtc / environment / managed), and this script regenerates the
  human readable Results/BASELINE.md from the enriched file.

  method / verification / nextTargets are only written when the file does not
  have them yet (or with -RefreshDefaults), so the recorded numbers inside them
  are not silently reset to the defaults this script was first written with.

.EXAMPLE
  pwsh Results/record-baseline.ps1

.EXAMPLE
  # put the built-in defaults back (drops hand edits to those three sections)
  pwsh Results/record-baseline.ps1 -RefreshDefaults
#>
[CmdletBinding()]
param(
  [string] $JsonPath = (Join-Path $PSScriptRoot 'baseline.json'),
  [string] $CppLog   = (Join-Path $PSScriptRoot 'cpp-native.log'),
  [string] $MarkdownPath = (Join-Path $PSScriptRoot 'BASELINE.md'),
  [switch] $RefreshDefaults
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $JsonPath)) {
  throw "no $JsonPath - run the benchmark with --write-baseline first"
}

$json = Get-Content $JsonPath -Raw | ConvertFrom-Json

# ---------------------------------------------------------------- native C++ rows
$cpp = @{}
if (Test-Path $CppLog) {
  foreach ($line in Get-Content $CppLog) {
    if ($line -match '^(?<label>.+?)\s+cpp\s+(?<ms>[\d.]+) ms') {
      # the managed labels say "off all subjects into" and "Miter/Square", the
      # native ones "all subjects into" and "Miter/Polygon" - normalise so the
      # two sides can be paired row by row
      $label = $Matches.label.Trim()
      $label = $label -replace 'rect clip \(all subjects', 'rect clip (off all subjects'
      $label = $label -replace 'rect clip lines \(all subjects', 'rect clip lines (off all subjects'
      $label = $label -replace 'Miter/Polygon', 'Miter/Square'
      $cpp[$label] = [double] $Matches.ms
    }
  }
}

$nativeRows = @()
foreach ($row in $json.managed) {
  if ($cpp.ContainsKey($row.workload)) {
    $nativeRows += [pscustomobject]@{
      workload   = $row.workload
      cppMs      = $cpp[$row.workload]
      portMs     = $row.portMs
      portOverCpp = [math]::Round($cpp[$row.workload] / $row.portMs, 2)
    }
  }
}

# ---------------------------------------------------------------- enrichment
$method = [pscustomobject]@{
  managed = 'interleaved A/B in one process (order swapped every round), fastest uninterrupted repetition per round (first repetition dropped), best of 7 rounds'
  native  = 'same workload definitions in benchmark/Clipper2.CppBenchmark, best of 3 processes, allocation counted with a replacing operator new'
  caveat  = 'this machine runs at 75-85% background CPU, so time ratios of the short rows move by +-10-15% between sessions; allocation numbers are deterministic'
}

$native = [pscustomobject]@{
  build = 'MinGW g++ 13.1, -O3 -DNDEBUG -march=native -static, Clipper2 2.0.1 default build (no CLIPPER2_HI_PRECISION)'
  log   = 'Results/cpp-native.log'
  rows  = $nativeRows
  note  = 'the C++ rows and the managed rows in this file were measured in the same time window; rebuild the native side with the command in benchmark/Clipper2.CppBenchmark/clipper_bench.cpp'
}

$verification = [pscustomobject]@{
  tests                = '32/32 MSTest'
  goldenHashes         = 'Results/golden-hashes.txt: 6409 hashes of the complete output (every coordinate, path order, polytree nesting) over the test corpus and a 4000 case fuzz corpus, recorded from the round 2 build; the round 3 build reproduces all 6409 (dotnet run -c Release --project benchmark/Clipper2.PortProfile -- golden verify)'
  cppFidelity          = '197 of 197 cases identical to the C++ -O2 build (path count and area); the -march=native build differs from its own -O2 sibling on 8 of them (FMA contraction), and the port matches the non-contracting builds'
  cppFidelityFiles     = 'Results/fidelity-cpp.txt vs Results/fidelity-port.txt (diff them case by case)'
  csharpFidelity       = '193 of 195 polygon cases identical to the upstream C# 2.0.0 port; the 2 remaining boolean cases and both offset cases differ where 2.0.0 and 2.0.1 legitimately diverge (the port matches C++)'
  zFlavour             = 'Clipper2ZLib (USINGZ): 1500 Z-callback cases (booleans, polytrees, offsets, ClipperD) hash identically to the round 2 build'
  profileTool          = 'dotnet-trace collect (default sampling profile) on benchmark/Clipper2.PortProfile, which runs the port alone + dotnet-trace report topN'
  publicSurface        = 'Clipper2Lib namespace, Clipper64/ClipperD/ClipperOffset/PolyTree/Paths/Point/JoinType... as in the upstream C# port; see docs/porting-notes.md 2.1 for the JoinType member numbering; round 3 adds PointInPolygonLocator, Clipper.PointInPolygon(polygon, points, results) and Clipper.BooleanOpParallel'
}

$nextTargets = @(
  [pscustomobject]@{
    area     = 'offset, Miter, delta = 27 (2 large cases)'
    measured = 'BuildIntersectList 30%, DoHorizontal 15%, DoTopOfScanbeam 8%, InsertLeftEdge 8%, scanline heap 6%'
    status   = '0.80x of C++: long AEL walks over the actives arena; candidates are a hot/cold split of Active and fewer duplicate scanline pushes'
  }
  [pscustomobject]@{
    area     = 'point in polygon, single call'
    measured = '~15 ns per call on 18-vertex paths vs ~9 ns in C++; a 2-points-per-compare SIMD Y scan measured no gain'
    status   = '0.62x of C++ per call; for many probes use PointInPolygonLocator / Clipper.PointInPolygon(polygon, points, results) - identical answers, 9x faster on this workload'
  }
  [pscustomobject]@{
    area     = 'intersection list sort'
    measured = '25% of the union of all 249 paths'
    status   = 'already sorts plain 24 byte nodes; the algorithm itself is fixed (tie order decides bit-exactness), a branchless comparer measured no gain'
  }
  [pscustomobject]@{
    area     = 'triangulation'
    measured = 'GC ~30%, ForceLegal ~33% of a run'
    status   = '1.33x of C++; the per vertex edge lists (List<Edge>) and per triangle objects are what is left to pool'
  }
)

# nativeCpp is always recomputed (it is paired from the log and the fresh managed
# rows); the other three keep whatever the file already holds, because their
# numbers are referenced by hand-maintained text
foreach ($section in @(
  [pscustomobject] @{ name = 'method';       value = $method }
  [pscustomobject] @{ name = 'verification'; value = $verification }
  [pscustomobject] @{ name = 'nextTargets';  value = $nextTargets }
)) {
  $existing = $json.PSObject.Properties[$section.name]
  if ($RefreshDefaults -or $null -eq $existing -or $null -eq $existing.Value) {
    $json | Add-Member -Force -NotePropertyName $section.name -NotePropertyValue $section.value
  }
}
$json | Add-Member -Force -NotePropertyName nativeCpp -NotePropertyValue $native

$json | ConvertTo-Json -Depth 8 | Set-Content $JsonPath -Encoding utf8

# the markdown view prints the file's own sections, not the defaults above
$method       = $json.method
$verification = $json.verification
$nextTargets  = $json.nextTargets

# ---------------------------------------------------------------- markdown view
$md = New-Object System.Text.StringBuilder
[void] $md.AppendLine('# Benchmark baseline (known starting point)')
[void] $md.AppendLine()
[void] $md.AppendLine("Recorded $($json.recordedAtUtc) on this machine ($($json.environment.processors) logical processors,")
[void] $md.AppendLine("$($json.environment.framework), $($json.environment.os)).")
[void] $md.AppendLine()
[void] $md.AppendLine('Everything below is also in `Results/baseline.json` (machine readable, and the file the')
[void] $md.AppendLine('benchmark reads with `--baseline=`), together with `Results/cpp-native.log` (the native')
[void] $md.AppendLine('side) and the two fidelity dumps.')
[void] $md.AppendLine()
[void] $md.AppendLine('## Reproduce / compare')
[void] $md.AppendLine()
[void] $md.AppendLine('```powershell')
[void] $md.AppendLine('# compare the current build against the recorded point (prints now / baseline / delta / speed)')
[void] $md.AppendLine('dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --baseline=Results/baseline.json')
[void] $md.AppendLine()
[void] $md.AppendLine('# record a new known point (keeps the native/verification/targets sections)')
[void] $md.AppendLine('dotnet run -c Release --project benchmark/Clipper2.Benchmark -- 7 --write-baseline=Results/baseline.json')
[void] $md.AppendLine('')
[void] $md.AppendLine('pwsh Results/record-baseline.ps1   # re-pair the native rows and regenerate this file')
[void] $md.AppendLine('```')
[void] $md.AppendLine()
[void] $md.AppendLine("Method: $($method.managed); native side: $($method.native).")
[void] $md.AppendLine("Caveat: $($method.caveat)")
[void] $md.AppendLine()
[void] $md.AppendLine('## Port against the upstream C# 2.0.0 port (same process, interleaved rounds)')
[void] $md.AppendLine()
[void] $md.AppendLine('| workload | port ms | ref ms | speed-up | port MB | ref MB | allocation | vs C# 2.0.0 |')
[void] $md.AppendLine('|---|---:|---:|---:|---:|---:|---:|---|')
foreach ($row in $json.managed) {
  $res = if ($row.resultsIdentical) { 'identical' } else { 'differs' }
  [void] $md.AppendLine("| $($row.workload) | $($row.portMs) | $($row.refMs) | $($row.speedupVsRefCSharp)x | $($row.portMB) | $($row.refMB) | $($row.allocRatioVsRefCSharp)x | $res |")
}
[void] $md.AppendLine()
[void] $md.AppendLine('## Port against native C++ (same time window)')
[void] $md.AppendLine()
[void] $md.AppendLine('| workload | C++ ms | port ms | port/C++ |')
[void] $md.AppendLine('|---|---:|---:|---:|')
foreach ($row in $nativeRows) {
  [void] $md.AppendLine("| $($row.workload) | $($row.cppMs) | $($row.portMs) | $($row.portOverCpp)x |")
}
[void] $md.AppendLine()
[void] $md.AppendLine("C++ build: $($native.build).")
[void] $md.AppendLine()
[void] $md.AppendLine('``port/C++`` above 1.00 means the port is faster. Pairs are best-of runs taken in one')
[void] $md.AppendLine('window; the short rows still move by +-10-15% between windows on this machine. The rows')
[void] $md.AppendLine('below 1.00 are listed with their profile under ''Where the remaining time goes''.')
[void] $md.AppendLine()
[void] $md.AppendLine('## Verification status')
[void] $md.AppendLine()
[void] $md.AppendLine("- tests: $($verification.tests)")
if ($verification.goldenHashes) { [void] $md.AppendLine("- golden output hashes: $($verification.goldenHashes)") }
if ($verification.zFlavour) { [void] $md.AppendLine("- Z flavour: $($verification.zFlavour)") }
[void] $md.AppendLine("- vs C++: $($verification.cppFidelity). Dumps: ``$($verification.cppFidelityFiles)``")
[void] $md.AppendLine("- vs upstream C# 2.0.0: $($verification.csharpFidelity)")
[void] $md.AppendLine("- public surface: $($verification.publicSurface)")
[void] $md.AppendLine("- profiler: $($verification.profileTool)")
[void] $md.AppendLine()
[void] $md.AppendLine('## Where the remaining time goes (round 3 profile, docs/porting-notes.md 4.6)')
[void] $md.AppendLine()
[void] $md.AppendLine('| area | measured share | status |')
[void] $md.AppendLine('|---|---|---|')
foreach ($t in @($nextTargets)) {
  [void] $md.AppendLine("| $($t.area) | $($t.measured) | $($t.status) |")
}
[void] $md.AppendLine()
[void] $md.AppendLine('## Logs')
[void] $md.AppendLine()
foreach ($file in (Get-ChildItem $PSScriptRoot -File | Where-Object { $_.Extension -in '.log', '.txt' } | Sort-Object Name)) {
  [void] $md.AppendLine("- ``Results/$($file.Name)`` ($([math]::Round($file.Length / 1KB, 1)) KB)")
}

Set-Content $MarkdownPath $md.ToString() -Encoding utf8

Write-Host "baseline enriched: $JsonPath"
Write-Host ("native rows paired: {0} of {1}" -f $nativeRows.Count, $json.managed.Count)
Write-Host "markdown written : $MarkdownPath"
