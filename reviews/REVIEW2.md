# Review 2 — Design completion and licensing

Date: 2026-11-13  
Reviewed revision: `29e5576be373f4e91aa56442c12b8d86678227b8`  
Baseline: `CODEX_DESIGN.md` v1.0. Cross-platform test execution is excluded from the completion verdict, as requested.

**Follow-up:** The findings below describe the reviewed revision. Subsequent
fixes, the agreed simplified GPLv3 distribution approach, and verification
results are recorded in [docs/review2-verification.md](../docs/review2-verification.md).

## Verdict

**No: the implementation is substantially complete, but not everything is done, even excluding cross-platform testing.** The five Review 1 findings have corresponding fixes and regression coverage, and a fresh Release test run passed **100 tests, 0 failed, 0 skipped**. This review nevertheless reproduced a source-overwrite defect in read-only mode and an EditorConfig indentation defect.

**Licensing is not release-ready as packaged.** GPLv3 is a viable project license, but the repository and package contradict each other, the GPL text has been altered, and the package omits license/third-party notices and clear corresponding-source directions. These are concrete packaging/documentation findings, not a determination that the project's license grant is legally void. Repository inspection cannot establish ownership or the provenance of every contribution.

## Findings

### 1. High — A report symlink can overwrite source during `--check`

**Locations:** `ForMatthias.Cli/Application.cs:53–57,189–196`; compare physical-path normalization in `ForMatthias.Workspaces/SourceSnapshot.cs:37–51`.  
**Requirements:** Design §§0.2, 4.3 and 9.4: check/diff must never write source, and physical aliases must be handled safely.

The report checks use its filename extension and `Path.GetFullPath`, while selected source paths use symlink-aware `PhysicalPaths.Normalize`. A report named `report.json` that links to `Input.cs` passes both checks. `File.WriteAllTextAsync` then follows that link and replaces the C# source with the JSON report, bypassing source validation and guarded commit.

**Reproduced on Linux in a disposable directory:**

1. Create `Input.cs` containing `class C {}` and a final newline.
2. Create `report.json` as a symlink to `Input.cs`.
3. Run `formatthias Input.cs --check --only format --report report.json`.

**Observed:** `Input.cs` contained the JSON report afterward. The report itself recorded `committed: false` and `summary.exitCode: 1`. This is source destruction during a documented read-only operation, not just incorrect reporting. The same report-writing path serves `--diff`.

**Required fix:** Protect report destinations using physical identity, including linked parent directories, before processing and immediately before report output. Prefer a report-writing strategy that does not follow an existing destination symlink. Add a regression asserting that aliased source bytes remain unchanged in check/diff modes.

### 2. Medium — Custom indentation equates one indentation level with one tab

**Locations:** `ForMatthias.Core/EditorSettings.cs:51–55`; consumers include `MemberOrganizer.cs:54,81` and `ListWrapper.cs:43–44`.  
**Requirements:** Design §§3.5, 8.1 and 8.2; FMT-01 requires EditorConfig indentation to be respected.

`EditorSettings.Indent` returns exactly one tab whenever `indent_style = tab`, regardless of `indent_size` and `tab_width`. These are independent settings: an eight-column indent with four-column tabs requires two tabs.

**Reproduced configuration:**

```ini
root = true

[*.cs]
indent_style = tab
indent_size = 8
tab_width = 4
```

Input: a class whose `Z` and `A` methods each start with two tabs. Running `--only organize --diff` sorts the methods and generates regions, but gives each method and region directive **one tab**, reducing the configured eight-column indentation to four columns. The candidate passes validation.

**Required fix:** Generate indentation from desired visual columns and tab stops, using spaces for any remainder. Cover unequal indent/tab widths and nested scopes in organize-only and wrap-only regressions. The [EditorConfig specification](https://spec.editorconfig.org/#indentation-non-normative) explicitly illustrates these combinations.

### 3. High — NuGet advertises MIT while the project declares GPLv3

**Locations:** `ForMatthias.Cli/ForMatthias.Cli.csproj:10`; `README.md:96–101`; root `LICENSE`.

The project sets `PackageLicenseExpression` to `MIT`, while the README identifies the project as GNU GPL v3.0. A **freshly packed** `dotnet-formatthias.1.0.0.nupkg` contains:

```xml
<license type="expression">MIT</license>
<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>
```

Its embedded README declares GPLv3. There is no documented intentional dual-license grant reconciling this conflict. Consumers and automated license scanners therefore receive incompatible statements from the same artifact.

**Required fix:** Confirm the copyright holder's intended grant and make README, package metadata and release materials consistent. If the intended grant is GPL version 3 only, use `GPL-3.0-only`; use `GPL-3.0-or-later` only if that additional permission is intended and explicitly granted. Rebuild and inspect the resulting package. Merely changing the metadata to MIT would not resolve ownership or permission questions.

### 4. Medium — `LICENSE` is not a verbatim GPLv3 copy

**Locations:** `LICENSE:154,179,195,208,245,343,407,435,446,471,540,552,563,589,600,612`.

The official GPL has consecutively numbered sections 0 through 17. In this file, sections **2 through 17 are all numbered `1.`**, although internal references still refer to sections 4, 5, 6, 10, 15 and 16. This is an extensionless plain-text file, so Markdown automatic list numbering does not repair it.

Compared against the [FSF's official GPLv3 text](https://www.gnu.org/licenses/gpl-3.0.txt) during review. The license document itself permits verbatim redistribution and says changing it is not allowed.

**Required fix:** Replace the license document with the unmodified official text. Keep project-specific copyright and license-grant notices separate from that text. The generic “or any later version” application example at the end of the GPL is a template, not by itself a project-specific grant of that option.

### 5. High for distribution — Required notices and source-access information are missing from the package

**Locations:** `ForMatthias.Cli/ForMatthias.Cli.csproj:12–15`; freshly generated package archive and `.nuspec`.

The package bundles Roslyn, Humanizer, MSBuild Locator, Microsoft extension/composition libraries and build-host dependencies. Archive inspection found **no LICENSE, COPYING or third-party notice files**. Only the README is explicitly packed as project documentation, and its relative `LICENSE` link has no target in the archive.

Concrete dependency evidence:

- `Humanizer.Core` **2.14.1** is MIT-licensed. Its [version-specific license](https://github.com/Humanizr/Humanizer/blob/v2.14.1/LICENSE) requires preservation of its copyright and permission notice and includes Inflector/ByteSize attributions. `Humanizer.dll` is bundled without that notice file.
- `Microsoft.CodeAnalysis.Common` **5.9.0** declares MIT in its restored `.nuspec` and ships `ThirdPartyNotices.rtf` in its upstream package. That notice file is absent from this tool package.

MIT dependencies are not inherently incompatible with a GPLv3 application. The issue is retaining their applicable notices when redistributing their binaries; labeling the whole package MIT or GPL does not replace those notices. This was a targeted dependency check, not a complete transitive-license/provenance audit.

For GPL binary distribution, the package also supplies neither corresponding source nor clear instructions for obtaining the matching source. Its repository metadata contains a commit hash but **no repository URL**. GPLv3 §6(d) permits source on a different server, but requires clear directions alongside the object-code download and continued availability. A source archive inside every `.nupkg` is not mandatory if a compliant external distribution arrangement is provided.

**Required fix before publication:** Include the unmodified project license and applicable dependency license/copyright/notice texts; inventory the actual bundled runtime and build-host payload; provide version-matched corresponding-source access and build instructions with the release/download. Include required dependency source or access arrangements as applicable. The repository says the package has not been published, so this is a release-readiness finding, not an assertion that a public distribution violation has already occurred.

## Design coverage and Review 1 follow-up

| Area | Assessment |
| --- | --- |
| M0: .NET 11, public Roslyn/MSBuild APIs, SLNX, semantic-cleanup spike | Implemented; feasibility note and API/workspace tests present. Local verification still uses the installed RC3 SDK. |
| M1: JSON hierarchy/schema, CLI, discovery, snapshots, reports | Substantially implemented. Finding 1 prevents signing off on read-only/report safety. |
| M2: classification, custom rules, signature sort, initializer/layout constraints | Implemented with tests. Review 1 partial-type safety fix resolves combined symbols and unions context constraints. |
| M3: canonical regions, repeated runs, trivia protection | Implemented with tests. Expanded structural validation checks containing scopes, directive contexts and generated region runs. |
| M4: formatting, wrapping, existing multiline preservation | Implemented with tests, but Finding 2 remains. Review 1 invocation-target and multiline-interior fixes are present. |
| M5: all-TFM/linked-file handling, strict loading, semantic imports, compiler gate | Implemented with integration coverage. Baseline type-warning identities were corrected; incomplete-binding import removal is explicitly skipped. |
| M6: structural/fixed-point gates, guarded source commits, cancellation, deterministic reports | Substantially implemented and tested; the report writer bypass in Finding 1 violates a non-negotiable invariant. |
| M7: docs, packaging, installation, dogfooding and throughput | Implementation and recorded evidence exist. Licensing/distribution findings 3–5 remain, as does stable-SDK verification. |

All five original Review 1 findings have implementation changes and negative/behavioral regression coverage in `ReviewRegressionTests.cs`. The current test run includes those regressions. This is stronger evidence than an issue being marked resolved, but does not prove correctness for every possible input.

The previously missing smoke/throughput evidence is now recorded in `docs/review1-verification.md`:

- Delifhery: 16-project solution, 148 files, 128 changed, successful post-format build and zero second-run diff.
- Serilog: 217-file syntax smoke, representative manual diff review and zero second-run diff.
- Synthetic dependency-chain solution: 10 projects, 300 files, 9,000 methods, first/second-run measurements and zero second-run diff.
- Fresh isolated installed-tool checks were recorded for the prior review fixes.

Those external runs are **existing recorded evidence**, not runs repeated for Review 2. Whole-type directive skips, syntax-only semantic-cleanup skips, and explicit rejection of optional unsupported wrapping targets are documented conservative behaviors permitted by the design; they are not treated here as missing features.

`ISSUES.md:3–7` still records stable .NET 11 SDK verification as outstanding. This is distinct from the excluded Windows/macOS testing. Package-name availability should also be rechecked at publication, as already documented; the prior check is not a permanent reservation.

## Verification performed for this review

```sh
env -u DOTNET_ROOT -u DOTNET_ROOT_X64 dotnet test ForMatthias.slnx --no-restore -c Release
env -u DOTNET_ROOT -u DOTNET_ROOT_X64 dotnet pack ForMatthias.Cli --no-restore -c Release -o /tmp/opencode/review2/package
```

- Release tests: **100 passed, 0 failed, 0 skipped**, approximately 1 minute 41 seconds of test execution.
- Package build: successful. Inspected the freshly generated `.nuspec` and complete archive entry list with `unzip`.
- Environment: Linux, .NET SDK `11.0.100-rc.3.26522.148`; preview-SDK informational messages remain.
- Findings 1 and 2 were reproduced using the rebuilt CLI DLL and disposable inputs under `/tmp/opencode/review2/`.
- Reviewed the design, implementation paths, existing tests/acceptance records, root license, package metadata, selected upstream dependency metadata and official license/specification sources.
- This review changes only `reviews/REVIEW2.md`; it does not implement the fixes.

## Licensing references and limits

- [Official GPLv3 text](https://www.gnu.org/licenses/gpl-3.0.txt): §§1, 4–6 and 14, plus verbatim-copy notice.
- [Humanizer 2.14.1 license](https://github.com/Humanizr/Humanizer/blob/v2.14.1/LICENSE): MIT terms and third-party attributions.
- [NuGet license packaging documentation](https://learn.microsoft.com/nuget/reference/msbuild-targets#packing-a-license-expression-or-a-license-file): package expressions/files and explicit inclusion.

Using this formatter does **not by itself** make a user's formatted C# files GPL-licensed: GPLv3 §2 only covers output when its content constitutes a covered work. “CodeMaid-inspired” is also not evidence of copied CodeMaid code. A definitive legal/provenance opinion would require facts outside this repository, including contribution rights and any reused code.

## Completion recommendation

1. Fix the report alias overwrite first and add read-only regression coverage.
2. Correct custom indentation and verify unequal tab/indent widths.
3. Resolve the license grant consistently, restore the official GPL text, and package the necessary notices/source-access information.
4. Run the outstanding stable-SDK verification and recheck the final release artifact.

Until these are addressed, keep the design's definition of done and release readiness open, even with cross-platform testing excluded.
