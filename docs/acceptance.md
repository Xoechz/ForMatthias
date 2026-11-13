# Acceptance coverage

Tests use design IDs in method names. Multiple related IDs share focused fixtures; exact-output golden cases live under `tests/Fixtures`.

| Milestone | Evidence |
| --- | --- |
| M0 | `ApiTests`, `WorkspaceSpikeTests`: public formatting, CS8019, Linux project/SLNX read; exact package/API note |
| M1 | `ConfigurationTests`, `CliTests`: CFG-01..04, WS-01/07, CLI-01/02, inheritance/exclusions, read-only diff/check, snapshots |
| M2 | `OrganizationTests`: ORG-01..13, SAFE-05, seeded 100-case topological permutation/initializer invariant |
| M3 | `OrganizationTests`, organization/directive golden fixtures: REG-01..07, TRV-01..03 |
| M4 | `WrappingTests`, `FormattingTests`, `HardeningTests`, wrapping golden fixtures: WRAP-01..08, FMT-01..04, UTF-8/BOM/UTF-16/CRLF real writes |
| M5 | `WorkspaceIntegrationTests`, `WorkspaceSpikeTests`: WS-02..08 (SLNX supported), IMP-01..04, SAFE-01..03, multiple TFMs and linked files |
| M6 | `CliTests`, `HardeningTests`, `DogfoodTests`: SAFE-04/06, CLI-03/04, import/token/enum gates, cancellation/report overwrite protection, whole-solution second-run equality |
| M7 | README/configuration/safety/development docs, changelog, tool packaging and installed tool smoke; three-platform CI workflow |

Supported conservatively: directive scopes and ambiguous comments produce diagnostic skips rather than speculative movement. Unbreakable/unsafe wraps produce CSORG4001. Attribute/indexer/tuple wrapping enablement is rejected rather than ignored. No full-build/source-generator equivalence claim is made.

Local Linux verification and package smoke are recorded during implementation. Windows/macOS installation, stable-SDK verification and externally hosted CI results require their corresponding runners; defining a workflow is not evidence that it has run.

Current local Release suite after Review 1: **100 passed, 0 failed, 0 skipped** (initial suite: 78). Review regressions cover combined partial symbols, baseline warning origins, invocation targets including inconsistent TFMs, multiline formatting, structural ownership and incomplete generated bindings. See [review verification](review1-verification.md) for recorded external/project/throughput evidence and installed-package checks. `.gitattributes` fixes fixture checkout line endings for cross-platform golden comparisons.
