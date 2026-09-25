# Building and publishing the NuGet package

The library is published to nuget.org as **`Clipper2Sharp`** (the assembly and the
namespace stay `Clipper2Lib`, so the package is a drop-in for the upstream C# port).

| | |
|---|---|
| package id | `Clipper2Sharp` |
| assembly / namespace | `Clipper2Lib` |
| target framework | `net10.0` — one managed assembly: no P/Invoke, no native dependency, no `unsafe`, no NuGet dependencies. Runs on Windows, Linux and macOS (x64, arm64) and in the browser as WebAssembly |
| version | `<Version>` in `Directory.Build.props` (currently `2.0.1`) |
| licence | `BSL-1.0` (the Clipper2 licence, `PackageLicenseExpression`) |
| package readme | the repository `README.md`, packed as `README.md` |
| symbols | `.snupkg`, published next to the package |
| project to pack | `src/Clipper2Lib/Clipper2Lib.csproj` |

Only that one project is packable: `src/Clipper2ZLib` (the `USINGZ` flavour of the
same sources) and `utils/Clipper.FileIO` are marked `<IsPackable>false</IsPackable>`.
If the Z flavour should become its own package some day, it needs its own
`PackageId` and a second push step.

## The two pipelines

`.github/workflows/ci.yml` — on every push to `main`, every pull request and on
demand:

1. restore and build **`Clipper2Sharp.Ci.slnx`** (see below) in Release,
2. `dotnet test tests/Clipper2.Tests/Clipper2.Tests.csproj` (35 tests),
3. the bit-exactness gates, which read committed files and therefore need no C++
   toolchain:
   * `PortProfile golden verify Results/golden-hashes.txt` — every coordinate,
     path order and polytree nesting of the whole corpus plus the fuzz corpus,
   * `PortProfile fidelity Results/fidelity-cpp.txt` — the per case count/area dump
     of the 195 boolean and 2 offset cases against the C++ dump,
   * `PortProfileZ` — the same for the `USINGZ` flavour,
4. `dotnet pack`, then it inspects the package (assembly + README must be in it) and
   consumes it: a scratch `dotnet new console` project adds the `.nupkg` from a local
   folder source, calls `Clipper.Intersect` and checks the result,
5. uploads the test report and the `.nupkg`/`.snupkg` as artifacts.

`.github/workflows/publish.yml` — on a `v*` tag (and on demand, see *Dry run*):
the same build/test/gate/pack sequence, then the upload to nuget.org. The tag name
is checked against `<Version>` in `Directory.Build.props` before anything is
packed, so a mismatched tag fails instead of publishing a surprise version.

### Why there is a second solution file

`Clipper2Sharp.slnx` includes `benchmark/Clipper2.Benchmark`, which compiles the
**upstream C# port** from a local Clipper2 checkout (`benchmark/ReferenceClipper2Lib`,
root overridable with `-p:Clipper2ReferenceRoot=…`) and reads the test corpus from
that checkout. A CI runner has neither, so it builds `Clipper2Sharp.Ci.slnx`, which
holds src, utils, examples, tests and the port-only drivers. Keep the two files in
sync when a project is added.

## Releasing

```bash
# 1. bump the version (and, if the algorithms changed, re-record the guards)
#    Directory.Build.props: <Version>2.0.1</Version> -> 2.0.2
dotnet run -c Release --project benchmark/Clipper2.PortProfile -- golden record
dotnet run -c Release --project benchmark/Clipper2.PortProfileZ -- record

# 2. commit, then tag that commit
git commit -am "Bump version to 2.0.2"
git tag v2.0.2
git push origin main --tags
```

The tag starts the publish workflow. Version numbers are permanent on nuget.org:
`dotnet nuget push` runs with `--skip-duplicate`, so re-running a tag that already
published is a no-op rather than an error, but it can never replace a package.

### Dry run

Actions → **Publish to NuGet** → *Run workflow* uses the version currently in
`Directory.Build.props`, does the whole job except the upload, and keeps the
`.nupkg` as an artifact. Switch the `dry_run` input off to upload without a tag.

## One-time setup on nuget.org

The workflow authenticates with **trusted publishing** (OIDC) when the
`NUGET_API_KEY` secret is absent, and falls back to that secret when it is present.
Pick one.

### A. Trusted publishing (keyless, no long-lived secret)

1. nuget.org → **Account → Trusted Publishing → Add policy**:

   | field | value |
   |---|---|
   | Repository Owner | `DimQ1` |
   | Repository | `Clipper2Sharp` |
   | Workflow File | `publish.yml` (the file name only — not `.github/workflows/`, not the workflow `name:`) |
   | Environment | `release` |

   The environment is optional: fill it in to restrict the policy to that
   environment (it must then match the workflow's `environment:` exactly), or
   leave it blank — the claim is only checked when the policy sets it.

2. On the same policy, set the **scopes**: allow pushing **new packages** and **new
   versions of existing packages**, and restrict the package **glob** to
   `Clipper2Sharp` (or `*` if you intend to publish more packages from here) — a
   policy without the matching scope will refuse the push.

3. Choose the **policy owner**: your own account, or an organization you are an
   active member of. The policy covers every package that owner owns, and it goes
   inactive if that owner (or your membership) disappears.

4. GitHub → repository → **Settings → Environments → New environment** → `release`,
   then add an **environment secret** `NUGET_USER` with the nuget.org *profile name*
   (not the e-mail) — or a repository-level secret, both are visible to the job.
   Optionally add required reviewers to gate every publish.

The policy is **temporarily active for 7 days** while the GitHub repository is
private: it behaves normally, but if nothing is published within that window it
becomes inactive — and the 7-day window can be restarted from the policy page at
any time. The first successful publish is what makes it permanent (it is the one
that confirms the repository's numeric id to nuget.org).

### B. API key secret

GitHub → repository → **Settings → Secrets and variables → Actions → New repository
secret** → `NUGET_API_KEY`, value = a nuget.org key with **Push new packages and
package versions** scope. The workflow then skips the OIDC login and uses the key.

## Checking a package locally before any of that

```powershell
dotnet pack src/Clipper2Lib/Clipper2Lib.csproj -c Release -o artifacts
# the package is a zip: this lists what will be in it
Expand-Archive -Force artifacts\Clipper2Sharp.2.0.1.nupkg "$env:TEMP\nupkg-inspect"
Get-ChildItem -Recurse "$env:TEMP\nupkg-inspect" | Select-Object FullName
```

## Troubleshooting

| symptom | cause | fix |
|---|---|---|
| `NuGet/login` fails with 403 | `id-token: write` missing | it is set in the workflow; don't remove it |
| "no matching policy" / push unauthorized | policy fields do not match, e.g. workflow file name or the environment | `publish.yml`, owner `DimQ1`, repo `Clipper2Sharp`; if the policy sets an environment it must be `release` |
| push rejected although the policy exists | the policy has no scope for that action, or the package glob does not match | allow *new packages* + *new versions*, glob `Clipper2Sharp` |
| "no credentials" from the push step | neither secret is set | add `NUGET_API_KEY`, or `NUGET_USER` + the nuget.org policy |
| tag version mismatch | tag and `<Version>` differ | bump `<Version>`, commit, tag *that* commit |
| `already_exists` | the version is already on nuget.org | nothing to do — `--skip-duplicate` makes this a pass; bump the version for a real release |
| a re-run repeats the old YAML | `gh run rerun` replays the workflow from the tagged commit | push a fix to `main` and re-run, or use the manual dry run |

The temporary key is valid for **one hour and one use**: the OIDC token is
exchanged for a single API key, which is why the login step sits immediately before
the push instead of at the top of the job.

The GitHub Release (with the `.nupkg` attached) is deliberately **not** part of the
publish workflow — a conflicting release would otherwise block the NuGet upload.
Create it separately if wanted:

```bash
gh release create v2.0.2 --generate-notes artifacts/Clipper2Sharp.2.0.2.nupkg
```
