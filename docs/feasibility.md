# M0 public API feasibility

Verified on Linux with the installed .NET 11 SDK:

- All Roslyn packages: **5.9.0**, Microsoft.Build.Locator **1.11.2**.
- `Formatter.FormatAsync(Document, ..., CancellationToken)` formats an AdhocWorkspace document. Roslyn preserves fitting single-line blocks by default.
- `MSBuildLocator.RegisterDefaults()` runs before workspace creation, with a non-inlined creation boundary. MSBuild.Framework 17.11.48 is compile-only, never copied as an application runtime dependency.
- `MSBuildWorkspace.OpenSolutionAsync` loads this four-project **SLNX** solution on Linux, including its references and documents.
- `CSharpCompilation.GetDiagnostics()` exposes **CS8019**, the compiler's semantic unnecessary-import diagnostic, including hidden diagnostics. This is a stable public API alternative to internal IDE0005 code-fix services. Cleanup intersects diagnosed using spans across all applicable contexts and validates the resulting compilations. Documentation mode includes XML documentation binding. No token-occurrence heuristic is used.

Libraries are consolidated into Core (contracts, configuration, transformations) and Workspaces (IO, loading, semantic validation), with CLI depending on these layers. Tests are consolidated into one project with unit, golden, invariant, and integration suites.
