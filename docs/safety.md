# Safety model

## Boundaries

Member ordering is not universally behavior-neutral: reflection iteration order, serializers, source generators, metadata consumers and external tooling can observe it. The tool protects known execution/layout hazards and validates compiler diagnostics; it cannot prove equivalence for arbitrary external observers.

Organization is local to one physical type part. Multi-variable declarations are indivisible. Static and instance field/event/auto-property initialization sequences each retain their original order. Struct and potentially layout-sensitive instance declarations retain relative order. Attributed interfaces retain all member order conservatively, including attribute aliases. Enums and primary-constructor parameter order are never rearranged.

Project mode resolves the combined partial-type symbol and unions layout/interop constraints across every applicable project/TFM context. An attribute on another partial declaration therefore protects this part too. Candidate validation independently enforces those same semantic constraints. Syntax-only partial classes conservatively pin instance layout declarations, and syntax-only partial interfaces pin all direct members because attributes on unseen parts cannot be ruled out.

Parser errors, unsupported member syntax, ambiguous standalone comments, unbalanced/cross-scope regions and context-bearing directives protect the affected organization scope. Directive protection includes body-local pragmas that could affect subsequent declarations. Other types remain independently eligible. This implementation chooses conservative whole-type skips over speculative directive-segment movement.

Existing balanced member-level regions are replaced only in accepted scopes. Method/accessor regions and whole-type enclosing regions survive. XML docs, attributes, leading comments and trailing comments remain associated with their declarations. Generated regions can repeat and include singleton groups.

## Semantic workspaces

Project evaluation and explicit restore can execute MSBuild logic. Process only trusted projects. Syntax mode never invokes MSBuild. Load errors are fatal by default; `--on-load-failure syntax` is explicit degradation with diagnostics and skipped semantic cleanup. `--fail-on-skipped` makes that incomplete work nonzero.

The loader registers MSBuildLocator before workspace creation, verifies loaded documents/references and captures workspace diagnostics. Multi-targeted projects get explicit per-TFM inner loads; linked physical files map to every context and receive one canonical candidate. Conditional organization scopes are protected across parse contexts. `--framework` narrows validation and reports omitted TFMs. Missing requested frameworks fail.

Compiler `CS8019` is the public semantic basis for unnecessary using removal. A using must be unnecessary in all applicable contexts and carry no protected comments/directives. XML documentation binding is enabled. Alias/static/global imports are never removed from token-name guesses. Imports stay in their original scopes.

Any compiler error in an applicable context disables unused-import deletion for that physical file (`CSORG5005`). This includes incomplete Razor/source-generator binding: a missing generated type can otherwise make its required import look unnecessary. Other capabilities and baseline-error-aware compilation validation remain available; skipped cleanup is visible in file reports and `--fail-on-skipped`.

Validation includes compiler diagnostics for loaded projects and their project-reference graph. Complete source-generator execution, analyzer/code-fix execution and full MSBuild build validation are not claimed. Compiler error baselines may persist; new errors block the whole candidate set. New warnings are reported and can be made blocking. Diagnostic identity includes context, invariant message and stable physical declaration-part identity instead of source line numbers. Type diagnostics use the namespace-qualified header/part identity, excluding the reorderable member body.

## Runtime integrity gates

1. Reparse before and after in every relevant parse context; reject new syntax errors.
2. Verify ordinary token ledger, literal token text and enum order; map direct declarations and their comments/attributes to namespace-qualified containing scopes and physical partial-part identities. Preserve top-level statement order, per-token compiler-directive contexts, disabled source, and unmanaged/body-local region boundaries. For accepted organization scopes explicitly verify each canonical generated region's label and membership against the configured contiguous rule runs.
3. Independently verify initializer and layout/interop order.
4. Verify import changes against the approved semantic cleanup stage; only safe duplicate deletion is additionally permitted.
5. Execute the enabled transformation pipeline a second time and require byte-identical output.
6. Apply all candidates in memory to all linked/dependent compilation contexts and compare diagnostics.

Formatting happens before wrapping. List breakpoints are tracked between tokens rather than saving entire list nodes, allowing Roslyn spacing and indentation throughout interiors. After wrapping, normalization is restricted to affected member spans, retains those breakpoints, and re-evaluates overflow when indentation changes. Both formatting convergence and full-pipeline second-run equality are gated. Unresolvable atomic-expression overflow is diagnosed, never split arbitrarily.

## Writes

Snapshots retain source bytes, BOM/encoding, SHA-256, timestamp and permissions. Check/diff run validation but skip commits. Before writing, verify source hashes and refuse changed/read-only files. Prepare a same-directory temporary file, flush it, retain Unix permissions, recheck the source and replace it. Temporary files are removed on cancellation/failure. Symlink aliases resolve to physical identities; recursive scans avoid link escapes/cycles.

This is best-effort **per-file** replacement, not a repository-wide transaction or a filesystem lock against a malicious concurrent writer. A very small final hash-check/rename race remains. Failures identify successfully committed files in JSON. A failing candidate/compilation validation prevents the commit phase; a later IO error stops remaining commits. Ctrl+C returns 130.
