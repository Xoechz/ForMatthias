# Development

## Layout and dependencies

- `ForMatthias.Core`: immutable requests/results, configuration/schema resolution, EditorConfig bridge, ordering constraints, regions, formatting, wrapping, imports and structural validation.
- `ForMatthias.Workspaces`: physical snapshots/identity, discovery, MSBuild loading, semantic import cleanup, compilation baselines and guarded commit abstraction.
- `ForMatthias.Cli`: argument grammar, orchestration, unified diffs, JSON/human reports and exit policy.
- `ForMatthias.Tests`: API feasibility, configuration, unit/invariant, exact golden, CLI, IO, semantic integration and disposable repository dogfood tests.

All projects target `net11.0`, solution format is SLNX, dependencies use central package management. Roslyn is pinned consistently to 5.9.0 and Locator to 1.11.2; see [feasibility](feasibility.md). No internal Roslyn services are reflected or invoked. A small explicit parser implements the bounded CLI grammar without an extra argument-parser dependency.

```sh
dotnet restore ForMatthias.slnx
dotnet build ForMatthias.slnx --no-restore
dotnet test ForMatthias.slnx --no-restore
dotnet run --project src/ForMatthias.Cli -- ./ForMatthias.slnx --check
```

Integration fixtures restore explicitly before project loading. The loader itself never restores unless requested. TFM fixtures cover net10.0 and net11.0; reference packs can be downloaded during the explicit fixture restore. MSBuild property-discovery/restore subprocesses use `-nr:false` to avoid reusable child nodes retaining redirected pipes.

Golden fixtures compare exact text then require an identical second pass. Randomized tests use a fixed seed. Dogfooding runs every source through a second execution and also copies this complete solution into a disposable directory, restores explicitly, validates/transforms it, then requires no second-run diff. The original repository is not reformatted by dogfood tests.

The provided Linux environment uses .NET 11 RC3. Its `DOTNET_ROOT` initially referred to a different SDK; verification unsets `DOTNET_ROOT` and `DOTNET_ROOT_X64` so the active SDK resolves its own runtime. This environment-specific adjustment is not baked into the project. CI installs the .NET 11 SDK on Ubuntu, Windows and macOS and builds/tests/packages/installs the tool. Remote CI results are available only after the workflow runs.

## External and throughput verification

`tools/ForMatthias.Verification` is an opt-in executable, separate from the ordinary test solution. It runs real writes followed by `--check --diff`, reports timings and diagnostic counts, and fails if the second run differs. See [review verification](review1-verification.md) for recorded revisions, observations and results.

```sh
dotnet run --project tools/ForMatthias.Verification -c Release -- external /path/to/disposable/checkout
dotnet run --project tools/ForMatthias.Verification -c Release -- synthetic /path/to/new/disposable/directory
dotnet run --project tools/ForMatthias.Verification -c Release -- project /path/to/solution.slnx /path/to/new/reports-and-backups
dotnet run --project tools/ForMatthias.Verification -c Release -- restore /path/to/reports-and-backups
```

The synthetic mode explicitly restores a reproducible 10-project dependency chain with 300 files and 9,000 methods. Project mode assumes dependencies are already restored, snapshots the current physical C# sources, and keeps report/backup files outside the tested repository. Restore mode refuses to overwrite a file edited since verification. External mode is syntax-only and intended for disposable clones; it does not establish compiler equivalence.

## Distribution

`dotnet pack src/ForMatthias.Cli -c Release -o artifacts` creates `dotnet-formatthias.1.0.0.nupkg`. Test installation with `--tool-path`, or a standard local manifest. Packaging retains the Roslyn MSBuild BuildHost payload. Verify project-mode execution from the installed package, not just its version output. Recheck NuGet name ownership immediately before publishing; no publication happens during implementation.

The tool command is `formatthias`. Local manifests support `dotnet formatthias` and `dotnet tool run formatthias`. Global/tool-path installations use the standalone command. The SDK rejects multi-command tool settings, so a second global `dotnet-` alias is not packaged; see Issue 6.

The project is GPL-3.0-only. The package contains `LICENSE`, `SOURCE.md`,
`THIRD-PARTY-NOTICES.md`, and original upstream notices under `licenses/`.
The pack target gathers those notices from resolved copy-local NuGet dependencies;
it needs no extra packaging executable or online downloads after restore.
When upgrading dependencies, review their notices and update the version-matched
source links in [SOURCE.md](../SOURCE.md).

Before publishing a binary, publish its matching source commit and verify the
package's repository metadata points to it. Link to that source alongside the
release download, with dependency source access as documented in `SOURCE.md`.
A public, release-matched repository supplies project source without embedding
source ZIPs in each package. Local dirty-tree packages are verification artifacts,
not evidence that the matching source has already been published.

## Reporting and limitations

Diagnostic IDs are stable `CSORG` categories from the design. Reports have schema/tool versions, mode/input, normalized-path file statuses, applied/skipped capabilities, source spans, counts, hashes, committed flags and summary/exit code. Diagnostic ordering is normalized path then span then ID. Full source text is omitted.

Use `ISSUES.md` for blockers, implementation tradeoffs and unresolved verification. Maintain fixture coverage when fixing a defect. Whole-type directive skips, unknown-attribute conservatism, unsupported optional wrap targets and compiler-only validation are intentional supported limitations. No release should claim externally verified platform results before those jobs actually run.
