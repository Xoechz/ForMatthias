# Review 2 — fixes and verification

Date: 2026-11-13. Environment: Linux, .NET SDK 11.0.100-rc.3.26522.148.

## Findings addressed

| Finding | Resolution |
| --- | --- |
| 1. Report alias overwrites source during check/diff | `ReportWriter` rejects leaf symlinks (including dangling links), normalizes parent aliases, checks protected physical inputs, captures the intended destination, and revalidates immediately before replacement. A temporary file replaces the report directory entry rather than opening an existing linked file for writing. |
| 2. Indentation assumes one tab per level | `EditorSettings` now creates indentation from desired visual columns. Organization, member spacing and wrapping use whole-depth/continuation columns, including cases requiring spaces after tabs or indentation smaller than a tab. |
| 3. MIT/GPL metadata conflict | README and NuGet now consistently declare GPL-3.0-only, with author/copyright and repository URL metadata. The user explicitly confirmed retaining GPLv3. |
| 4. Altered GPL text | The current `LICENSE` was already corrected when implementing the fixes. Compared it byte-for-byte with the FSF's official GPLv3 text; no further edit was needed. |
| 5. Notices/source-access omissions | Package includes the GPL text, consolidated MIT notices/attributions, original Roslyn/.NET notices, and `SOURCE.md` with rebuilding instructions and version-matched upstream source links. |

The initial source-ZIP/custom dependency-inventory approach was removed at the
user's request. There is no added packaging project, per-binary inventory, or
source archive in the resulting package. Standard packing collects upstream
notice files from the resolved NuGet cache. Project source is provided through
the matching published repository revision, not a new distribution system.

## Verification

```sh
env -u DOTNET_ROOT -u DOTNET_ROOT_X64 dotnet test ForMatthias.slnx --no-restore -c Release
env -u DOTNET_ROOT -u DOTNET_ROOT_X64 dotnet pack ForMatthias.Cli --no-restore -c Release -o /tmp/opencode/review2-fixed
```

- **112 tests passed, 0 failed, 0 skipped**, including twelve Review 2 regression
  cases and existing whole-repository compilation/idempotency dogfood tests.
- The report regressions introduce links both before execution and during the
  processing callback, and verify unchanged source bytes in check/diff modes.
- Indentation cases use indent/tab widths **8/4, 4/8 and 4/3**, including nested
  type indentation, space remainders and continued argument lists. The transform
  test helper also checks structural integrity and second-run equality.
- Packing succeeds. Archive inspection confirms `GPL-3.0-only`, the repository
  URL, `LICENSE`, `SOURCE.md`, `THIRD-PARTY-NOTICES.md`, and **12 original upstream
  notice files** under `licenses/`. No `SOURCE.zip`, `DEPENDENCIES.json`, or
  `DEPENDENCY-NOTICES.zip` is present.
- A fresh isolated tool-path installation from that package succeeded, using a
  separate NuGet cache under `/tmp/opencode/review2-fixed/cache`.
- Running the installed managed CLI payload against
  `tests/Repositories/Smoke/Smoke.slnx --check --diff` returned **0 with no diff**.
  It was invoked with the active `dotnet` host to avoid this environment's
  standalone-apphost runtime-resolution mismatch.
- The GPL text matches `https://www.gnu.org/licenses/gpl-3.0.txt` byte-for-byte.
- All nine dependency source-archive URLs in `SOURCE.md` returned HTTP **200**
  after redirects during verification. This establishes current reachability,
  not a guarantee of future hosting or an independent upstream rebuild.

## Remaining release requirements

Stable-SDK verification and cross-platform runner results remain outstanding
under Issue 1. The installed SDK is still RC3; the fixes do not change that fact.

No commit, source publication or package publication was performed. Before
distributing these binaries publicly, publish their exact matching source and
ensure the package repository commit identifies it; the current working-tree
changes are not represented by the preexisting Git commit. Keep source directions
next to the download and maintain the required source access as described in
`SOURCE.md`. Recheck NuGet package-name availability at publication.
