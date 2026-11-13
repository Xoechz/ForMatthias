# Changelog

## 1.0.0 — Unpublished

- .NET 11 dotnet-tool with syntax and semantic project/SLN/SLNX backends.
- Hierarchical schema-validated organizer JSON and per-file EditorConfig.
- Configurable first-match rules, deterministic overload signatures and constrained topological ordering.
- Initializer/layout/interop protection and conservative directive/comment scopes.
- Canonical singleton/repeated member regions with documentation/comment ownership gates.
- Roslyn formatting, custom member blank lines, threshold-triggered list wrapping and same-line closing parentheses.
- Scoped imports and compiler-proven unused imports intersected across contexts/TFMs.
- Read-only check/diff, feature selection, diagnostics/JSON reports, guarded writes and cancellation.
- Golden/invariant/integration/dogfood coverage and Linux/Windows/macOS CI configuration.

Known constraints and verification status are tracked in `ISSUES.md` and `docs/safety.md`.

### Review 1 corrections

- Resolve layout/interop protection from combined partial symbols across project contexts, and enforce the constraints independently during candidate validation.
- Stabilize type-level diagnostic origins across member reordering.
- Distinguish method/delegate invocations semantically; diagnose ambiguous syntax-only target selection.
- Format multiline-list interiors while preserving token breakpoints and convergence.
- Enforce declaration ownership by namespace/type/physical part, per-token directive context, body-region boundaries and configured generated-region membership.
- Preserve imports when compiler contexts have errors or missing generated bindings; regression found by rebuilding the supplied Delifhery solution.
- Add review regression coverage and a reproducible external/project/throughput verification executable.
