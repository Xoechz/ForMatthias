# Review 1 — fixes and verification

Date: 2026-11-12. Environment: Linux x86-64, installed .NET SDK 11.0.100-rc.3.26522.148, Roslyn 5.9.0. Commands unset the mismatched `DOTNET_ROOT`/`DOTNET_ROOT_X64` variables. Cross-platform/stable-SDK evidence remains outstanding under Issue 1.

## Findings addressed

| Finding | Implementation and regression evidence |
| --- | --- |
| 1. Partial layout/interop | `TypeSafetyAnalysis` resolves combined partial symbols and unions context constraints; `MemberOrganizer` and `CandidateValidator` enforce them. R1 tests cover conditional attributes across net10.0/net11.0, aliased layout attributes, partial interfaces, syntax uncertainty and deliberately unsafe candidates. |
| 2. Existing warnings reported as new | `DeclarationIdentity` supplies namespace-qualified physical part/header identities. R2 verifies baseline CS1591 with actual reorganization and `--fail-on-new-warnings`. |
| 3. Invocation target switches | Bound method kind distinguishes method/delegate calls, intersected across contexts. R3 covers both switch directions and explicit syntax-only ambiguity diagnostics. |
| 4. Multiline interior formatting | Token breakpoints replace whole-node restoration. R4 compares exact output for bad indentation and expression spacing, retaining grouped arguments and the final same-line parenthesis. Full-pipeline dogfood checks convergence. |
| 5. Structural guarantees | Per-container declaration ownership, token/directive contexts and explicit generated-run checks. R5 negative candidates cover classes, namespaces, repeated namespace blocks, nested/partial parts, header comments, directive movement, body-region deletion/relocation and incorrect canonical regions. |

The implementation also fixed a generator-context import-cleanup defect found during the requested real-repository rebuild (Issue 12). Missing compiler bindings now disable import removal with an explicit skipped diagnostic.

Final Release verification: **100 tests passed, 0 failed, 0 skipped** (the original 78 plus 22 review/real-repository regression cases). `dotnet test ForMatthias.slnx --no-restore -c Release` completed successfully. The verification executable also builds with **0 warnings and 0 errors**. The environment still emits the informational NETSDK1057 preview-SDK message.

The updated package was rebuilt at `artifacts/dotnet-formatthias.1.0.0.nupkg`, installed into a fresh isolated tool-path/cache, and executed against both the packaged smoke fixture and the already-formatted Delifhery solution. Both checks returned 0; the installed-tool Delifhery report recorded **0 changed, 0 failed**. NixOS apphost execution required setting `DOTNET_ROOT`/`DOTNET_ROOT_X64` to the active .NET 11 installation; unsetting them is sufficient for SDK-driven `dotnet` commands but not for a standalone apphost on this host.

## Supplied Delifhery repository

- Repository: `~/Repos/delifhery-web-bb-Xoechz`, revision `c4c442abeda23b92ea0625b5a675892a67683cda`.
- User reset tracked changes before the final test; existing untracked editor/Aspire files were retained.
- Target: `backend/Delifhery.Backend.slnx`, 16 projects, net10.0. Existing dependency restore was refreshed explicitly. Baseline full build succeeded with preexisting warnings; cached audit warnings initially prevented strict workspace loading (Issue 13).
- First post-format rebuild exposed the Razor-generated binding/import defect. That attempt was rolled back using recorded source bytes and hash checks, without resetting unrelated files.
- Corrected full-capability **project-mode** run: **148 physical files, 128 changed, 0 failed**; elapsed **81.676 seconds** including load, compilation validation, transformations and writes.
- Subsequent `--check --diff`: exit **0**, **0 diff characters**, **49.342 seconds**.
- Post-format `dotnet build backend/Delifhery.Backend.slnx --no-restore -nr:false`: **0 errors**, 5 existing compiler/analyzer warnings. No services or containers were started.
- Five physical files reported CSORG5005 because their workspace compilation lacked complete generated bindings; required imports were retained. Seven unsupported/error syntax organization scopes and unbreakable/protected list overflows were diagnosed conservatively. These skips are visible rather than silently treated as completed cleanup.
- Manual diff review: `AddressCore.cs` retained XML docs, `[Required]`, property and method bodies while canonicalizing regions. `CustomerController.cs` retained route/authorization attributes and initializer-backed dependency field while moving it into the field region and wrapping long signatures. `OPA/Program.cs` has no final diff and retains `using OPA.Components`.
- Final formatted changes remain in the test repository for inspection. Source snapshots and first/second JSON reports are under `/tmp/opencode/delifhery-review-fixed`. No commits were created.

## External open-source syntax smoke

- Repository: `https://github.com/serilog/serilog`, revision `43105b35095337d77f022561a8f2d3db2f9a7937`, disposable clone.
- Full syntax pipeline: **217 files analyzed, 185 changed, 0 failed**, exit 0; **94.685 seconds**. This run overlapped other exploratory verification, so its timing is not an isolated benchmark.
- Second `--check --diff`: exit 0, **0 diff characters**, **33.736 seconds**.
- Diagnostics included intentionally skipped semantic unused-import cleanup, directive/unsupported organization scopes and unresolved overflow. Syntax-only processing does **not** establish project compiler equivalence or an external baseline build.
- Manual diff review: `Events/LogEvent.cs` preserved constructor parameter order, chained initializer argument order, XML documentation and bodies while wrapping signatures/initializers. `Debugging/SelfLog.cs` preserved conditional/directive-protected outer scopes and logging expressions; an independently eligible nested type received a canonical Methods region and wrapped signature. Closing parentheses stayed on final-item lines.
- A separate Humanizer exploration timed out and is not counted as a successful run (Issue 14).

## Reproducible synthetic solution throughput

The opt-in verification executable generates a fixed 10-project dependency chain with **300 source files, 300 types and 9,000 methods**, plus fields, initializer-backed referenced-project objects and properties. Methods begin in reverse name order. Restore runs explicitly before the timed formatter invocation.

```sh
dotnet run --project tools/ForMatthias.Verification -c Release -- synthetic /tmp/opencode/formatthias-throughput-review1
```

Recorded single-run results:

| Measurement | Result |
| --- | --- |
| First project-aware full pipeline, validation and writes | **32.096 s** |
| Changed / failed files | **300 / 0** |
| Approximate first-run file throughput | **9.35 files/s** |
| Second project-aware check/diff, including reload/validation | **18.110 s** |
| Second-run exit / diff | **0 / 0 characters** |
| Organizer diagnostics | **0** |

These are observational end-to-end measurements on the provided machine, not a performance promise or a comparative microbenchmark. The corpus has many ordinary methods and does not represent every expensive nested-wrap/source-generator case. JSON reports remain with the generated disposable solution. The harness refuses an existing synthetic output directory, making the generated input repeatable.
