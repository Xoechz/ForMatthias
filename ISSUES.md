# Implementation issues

## Issue 1 - Installed SDK is a release candidate

M0 / M7

The provided environment has .NET SDK 11.0.100-rc.3.26522.148, not a stable .NET 11 SDK. The application targets net11.0 as required and uses stable direct NuGet dependency versions. Verification here uses the installed SDK; stable SDK and Windows/macOS verification must also run in CI. The three-platform workflow is supplied, but remote runner results cannot be claimed from this Linux environment. This is an outstanding release-verification requirement.

## Issue 2 - Redirected restore hangs with reusable MSBuild nodes (resolved)

M5

Integration testing exposed solution restores whose reusable child MSBuild nodes retained redirected output handles. Explicit restore and property-discovery subprocesses now pass `-nr:false`. The linked-project integration fixture completes and validates one physical write.

## Issue 3 - Conservative directive and attribute scopes

M2 / M3 / M5

Types containing context-bearing directives are conservatively skipped as entire organization scopes, including directives inside bodies that could leak into following declarations. Unknown type/field attributes pin layout-sensitive sequences; attributed interfaces pin all members. This intentionally favors preservation over smaller movable segments. Independent types remain eligible. Optional attribute/indexer/tuple wrapping targets are rejected as unsupported configuration, as permitted by the v1 contract.

## Issue 4 - Formatter continuation indentation versus custom wrapping (resolved)

M4 / M6

Dogfooding found that Roslyn's continuation indentation and newly multiline enclosing blocks could change a custom wrap on the following invocation. Review 1 subsequently identified that preserving whole list interiors prevented proper formatting. The corrected implementation preserves token breakpoints only, normalizes list-item indentation, applies Roslyn spacing throughout interiors, and converges targeted affected-member formatting with overflow re-evaluation. Generated directives are reparsed into canonical trivia ownership. Runtime fixed-point gates, exact-output regressions and full-solution dogfooding verify the resolution.

## Issue 5 - MSBuildWorkspace can return an SDK project without restored assets (resolved)

M5 / M6

A no-restore regression showed that a nonthrowing project load could still yield incomplete compilation context. The loader now checks evaluated SDK `ProjectAssetsFile` existence and physical metadata reference availability rather than trusting the open call or reference count alone. Missing assets produce exit 3 and instruct the user to restore explicitly. The loader never substitutes an incomplete compilation baseline.

## Issue 6 - SDK permits only one tool command (resolved naming adjustment)

M7

An installation spike verified that .NET tool restore rejects multi-command settings ("More than one command is defined for the tool"). The package therefore retains the `formatthias` command to satisfy the required `dotnet tool run formatthias` local-manifest workflow. Local installations also support `dotnet formatthias`; global/tool-path installations use standalone `formatthias`. A global `dotnet formatthias` shorthand would require renaming the command to `dotnet-formatthias`, losing the specified local long-form name. Documentation records this allowed command-naming adjustment; no unsupported alias is shipped.

## Issue 7 - Partial-type layout and interop constraints were local to one syntax part (resolved)

M2 / M5 / M6 — Review 1 finding 1

Safety analysis now resolves the combined type symbol, including attributes on other partial declarations and aliases, and unions constraints across applicable project/TFM contexts. Ordering and independent candidate safety validation use the same physical-part constraint identities. Syntax-only partial declarations conservatively pin unknown layout/interop sequences. Multi-TFM partial class/interface regressions verify preservation and rejection of deliberately reordered unsafe candidates.

## Issue 8 - Type-level diagnostic identity depended on member order (resolved)

M5 / M6 — Review 1 finding 2

Compiler diagnostic origins now use namespace-qualified physical type-part/header identity rather than hashing a type's reorderable body. Baseline CS1591 warnings survive member movement, including `--fail-on-new-warnings`. Ordinary declaration diagnostic identities remain independent of whitespace and source line positions.

## Issue 9 - Invocation wrapping switches were OR-combined (resolved)

M4 / M5 — Review 1 finding 3

Method and delegate calls are distinguished using bound `IMethodSymbol.MethodKind` in every applicable semantic context. Differing switches never authorize the disabled target. Missing, dynamic, syntax-only or inconsistent binding preserves ambiguous overflowing lists and emits CSORG4002; the skipped policy applies. Equal switches need no semantic classification.

## Issue 10 - Full formatting froze multiline-list interior whitespace (resolved)

M4 / M6 — Review 1 finding 4

Entire syntax lists are no longer restored after formatting. Token-level newline breakpoints retain grouping while list indentation and Roslyn expression spacing are corrected. Formatting converges with bounded iteration; final post-wrap formatting is restricted to affected member spans. Exact-output coverage verifies misindented arguments and `a+  b` become correctly formatted without collapsing existing breakpoints.

## Issue 11 - Structural gate lacked complete scope/region ownership checks (resolved)

M3 / M6 — Review 1 finding 5

Direct declaration/comment/attribute ownership is checked per namespace, type and physical partial part (including repeated namespace blocks). Imports are mapped to their physical scopes. Token-level compiler-directive context, disabled source, top-level execution order and unmanaged/body-region boundaries are preserved. Accepted organization scopes explicitly validate generated region labels, minimum sizes and exact configured contiguous-run membership. Negative tests submit cross-class/namespace/part moves, moved directive contexts and malformed/wrong regions to the validator.

## Issue 12 - Incomplete Razor binding could falsely justify removing a required import (resolved)

M5 / M6 — Delifhery verification

The first real-repository post-format build caught removal of `using OPA.Components` while the workspace lacked the Razor-generated `App` type. Compiler CS8019 is now gated on error-free compilation binding in every applicable context. Otherwise unused-import cleanup is preserved and reported as CSORG5005, including skipped-capability policy. Formatter changes from the failed attempt were restored from hash-checked pre-run snapshots. The corrected run retained the import, passed the full 16-project build, and produced zero second-run diff. A focused incomplete-generated-binding regression prevents recurrence.

## Issue 13 - Cached NuGet audit warnings are reported as workspace failures

M5 / M7 — Delifhery verification environment

The supplied test solution built successfully, but Roslyn reported its cached NU1902/NU1903 audit messages as failure-kind workspace diagnostics. Strict project loading correctly refused the initial run. For the controlled formatter test, restore metadata was refreshed explicitly with `dotnet restore -p:NuGetAudit=false -nr:false`; no project settings or package versions in source were edited. This is a verification preparation workaround, not a change to production loader policy or a resolution of the repository's dependency advisories. The original audit warnings were recorded before the refresh.

## Issue 14 - Additional Humanizer smoke exceeded the exploratory time budget

M7 — External smoke exploration

An additional whole-tree Humanizer syntax smoke at revision 4574b93883bf48e3f0f3bca73297054537317ab2 exceeded a 240-second command budget before a completed report. It is not counted as a passing acceptance run or as throughput evidence. Completed Serilog, supplied Delifhery, and the controlled synthetic solution runs are recorded separately; there is no general throughput guarantee.

## Review 1 verification record

- Final Release suite: **100 passed, 0 failed, 0 skipped**, including all five review findings and the additional real-repository cleanup defect.
- Supplied Delifhery solution: 148 files analyzed, 128 changed, 0 failed; second check/diff returned 0 with no diff; post-format full build succeeded with 0 errors. Snapshots/reports: `/tmp/opencode/delifhery-review-fixed`.
- Disposable Serilog syntax smoke: 217 files analyzed, 185 changed, 0 failed; second check/diff returned 0 with no diff. Representative diffs were manually reviewed.
- Synthetic 10-project, 300-file, 9,000-method dependency-chain solution: first validated write 32.096 seconds; second project-aware check/diff 18.110 seconds, exit 0 and zero diff. No diagnostic or failed file.
- Updated NuGet package built and installed into a fresh tool path/cache. Installed-tool smoke and Delifhery project checks returned 0; Delifhery installed-tool report has 0 changed and 0 failed.
- Details, commands, revisions, manual-review observations and limitations: `docs/review1-verification.md`.
- Remaining platform/SDK verification and exploratory constraints remain explicit in Issues 1, 13 and 14; no publication was performed.

## Initial verification record (superseded by Review 1 verification)

- Release build and tests: **78 passed, 0 failed, 0 skipped**, using the installed .NET 11 RC3 SDK on Linux.
- Exact golden fixtures and a seeded 100-case ordering invariant test pass.
- Disposable complete-solution dogfood: explicit restore, candidate compilation validation, guarded writes, and zero second-run diff pass.
- Real project fixtures cover SLN/SLNX, linked files, net10.0/net11.0 contexts, strict missing-assets failure, explicit fallback, unused imports and new compiler diagnostics.
- `artifacts/dotnet-formatthias.1.0.0.nupkg` is produced successfully. Local manifest restore and installed `dotnet tool run formatthias tests/Repositories/Smoke/Smoke.slnx --check` exit 0. Tool-path installation and execution also succeeded.
- NuGet exact package-name search returned no collision on 2026-11-12. No publication performed.
- Windows/macOS and stable-SDK results remain subject to Issue 1; CI configuration is not a substitute for actual runner results.
