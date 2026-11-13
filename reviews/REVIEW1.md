# Review 1 — Implementation against CODEX_DESIGN.md

Date: 2026-11-12

## Verdict

**The implementation is not complete against the design, even excluding cross-platform test execution.** Most planned functionality is implemented and the existing 78 tests pass, but review identified four reproducible defects and an incomplete structural-validation gate.

## Findings

### 1. High — Layout safety fails across partial declarations

**Location:** `src/ForMatthias.Core/MemberOrganizer.cs:43–46`  
**Requirement:** Design §7.2 requires semantic resolution of layout/interop attributes in project mode.

Layout protection only checks attributes on the current syntax declaration. It does not inspect the combined partial-type symbol.

Reproduction:

- One file declares `[StructLayout(LayoutKind.Sequential)] public partial class Packet`.
- Another part declares fields in order `Z`, then `A`.
- Run project mode with `--only organize --diff` after restoring the project.

**Observed:** The candidate reorders the fields to `A`, then `Z` and passes validation. This changes native layout without introducing a compiler error.

The syntax-only attribute check also leaves a corresponding gap for COM attributes declared on another interface part; that variant was not separately reproduced.

**Required fix:** Resolve safety attributes across all partial declarations and applicable project contexts. Enforce the resulting constraints in both ordering and candidate validation, with regression coverage.

### 2. Medium — Existing compiler warnings are incorrectly reported as new

**Location:** `src/ForMatthias.Workspaces/ProjectSession.cs:145–150`  
**Requirement:** Design §9.2 requires diagnostic identity to survive member movement.

Diagnostic identity hashes every token in the containing declaration. For a diagnostic attached to a type, reorganizing its members changes that hash.

**Observed:** The partial-class reproduction above reported an existing `CS1591` warning as new. Adding `--fail-on-new-warnings` rejected the candidate despite no genuinely new warning.

**Required fix:** Use stable declaration/origin identities that do not depend on member ordering. Add regression tests for type-level diagnostics and baseline warnings.

### 3. Medium — Method-call and delegate-invocation wrapping switches are not independent

**Location:** `src/ForMatthias.Core/ListWrapper.cs:82`  
**Requirement:** Design §3.4 specifies configurable wrapping targets.

Every invocation is enabled by `methodCalls OR delegateInvocations`.

**Observed:** With `methodCalls: false`, `delegateInvocations: true`, and `maxLineLength: 40`, an ordinary method call still wrapped under `--only wrap --diff`.

**Required fix:** Distinguish invocation kinds where semantic context is available. Explicitly handle syntax-only ambiguity rather than silently ignoring a disabled target.

### 4. Medium — Full formatting skips interiors of existing multiline lists

**Location:** `src/ForMatthias.Core/TransformationPipeline.cs:82–115`  
**Requirement:** Design §8.1 requires full formatting to apply Roslyn spacing and indentation; preserving existing wrapping means preserving breakpoints.

The implementation saves entire multiline argument/parameter lists and restores them after formatting. This preserves all interior whitespace, not merely existing line breaks.

**Observed:** Running `--only format --diff` on a multiline call with inconsistent argument indentation and the expression `a+  b` produced no diff.

**Required fix:** Preserve existing list breakpoints while still applying standard spacing and appropriate indentation within the list. Add exact-output regression coverage.

### 5. Medium — Structural validation does not enforce all required ownership boundaries

**Location:** `src/ForMatthias.Core/CandidateValidator.cs:19–32,44–64`  
**Requirement:** Design §9.1 requires declaration mapping, ownership, directive scoping, and generated-region validation.

Code inspection found:

- Token and declaration/comment checks are largely global multisets.
- Ordinary members are not mapped back to their containing type.
- Type identities omit namespaces.
- There is no explicit validation that generated regions enclose the correct configured rule runs.

For example, moving an otherwise unchanged method between two classes would not be rejected by the current per-type sequence checks when no initializer/layout sequence is affected. This is a missing safety-gate guarantee, not an observed organizer operation. No separate executable reproduction was run for this finding.

**Required fix:** Validate declaration ownership per stable containing scope, include namespaces and partial-part identity in type mapping, and explicitly validate generated-region membership and directive scoping. Add negative tests that deliberately submit invalid candidates to the validator.

## Substantially implemented functionality

The repository contains implementations and tests for:

- Configuration inheritance and schema validation.
- CLI selection, check/diff, reports, and exit policies.
- Constrained member ordering and canonical regions.
- Wrapping and import cleanup.
- Project/solution loading, multi-TFM contexts, and linked files.
- Compilation checks, guarded writes, packaging, and documentation.

Documented whole-type directive skips and rejection of optional unsupported wrapping targets are permitted conservative behavior under the design.

## Verification performed

```sh
env -u DOTNET_ROOT dotnet test ForMatthias.slnx --no-restore -c Release
```

**Result:** 78 passed, 0 failed, 0 skipped, using the installed .NET 11 RC3 SDK on Linux.

The initial test attempt encountered a runtime-resolution mismatch because the environment's `DOTNET_ROOT` pointed to a different runtime. Unsetting that variable allowed execution with the active SDK.

Additional CLI reproductions for findings 1–4 used disposable fixtures under `/tmp/opencode`, with diff-only source processing. The project fixture was explicitly restored. Repository implementation files were not changed during review.

## Remaining acceptance evidence

No recorded evidence was found for the design's external-repository smoke review (§10.1) or synthetic-large-solution throughput measurement (§10.4). Existing dogfood tests exercise this repository. Absence of recorded evidence does not establish that no such work occurred outside the repository.

Cross-platform test execution is excluded from this review's completion verdict, as requested.

## Recommended priority

1. Fix partial-type layout/interop safety before considering release readiness.
2. Correct diagnostic identity, wrapping-target selection, and multiline formatting behavior.
3. Complete structural-validation guarantees and add regression tests for all findings.
4. Record the remaining smoke-review and throughput evidence.

The design's definition of done should remain incomplete until these gaps are addressed.
