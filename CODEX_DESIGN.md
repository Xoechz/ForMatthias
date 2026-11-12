# ForMatthias C# Organizer — Implementation Design for Codex

**Status:** Approved requirements; implementation specification  
**Version:** 1.0 (design baseline)  
**Prepared:** 2026-11-12  
**Intended audience:** Codex implementation agent, maintainers, and reviewers  
**Working product name:** `formatthias`
**Primary deliverable:** Cross-platform .NET CLI package `dotnet-formatthias`

> **Implementation instruction:** Build the product described here incrementally, with tests for each milestone. Do not interpret a formatting preference as permission to change program semantics. When an invariant cannot be established, emit a diagnostic and skip the affected reorganization scope. Do not substitute broad refactorings for the specified functionality.

## 0. Executive summary

Build a CodeMaid-inspired, **integrated C# organizer and formatter**, based on Roslyn, that can operate on `.cs` files, directories, `.csproj` projects, and `.sln`/supported `.slnx` solutions. It will:

1. Reorder members using user-defined, ordered matching rules and configurable sort keys.
2. Preserve the relative execution order of field and auto-property initializers and other recognized declaration-order constraints.
3. Sort method/constructor overloads by a deterministic parameter-signature key.
4. Remove existing member-level organizational `#region` blocks and regenerate canonical regions, even for one member; permit repeated region names for noncontiguous groups.
5. Apply Roslyn/.editorconfig whitespace formatting and custom blank-line conventions.
6. Wrap overlong argument/parameter lists at a configurable line length, placing the closing `)` on the **same line as the final argument or parameter**.
7. Organize usings and perform semantically justified unused-using cleanup in project-aware mode.
8. Provide `--check`, `--diff`, diagnostics, targeted features, hybrid workspace selection, and safe file writes.
9. Remain deterministic, idempotent, conservatively safe, and suitable for CI.

**Not in v1:** renaming, broad refactoring/code-style rewrites, reflow of comment/XML-doc text, fluent-chain wrapping, arbitrary expression wrapping, IDE integration, moving members between files, or rearranging enum members.

### 0.1 Source of truth and decision status

The decisions below were **explicitly confirmed** during planning:

| Area | Binding requirement |
| --- | --- |
| Overall product | Integrated formatter: Roslyn formatting + custom organization + wrapping + regions + safe cleanup |
| Member organization | Fully configurable member ordering with sensible defaults |
| Initialization | Preserve the relative order of initializer execution while sorting |
| Overloads | Sort same-name overloads by parameter signature |
| Existing member-level regions | Replace with canonical configured regions |
| Region generation | Automatic after reorganization; min. **1** member per region |
| Discontinuous member groups | Generate multiple regions with the same canonical name |
| Config hierarchy | Nested directory JSON configurations with inheritance |
| Arrays of organization rules | A child configuration **replaces** the entire inherited array |
| Wrapping | Only initiate a new wrap when the configured maximum line length is exceeded |
| Wrapped closing parenthesis | Same line as the last argument/parameter |
| Workspace | Hybrid automatic selection |
| Project-load failure | Strict failure; **no silent syntax-only fallback** |
| Unsafe reorganization | Skip affected type, continue independent safe scopes |

Other defaults specified below are implementation choices consistent with the plan. Treat them as part of this design baseline unless a real implementation blocker is discovered; document any necessary deviation.

### 0.2 Non-negotiable invariants

- **Syntactic integrity:** No new syntax errors, malformed directives, or unbalanced generated regions.
- **Structural integrity:** Except for intended ordering, allowed import deletion, and managed-region replacement, preserve declarations, tokens, attributes, comments, and user code.
- **Execution-order integrity:** Preserve each original ordered initialization sequence within a partial type part; never reorder fields of recognized layout-sensitive types.
- **Project integrity:** In project-aware mode, introduce no compilation errors in any relevant project/TFM context.
- **Idempotency:** `T(T(input, config), config) == T(input, config)` byte for byte for supported files after encoding and newline normalization.
- **Determinism:** Identical inputs, configurations, target-framework contexts, and tool version produce identical output and diagnostics.
- **No silent degradation:** If semantic context was requested but cannot load, fail (unless fallback explicitly selected).
- **Write safety:** Do not overwrite a file changed since the tool read it. Never write an invalid candidate.

### 0.3 Support baseline

- Implement the tool on **.NET 11** (`net11.0`) as a cross-platform `dotnet tool` (Windows, Linux, macOS).
- Support C# language versions handled by the pinned Roslyn release; detect and skip unsupported syntax safely.
- Pin one compatible **stable** set of `Microsoft.CodeAnalysis.*`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.Build.Locator`, and related packages in central package management. Record exact selected versions in the repository when scaffolding; do not independently mix Roslyn package versions.
- Use `System.CommandLine` only if its selected stable API is suitable; otherwise implement the small CLI with a maintained argument parser. No implementation should depend on unpinned prerelease behavior.
- Ensure the CLI works on Linux without Visual Studio; project mode requires a suitable installed .NET SDK/MSBuild environment.

## 1. Scope and terminology

### 1.1 Modes and capabilities

The user-facing capabilities are `organize`, `format`, `wrap`, and `cleanup`.

| Capability | Included transformations | Needs semantic context? |
| --- | --- | --- |
| `organize` | Member rules, constrained ordering, overload sorting, canonical member-level regions | Some safety restrictions; syntax subset available |
| `format` | Roslyn whitespace/braces/spacing plus custom blank lines | No, except accurate project options if available |
| `wrap` | Threshold-triggered argument/parameter list formatting | No |
| `cleanup` | Sort/deduplicate safe imports, remove unused imports with verified semantic data, trailing whitespace | Unused import removal: yes |

Running without `--only` invokes all enabled capabilities. `--only organize` must not invoke unrelated full-document formatting; it may format changed spans and generated region boundaries. `--only wrap` must not reorganize members or remove usings. `--only cleanup` must not perform general refactorings.

**Format policy:** The default full command is permitted to reformat the selected full files. Selective commands minimize unrelated diffs.

### 1.2 Reorganization units

- **Physical file:** Unique normalized filesystem path; write at most once per invocation, even if referenced by multiple projects.
- **Type part:** One syntax declaration of a class, struct, record, interface, or nested type in one physical file. Partial type parts are independently organized; never merge or redistribute members between parts.
- **Member:** An indivisible Roslyn declaration node at direct type-member depth (a multiple-variable field declaration remains one unit).
- **Group:** First matching configured member rule (or the unmatched fallback).
- **Contiguous run:** Adjacent declarations in the final order with the same logical grouping rule; region generation acts on runs, not globally grouped sets.
- **Safety boundary:** A sequence/block across which declarations must not be moved due to directives, unsupported syntax, ambiguous trivia, layout, or other hazards.

## 2. Proposed repository structure

```text
/
├── README.md
├── LICENSE
├── .editorconfig
├── Directory.Packages.props
├── global.json                    # SDK selection if chosen/pinned
├── src/
│   ├── ForMatthias.Cli/            # executable, CLI, reports, exit status
│   ├── ForMatthias.Core/           # config-independent transformation plan contracts
│   ├── ForMatthias.Configuration/  # JSON, hierarchy, options, editorconfig bridge
│   ├── ForMatthias.Roslyn/         # syntax transforms, format/wrap/imports, constraints
│   └── ForMatthias.Workspaces/     # syntax backend and MSBuild project backend
├── tests/
│   ├── ForMatthias.UnitTests/
│   ├── ForMatthias.GoldenTests/
│   ├── ForMatthias.IntegrationTests/
│   ├── Fixtures/                  # source + expected + config fixtures
│   └── Repositories/              # small actual projects/solutions
└── docs/
    ├── configuration.md
    ├── safety.md
    └── development.md
```

**Pragmatic allowance:** Consolidate the library projects if dependency overhead outweighs benefit; preserve the logical boundaries/interfaces. Never make Core depend on CLI, terminal output, or a specific filesystem.

### 2.1 Core contracts (illustrative C#)

```csharp
public interface ISourceProvider
{
    Task<SourceSnapshot> ReadAsync(PhysicalFileId file, CancellationToken ct);
}

public interface IWorkspaceLoader
{
    Task<WorkspaceLoadResult> LoadAsync(InputSelection selection, WorkspaceOptions options, CancellationToken ct);
}

public interface IConfigurationResolver
{
    EffectiveConfig Resolve(string physicalPath, CliOverrides overrides);
}

public interface ITransformationStage
{
    string Id { get; }
    Task<StageResult> ExecuteAsync(TransformationContext context, CancellationToken ct);
}

public interface ICandidateValidator
{
    Task<ValidationResult> ValidateAsync(WorkspaceSnapshot before,
        WorkspaceSnapshot candidate, CancellationToken ct);
}

public interface IChangeCommitter
{
    Task<CommitResult> CommitAsync(IReadOnlyList<ValidatedFileChange> changes, CancellationToken ct);
}
```

Define immutable value objects for `SourceSnapshot` (path, raw bytes, encoding, BOM, newline policy, content hash), `DocumentContext` (parse options, applicable project/TFM contexts), `MemberDescriptor`, `OrderingConstraint`, `CandidateChange`, and `Diagnostic`.

### 2.2 Dependency direction

`Cli → Workspaces/Configuration/Roslyn → Core contracts`. Tests reference each library independently. Workspace loading, filesystem IO, reporting, and transformations should be injectable for deterministic tests.

### 2.3 Supported Roslyn APIs

Prefer documented, public APIs (subject to exact signatures of the pinned version): `CSharpSyntaxTree`, `SyntaxFactory`, `SyntaxNode` replacement and annotations, `Document`, `AdhocWorkspace`, `MSBuildWorkspace`, `Formatter.FormatAsync`, and `Formatter.OrganizeImportsAsync`. Use `Microsoft.Build.Locator` before loading MSBuild APIs. Avoid reflection into internal Roslyn services and avoid depending on editor-only Visual Studio components.

Unused-using removal should be implemented through stable, public Roslyn analyzer/code-fix or semantic-analysis support verified for the selected version; do **not** depend on inaccessible internal services. See §8.4 for a gated implementation strategy.

## 3. Configuration specification

### 3.1 Files and precedence

1. Resolve `.editorconfig` with the standard EditorConfig path hierarchy/section matching for built-in Roslyn settings; do not shadow standard settings with duplicate custom ones.
2. Read `.csharp-organizer.json` from ancestor directories, from outermost to the closest configuration for each physical file. Optional `root: true` stops search above that file.
3. Merge **objects recursively**; override scalar keys in the child; **replace arrays** supplied in the child, with the single exception of `exclusions`, which is additive. An explicitly supplied empty array replaces an inherited array with empty content.
4. `--config <path>` applies that organizer JSON as the **explicit complete organizer configuration** to all selected files, with built-in defaults underneath it and no directory-specific organizer configurations layered on top; it does not disable per-file `.editorconfig`.
5. Explicit CLI option overrides win over JSON options. A custom wrapping `maxLineLength` wins over EditorConfig's `max_line_length`, then the built-in default of 120.
6. Unknown JSON properties and unsupported schema versions are errors by default; reject the config before modifying files. Diagnostics include filename and JSON path.
7. Resolve inherited configuration **per physical file**, not per project or TFM. An identical shared file must have one effective organizer configuration.

**Example resolution:** `repo/.csharp-organizer.json` + `repo/src/.csharp-organizer.json` + `repo/src/module/.csharp-organizer.json` combine scalars and objects; if the module specifies `organization.rules`, its array fully replaces the inherited `rules` array.

### 3.2 Canonical v1 JSON schema

A machine-readable Draft 2020-12 JSON Schema is supplied beside this document as `config.schema.json`; a full default-style example is in `sample-config.json`. The fields below are normative. The adjacent schema must be kept consistent with this contract.

| Path | Type | Default | Meaning |
| --- | --- | --- | --- |
| `schemaVersion` | integer | 1 | Required format version |
| `root` | bool | false | Stop organizer-config parent lookup |
| `organization.enabled` | bool | true | Enable type-member organization |
| `organization.rules` | ordered array | built-in rules | First matching rule wins; order = group priority |
| `organization.sortBy` | ordered sort-key array | `accessibility,static,name,signature` | Default within-group sort |
| `organization.unmatched.behavior` | `preserve` | `preserve` | Unmatched members retained in original relative order |
| `organization.unmatched.region` | string | `Other Members` | Region name for unmatched contiguous runs |
| `regions.enabled` | bool | true | Enable managed member-level regions |
| `regions.mode` | `regenerate` | `regenerate` | Remove old managed regions and rebuild |
| `regions.minMembers` | integer >= 1 | 1 | Region for every nonempty contiguous matched run |
| `regions.emitForUnmatched` | bool | true | Wrap unmatched runs |
| `wrapping.enabled` | bool | true | Enable custom wrapping |
| `wrapping.maxLineLength` | integer >= 40 or null | null | Null = EditorConfig `max_line_length`, else 120 |
| `wrapping.strategy` | `when_exceeding_limit` | same | Do not initiate wrapping on fitting lines |
| `wrapping.argumentLayout` | `one_per_line` | same | Canonical layout for newly wrapped lists |
| `wrapping.continuationIndentLevels` | integer >= 1 | 1 | Continuation indent relative to statement indentation |
| `wrapping.closingParenthesis` | `same_line` | same | `lastArg);`, not its own line |
| `wrapping.preserveExistingMultiline` | bool | true | Do not collapse existing multiline constructs |
| `wrapping.targets` | bool object | see sample | Per-syntax-kind switches |
| `format.enabled` | bool | true | Roslyn spacing/braces/indentation |
| `format.blankLines` | counts object | see sample | Custom member/group blank-line rules |
| `cleanup.enabled` | bool | true | Import and trailing-whitespace cleanup |
| `cleanup.organizeUsings` | bool | true | Organize imports in their existing scopes |
| `cleanup.removeDuplicateUsings` | bool | true | Only when provably redundant |
| `cleanup.removeUnusedUsings` | bool | true | Project-aware only; no syntax guesswork |
| `cleanup.trimTrailingWhitespace` | bool | true | Outside raw string contents |
| `exclusions` | glob array | built-in generated/output filters | Additive across parents |

Rule object:

```json
{
  "id": "public-static-methods",
  "match": {
    "kind": ["method"],
    "accessibility": ["public"],
    "modifiersAll": ["static"],
    "containingTypeKinds": ["class", "record"],
    "nameGlob": "*Async",
    "explicitInterface": false
  },
  "sortBy": ["name", "signature"],
  "region": "Public Static Async Methods"
}
```

- All specified matching predicates are **AND**-combined. Array values inside `kind`, `accessibility`, and `containingTypeKinds` are **OR**; empty match arrays are invalid (omit predicates instead).
- `modifiersAll` requires every listed modifier, `modifiersAny` requires at least one listed modifier, and `modifiersNone` excludes every listed modifier.
- `nameGlob` supports `*` and `?`, using ordinal, case-sensitive matching; no regex in v1. Matching applies to the simple member name, not generic arguments or parameter types.
- `explicitInterface` refers to explicit interface implementations only. The qualified interface identity is included in the member's sort key for such members.
- `match` omitted means match every supported member kind; **every rule needs a nonempty unique `id`** and a nonempty canonical `region` name without newlines or directive text.
- `sortBy` omitted uses the global array; `sortBy: []` means stable original order within the group. Allowed keys: `accessibility`, `static`, `name`, `signature`, `original`.
- Rule IDs are stable keys used for diagnostics and group identity; a child config replaces the *whole* rules array instead of patching by ID.
- `static` distinguishes static and instance. Modifier ordering beyond static is not configured in v1; matcher supports modifier predicates.
- Accessibility ranking (default): `public`, `protected_internal`, `protected`, `internal`, `private_protected`, `private`. Normalize contextual/effective accessibility per syntax rules; do not invent `public` for a default-private class member.

**Supported member kinds:** `field`, `event_field`, `event_declaration`, `constructor`, `static_constructor`, `property`, `indexer`, `method`, `operator`, `conversion_operator`, `nested_class`, `nested_struct`, `nested_record`, `nested_interface`, `nested_enum`, `delegate`, `finalizer`. Unsupported future declarations are unmoved/diagnosed rather than dropped. Enum **members** are not sortable member kinds.

**Type-kind matching:** `class`, `struct`, `record`, `record_struct`, `interface` (normalize record class to `record`), with nested classes/structs etc. matching their own type kinds.

### 3.3 Built-in rule order

| Rank | Rule ID | Match | Canonical region |
| --- | --- | --- | --- |
| 1 | constants | const field | Constants |
| 2 | static-fields | static field not matched above | Static Fields |
| 3 | instance-fields | any remaining field | Fields |
| 4 | static-constructors | static constructor | Static Constructors |
| 5 | constructors | instance constructor | Constructors |
| 6 | events | event field or explicit event declaration | Events |
| 7 | properties | property | Properties |
| 8 | indexers | indexer | Indexers |
| 9 | methods | regular method | Methods |
| 10 | operators | operator or conversion operator | Operators |
| 11 | nested-types | nested class/struct/record/interface/enum/delegate | Nested Types |
| 12 | finalizers | finalizer | Finalizers |

The previous planning discussions grouped properties and indexers at similar priority; the explicit built-in defaults split them into two adjacent canonical groups. Both can be merged through configuration. The user's ordering overrides take priority over these defaults.

### 3.4 Wrapping targets (v1 defaults)

Enabled: method calls, method declarations, constructor creation calls, constructor declarations, `this(...)`/`base(...)` initializers, local function declarations, delegate invocations, record primary constructor declarations, and parenthesized lambda parameter lists.

Disabled by default: attribute arguments, indexers, tuple expressions, generic type argument lists, collection expressions, fluent-chain breaks, and arbitrary subexpression wrapping. Disabled targets can be exposed as booleans where practical; targets not implemented in v1 must be rejected or diagnosed as unsupported rather than silently ignored.

`wrapping.maxLineLength` counts visual columns starting at 1. Expand tabs using EditorConfig `tab_width`; count other Unicode scalar values as one column for deterministic cross-platform behavior (this is a **formatter metric**, not an assertion about terminal glyph widths). Only physical lines outside raw string contents are candidates.

### 3.5 Default blank-line policy

| Context | Blank lines |
| --- | --- |
| Between consecutive fields of same run | 0 |
| Between consecutive methods, constructors, or properties | 1 |
| Between different generated region blocks | 1 |
| After opening type brace / before closing type brace | 0 |
| Maximum consecutive blank lines in managed spans | 1 |

Don't normalize blank lines inside comments, raw strings, disabled syntax, or unrelated method bodies in `--only organize` mode. Respect `.editorconfig` for indentation and newline preferences; the custom blank-line settings govern only places not already defined by standard options.

## 4. CLI contract

### 4.1 Command grammar

```text
dotnet formatthias [PATH] [options]

PATH: .cs | directory | .csproj | .sln | .slnx (when verified supported)
Default PATH: current directory

--workspace auto|project|syntax         (default: auto)
--framework <TFM>                     (optional; otherwise all applicable TFMs)
--on-load-failure fail|syntax          (default: fail; explicit fallback only)
--restore                             (default: false; explicit trusted MSBuild restore)
--config <path>                       (explicit organizer config)
--check                               (read-only; nonzero if changes required)
--diff                                (read-only; unified diff to stdout)
--only organize,format,wrap,cleanup    (comma-separated subset; default all)
--include <glob>                      (repeatable)
--exclude <glob>                      (repeatable)
--include-generated                   (opt-in; exclusions still respected if explicit)
--report <path>                       (JSON diagnostic/change report)
--fail-on-skipped                     (skipped work causes nonzero exit)
--fail-on-new-warnings                (project validation stricter than default)
--verbose                             (human-readable progress)
--version
--help
```

Examples:

```bash
dotnet tool install --global dotnet-formatthias

dotnet formatthias .
dotnet formatthias ./MySolution.sln --check
dotnet formatthias ./src/App.csproj --diff
dotnet formatthias ./Feature.cs --workspace syntax --only organize,wrap
dotnet formatthias ./src --workspace project --framework net10.0
dotnet formatthias . --report ./artifacts/formatthias-report.json
```

For a repository-local tool, support a standard `.config/dotnet-tools.json` manifest and `dotnet tool run formatthias ...` usage. The tool command name and NuGet package ID must be checked for collisions before publication; renaming is allowed with documentation updates.

### 4.2 Workspace auto-selection

| Input | Resolution |
| --- | --- |
| Explicit `.cs` file | Syntax-only, even inside an otherwise project-backed folder; `--workspace project` opts in |
| `.csproj` | Project-aware |
| `.sln` | Project-aware |
| `.slnx` | Project-aware if public API version supports it; otherwise clear unsupported-format error |
| Directory with exactly one solution | Project-aware via solution |
| Directory with no solution but exactly one project | Project-aware via project |
| Directory with no project or solution | Syntax-only recursive file scan |
| Directory with multiple candidate solutions or projects | Error; request explicit path (do not guess) |

`--workspace syntax` never invokes MSBuild. `--workspace project` on a single file must locate an unambiguous containing project and load its full relevant context; otherwise fail. `--framework` is only valid in project-aware mode.

When `--on-load-failure syntax` is explicitly used, preserve a diagnostic that semantic-dependent capabilities were skipped and report the degraded operation; with `--fail-on-skipped` it is nonzero. **Default remains hard failure**.

### 4.3 Exit codes

| Exit code | Meaning |
| --- | --- |
| 0 | Success; in `--check`, no changes required; no fatal skip policy triggered |
| 1 | `--check` detected changes, or `--fail-on-skipped` detected incomplete work |
| 2 | CLI/configuration error |
| 3 | Workspace discovery/evaluation/load/restore error |
| 4 | Candidate transformation/validation error |
| 5 | File access/commit conflict or IO error |
| 130 | User interruption (convention; platform permitting) |

If errors differ across files, exit with the highest-severity category by fixed priority: `2,3,4,5,1` (within the same invocation, configuration validation takes place before any source transformation). Report every error, not just the chosen exit status.

`--check` and `--diff` **never write source files**, regardless of other options. If both are specified, emit diff and return check status. `--report` writes only its requested report file, not source.

### 4.4 Diagnostics/report shape

Use stable IDs such as `CSORG1001` (invalid config), `CSORG2001` (unsupported syntax), `CSORG2002` (unsafe directive), `CSORG2003` (layout-sensitive type), `CSORG3001` (initializer constraint prevents preferred sort), `CSORG4001` (wrapping cannot fit), `CSORG5001` (workspace load failure), `CSORG6001` (new compiler diagnostic), and `CSORG7001` (source changed during write).

JSON report should have `schemaVersion`, `toolVersion`, `mode`, `input`, `files[]`, and `summary`; each file has normalized path, status (`changed|unchanged|skipped|failed`), applied capabilities, skipped capabilities with reason and source span, emitted region count, member count, wraps, diagnostics, and a before/after hash where applicable. Use stable ordering by normalized path and source span. Avoid leaking full source text into reports.

## 5. Workspace and file execution model

### 5.1 Syntax-only backend

For direct `.cs` inputs or projectless directories:

- Parse with a pinned, supported Roslyn parser; load each file as a `Document` in an `AdhocWorkspace` if a workspace-backed formatter is needed.
- Use per-file analyzer configuration and line-ending/indentation options.
- Do not fabricate references, global usings, preprocessor symbols, or a semantic model.
- Perform syntax-safe organization, region regeneration, formatting, wrapping, and import organization.
- Never remove a using based on token occurrence heuristics.
- Be conservative around attributes that may imply interop or metadata ordering; see §7.
- Do not claim compiler-equivalence checks were performed.

For direct files with `#if` whose active branch depends on unknown compiler symbols, treat the conditional block as an opaque protected boundary. Do not move members into/out of it.

### 5.2 Project-aware backend

- Call `Microsoft.Build.Locator` **before any MSBuild assemblies are loaded**; use an initialization boundary that avoids accidental early assembly binding.
- Load project/solution via a compatible `MSBuildWorkspace`. Capture its diagnostics and **fail** if requested projects/TFMs are missing or not reliably loaded; verify returned projects and documents rather than trusting a non-throwing open call.
- Respect solution/project reference graph, `.editorconfig`, SDK and `global.json`, project `LangVersion`, nullable context, global usings, conditional symbols, and metadata references.
- No implicit `dotnet restore`. If restore is necessary, explain and fail, or execute it only with `--restore` in a trusted project.
- Treat project evaluation/build targets as potentially executable; document trust requirement and never attempt project-aware evaluation in a sandbox-untrusted mode.
- Record project evaluation configuration (Debug/Release, Platform, TFMs). v1 uses the environment's normal Configuration/Platform, not an unbounded Cartesian product of all configurations.

### 5.3 Target frameworks and linked documents

A multi-targeted SDK-style project has separate **inner-build** evaluations, one per relevant target framework. Do not assume one `MSBuildWorkspace` open implicitly gives all framework contexts. Enumerate/evaluate the desired TFMs explicitly (e.g. outer-build property discovery followed by per-TFM project loads using MSBuild global `TargetFramework` properties), and verify this against supported SDK fixture projects.

Algorithm:

1. Discover relevant physical `.cs` file paths for selected project(s)/solution.
2. Build `FileContextMap[PhysicalFileId] → [ProjectId, TFM, ParseOptions, CompilationOptions, References, DocumentId]` including linked files and dependent projects.
3. Resolve effective organizer config once per physical file.
4. Generate **one canonical candidate text** per physical file; do not repeatedly apply different formatters for each TFM.
5. Restrict transformations to syntax safely common to all relevant parse contexts. If `#if` makes an affected type structurally different across contexts, conservatively skip its reorganization (or restrict to independently proven-safe identical segments), with a diagnostic.
6. Apply each candidate's source text to every corresponding Roslyn `Document` in the in-memory solution context(s).
7. Validate compiler diagnostics in every affected compilation context, including dependent projects, before any writes.
8. Write the physical file once.

`--framework <TFM>` narrows validation to a selected TFM if supported by the project. Emit a note that omitted TFMs were not validated. Default is **all relevant TFMs**. A missing or invalid TFM must fail, not silently select another.

A source file shared between unrelated projects with incompatible parse contexts must not be rewritten if a unified safe candidate cannot be established. The desired output is always derived from the **physical** file, never from an arbitrary project order.

### 5.4 Physical file identity and generated source

- Normalize to absolute paths; reconcile symlink aliases to one physical identity where supported. Never silently follow links outside the selected root when recursively scanning; report or skip safely.
- Treat filenames under `bin/` and `obj/`, `*.g.cs`, `*.generated.cs`, `*.Designer.cs` (by default), analyzer-generated documents, and files marked with a recognized auto-generated header as excluded. Allow explicit inclusion with `--include-generated`, while respecting explicit user excludes.
- For partial classes, reorganization is local to each declaration; do not shuffle code between files.
- Avoid writing read-only or otherwise externally owned files.

### 5.5 Failure and fallback

- In `auto` mode, failure to load the selected project/solution returns exit 3; no automatic syntax fallback.
- Only `--on-load-failure syntax` enables fallback. In fallback, disable semantic-dependent transformations and log every skipped requirement with a diagnostic.
- In project-aware mode, a compilation or workspace context that becomes incomplete after load must fail semantic validation rather than being treated as an empty/valid project.
- A failure in one type should not prevent analysis and changes to other types; a file-level failed integrity gate rejects the entire candidate file.
- A repo-level candidate validation failure rejects the **dependent validation set**; do not write a portion of mutually dependent changed projects that has not been validated together.

## 6. Transformation pipeline

### 6.1 Ordering and stage contracts

```text
Inputs + CLI options
  → file discovery and exclusions
  → workspace/context load (all relevant TFMs)
  → source snapshots + effective JSON/.editorconfig
  → baseline syntax and compilation diagnostics
  → import sorting/deduplication (safe scopes)
  → member analysis and ordering constraints
  → member reorder and signature sorting
  → replace managed type-member regions / generate canonical regions
  → Roslyn standard whitespace formatting
  → custom wrapping and custom blank-line rules
  → narrowly scoped final whitespace normalization
  → structural + idempotency + project validation
  → diff/check or guarded file commit + JSON/human report
```

Pipeline implementation notes:

- Each stage returns an immutable document/result, changed spans, and diagnostics; no stage writes directly.
- The member-ordering stage must not mutate source before a complete plan exists for the type.
- When a type fails an organization safety precondition, mark it skipped and preserve its original type-member order and member-level regions; independent formatting can still apply where parse-safe.
- `regions` are regenerated only for types whose organization plan is accepted (or for well-formed types where a deterministic no-movement organization plan is explicitly accepted).
- Do not run unrestricted Roslyn formatting **after** custom wrapping: the final pass is confined to untouched/annotated whitespace spans that cannot collapse the intended multiline layout. Test this interaction using the pinned Roslyn version.
- If full formatter + custom stages do not reach a fixed point on second execution, reject the candidate and report an idempotency error rather than writing oscillating output.

### 6.2 Member descriptor and classification

Build a `MemberDescriptor` for each direct member of an eligible type:

```text
MemberDescriptor
  originalIndex
  node / stable source span / original full text
  memberKind
  simpleName / normalized signature
  declared and effective accessibility
  isStatic / modifier set
  explicitInterfaceIdentity
  hasInitializer (field, event-field, auto-property)
  isLayoutSensitive
  matchedRuleId / canonicalRegionName
  attachedLeadingTrivia / attachedTrailingTrivia
  activeDirectiveContext / ambiguity flags
```

**Important:** Multiple variable declarations like `private int x = F(), y = G();` are **one node** with a combined initializer sequence. Do not split them to satisfy a sort key. A field-like event with an initializer must be included in the appropriate static/instance initialization sequence.

Type kinds: ordinary class, struct, record class/struct, interface, nested counterparts. Do not reorganize enum members, parameters of records, or top-level statements as type members. Preserve the primary constructor header rather than manufacturing a moveable constructor member.

### 6.3 Matching rules and stable sort keys

For each member:

1. Evaluate `organization.rules` in array order. The first fully matching rule assigns the group rank and region label.
2. If none matches, place in the unmatched fallback group after all configured rules; keep unmatched members in original relative order.
3. Build a sort tuple within a group using `rule.sortBy ?? organization.sortBy`.
4. Use original source index as a mandatory final tie-breaker, irrespective of explicit sort keys.

**Signature comparator (for same-name methods and constructors):**

1. Group by member name (qualified explicit-interface identity included); for constructors, group by containing type.
2. Compare generic arity (ascending).
3. Compare parameter count (ascending).
4. Compare ordered parameter type syntax, using whitespace-normalized, ordinal comparison.
5. Compare parameter passing modifiers in a documented rank order: none, `in`, `ref`, `out`, `ref readonly`, then other supported modifiers as normalized token text. Include `params`/`scoped` as deterministic secondary tokens.
6. Finally original source index.

Use normalized source syntax for the ordering key in **both** syntax-only and project-aware mode to avoid cross-mode sort instability. Semantic models are still used for safety checks, but not to silently change sorting keys (type aliases and nullable spellings may therefore sort textually rather than by a resolved symbol identity).

`original` sort key ends other key comparison and preserves original order for matches up to that point. Implement case-sensitive ordinal name sorting by default; do not apply locale-dependent comparisons.

### 6.4 Constrained sorting algorithm

**Problem:** User order may conflict with field/auto-property/event initializer order or other constraints. Construct a set of valid topological orderings and choose one deterministically.

For every eligible reordering segment of one type part:

1. Let `members = [m0..mn-1]` in original source order.
2. Partition by protected directive/trivia boundaries. A protected block is an opaque anchor; nothing crosses it.
3. Identify all **static initializer-bearing** declarations in original order and add precedence edges between adjacent such declarations.
4. Identify all **instance initializer-bearing** declarations in original order and add precedence edges between adjacent such declarations.
5. For layout-sensitive types, add edges between adjacent **all** instance field declarations (not only initialized ones); where preserving member order is required (e.g. COM interface), mark entire member sequence as fixed.
6. Add any additional known ordering edges required by a supported construct. If unable to model a construct, skip the affected type's organization.
7. Compute the **lexicographically preferred topological ordering** with Kahn's algorithm and a priority queue: among currently unconstrained nodes, select the lowest group rank, then the configured sort tuple, then original index. Avoid nondeterministic dictionary iteration.
8. If constraints prevent perfect grouping, accept the valid result, record `CSORG3001`, and allow repeated region labels for separated contiguous runs.
9. Verify every precedence edge and the source-to-output member mapping before applying the rewriter.

Pseudocode:

```text
plan(members, effectiveRules, constraints):
    nodes = classify(members, effectiveRules)
    graph = addInitializerAndSafetyEdges(nodes, constraints)
    if graph.invalid or unsupportedHazard: return SKIP_TYPE(reason)
    ready = minPriorityQueue(compare(groupRank, sortTuple, originalIndex))
    for node where indegree == 0: ready.push(node)
    ordered = []
    while ready not empty:
        next = ready.pop()
        ordered.add(next)
        for successor in graph.successors[next]:
            indegree[successor]--
            if indegree[successor] == 0: ready.push(successor)
    if ordered.count != nodes.count: return SKIP_TYPE(cycle)
    assert(all constraints maintained)
    return ordered
```

**Subtlety:** Cross-boundary restrictions should normally be represented by partitioning a type into segments, not just precedence edges: a simple edge might still allow a member to cross the boundary in the other direction. Treat opaque protected spans as anchored barriers and sort only within each allowed segment.

**Example (initializer constraint defeats group priority):**

```csharp
class Example
{
    private int z = Log("first");
    public string Name { get; } = LogText();
    private int a = Log("last");
}
```

Even if the configured priority is fields before properties and field names alphabetically, the original `z → Name → a` initializer order must remain; resulting `Fields`, `Properties`, `Fields` regions are valid.

### 6.5 Region rebuilding algorithm

**Policy is unambiguous:** Existing **member-level organizational** regions are replaced by canonical, rule-derived regions; generation is automatic and `minMembers = 1` by default.

Algorithm per safely organized type:

1. Identify balanced `#region`/`#endregion` pairs through Roslyn directive trivia and their nesting/context. Do not use regex/text replacement.
2. Classify a region as replaceable only if its span is within the direct type-member organization domain and its removal does not alter or strand `#if`, `#pragma`, `#nullable`, `#line`, or other context-bearing directives.
3. Preserve regions inside methods, accessors, lambdas, or unrelated nested implementation bodies. Preserve outer regions enclosing entire types or unrelated declarations.
4. For replaceable member-level regions, remove directive trivia while preserving leading/trailing comments and all source tokens. Ambiguous custom comments that cannot be reassociated are anchored and trigger a skipped organization scope instead of deletion.
5. Sort member nodes with attached doc comments and attributes; retain literal comment text.
6. Traverse ordered nodes in physical output order and form maximal **contiguous** runs of identical rule IDs. Emit `#region <canonical-name>` before and `#endregion` after every run whose member count >= `minMembers` (including one-member runs).
7. For unmatched runs, honor `regions.emitForUnmatched`; default label `Other Members`.
8. When a constraint yields `Fields → Properties → Fields`, emit two distinct `#region Fields` blocks. Never forcibly reorder to make regions unique.
9. Normalize blank lines at region boundaries; verify balanced directives and a canonical second pass.

Region marker syntax example:

```csharp
#region Fields
private int first = Log("first");
#endregion

#region Properties
public string Name { get; } = LogText();
#endregion

#region Fields
private int second = Log("second");
#endregion
```

If a legacy region includes an unsafe interleaving of conditional directives, skip the affected type's region/member organization rather than flattening the directives destructively.

### 6.6 Trivia ownership rules

- XML documentation and attribute lists are part of a declaration and move with it.
- A contiguous leading comment block immediately associated with a member moves with it if no blank-line/section-marker ambiguity exists.
- Trailing single-line comments on a declaration's last line remain attached to the preceding declaration.
- Standalone section comments separated from declarations by blank lines, comments anchored to directives, and ambiguous banner comments become **non-crossable boundaries** or trigger a skip.
- A file header, namespace documentation, and `#nullable` header remain at their original scope.
- A moveable member's leading trivia must not steal indentation/newlines from an unrelated neighbor; reconstruct separators explicitly and use Roslyn formatter annotations/spans.
- Preserve comment text exactly in v1. Do not reflow XML docs, line comments, or block comments.

**Implementation test:** Compare a normalized multiset of comment tokens/text plus explicit ownership mapping before/after. Count-only comparison is insufficient because it could hide attachment changes.

## 7. Semantic safety boundaries

### 7.1 Initialization

- Preserve order of static field, static event-field, and static auto-property initializer-bearing declarations as one ordered sequence per type part.
- Preserve order of corresponding instance initializer-bearing declarations as a separate sequence per type part.
- Keep declarations with multiple field variables intact; internal declarator order cannot change.
- `const` fields have no runtime field initialization and can normally be moved, subject to other hazards.
- Never rearrange enum members, even those with explicit numeric values.
- Preserve primary-constructor parameter order and syntax; do not extract or merge them with ordinary constructors.

### 7.2 Layout-sensitive and interop types

- Prevent changing **relative instance field order** for structs (default sequential layout), types with `StructLayout(LayoutKind.Sequential)` or `Explicit`, `[FieldOffset]` fields, `[InlineArray]`, or other verified native-layout constraints.
- For COM interop interfaces where vtable declaration order is significant, preserve **all** direct member order.
- In project-aware mode, resolve relevant attributes/types semantically across aliases, fully-qualified names, and inherited metadata where applicable.
- In syntax-only mode, when a type has potentially relevant/unknown type-level attributes and attribute semantics cannot be established, conservatively pin its field order; conservatively pin interface order when interop attributes cannot be ruled out. Never allow a simple syntactic alias to bypass the safety gate.
- Preserve `fixed` buffers and field offset relations; do not reinterpret layout attributes.

### 7.3 Preprocessor and compiler context

- A source directive is not an organization region merely because it starts with `#`.
- Do not cross boundaries of `#if/#elif/#else/#endif`, `#nullable`, `#pragma`, or `#line` if doing so might change context or mapping.
- Treat conditional compilation blocks as opaque, anchored segments unless branch-aware transformations can be proven consistent across every loaded context.
- Do not move a source declaration across a `#nullable` or warning-pragma boundary; nullable annotations and warning suppression can change diagnostics.
- In syntax mode, unknown conditional symbols imply conservative protection. In project-aware mode, all relevant TFMs must agree on the transformation.
- If a type's region reconstruction and directive nesting interact ambiguously, **skip that type's organization**, not the entire source file.

### 7.4 Reflection and external observability

Compile-time equivalence does **not** imply universal behavioral equivalence. Metadata declaration order, reflection iteration order, source generators, tooling, serialization conventions, and external interop may observe original layout/order. Document this limitation prominently in the README. For known semantic hazards apply strict gates; for other arbitrary external observers no absolute guarantee is possible.

### 7.5 Unsupported syntax and broken source

- Do not generate a new type declaration when the original has parser errors overlapping the type or its relevant directives. Skip its organization; independently safe formatting outside the broken span may proceed.
- Unknown future C# syntax nodes are preserved as opaque declarations with a diagnostic.
- Do not attempt semantic cleanup if compilation context is incomplete.
- Never insert/remove modifiers, rewrite syntax constructs, convert expression bodies, or simplify expressions as side effects of organization.

## 8. Formatting, wrapping, and import cleanup

### 8.1 Standard Roslyn formatting

Use Roslyn's public document formatting APIs with each document's effective EditorConfig settings for indentation, tabs/spaces, brace placement, spacing, and newlines. Do not implement duplicate brace/spacing rules.

- Full execution (`all` or `--only format`): format selected documents as specified by EditorConfig, with documented changes throughout their formatting scope.
- `--only organize`: format only reordered declarations, changed separators, and new region directives.
- `--only wrap`: preserve non-targeted formatting; use EditorConfig only to determine indentation/line width preferences.
- `--only cleanup`: no unrelated brace/whitespace reformatting.
- Do not modify literal contents. For raw string literals, take particular care because indentation can be semantically significant.

### 8.2 Line-length wrapping algorithm

**Agreed user-facing behavior:** Add breaks only when a physical line containing an eligible argument or parameter list exceeds `maxLineLength`. Default threshold = 120 (unless EditorConfig supplies `max_line_length`). When newly wrapped, format **one argument/parameter per line**, indent by one level relative to the containing statement, and place the closing `)` on the **same line as the final argument/parameter**.

Example (assuming `maxLineLength` is low enough to trigger):

```csharp
// Before
var result = await client.ProcessAsync(customerId, customerName, config, cancellationToken);

// After
var result = await client.ProcessAsync(
    customerId,
    customerName,
    config,
    cancellationToken);
```

Method declaration:

```csharp
public Task<Result> ExecuteAsync(
    Customer customer,
    RequestOptions options,
    CancellationToken cancellationToken)
{
    // ...
}
```

Implementation:

1. Parse eligible `ArgumentListSyntax`/`ParameterListSyntax` (and corresponding constructor/initializer variants) with Roslyn, not text-pattern matching.
2. Compute effective visual line columns using current indentation/tab settings. Ignore text inside raw-string contents.
3. Process from outermost eligible overflowing expression/list to inner nested lists, repeating until no eligible overlong list can be improved. Use a fixed deterministic traversal order.
4. If a list fits, **do not introduce a new multiline layout**. If it is already multiline, leave its grouping and existing breakpoints intact unless further wrap is required to fix an overlong eligible line.
5. For a new wrap, place the first argument on a new line; place each subsequent argument on a new line; format `)` immediately after the last argument/parameter token. Preserve the syntax-required comma placements (do not invent trailing commas in invalid C# contexts).
6. Re-evaluate nested lists after outer formatting. For nested calls, wrap only remaining overflow. Avoid needless expansion of inner calls that now fit.
7. Preserve named argument order, `ref/out/in`, generic invocation syntax, comments, trailing comments, lambdas and raw strings. If comments make a canonical one-per-line rewrite unsafe, skip that list and report `CSORG4001`.
8. If one argument's atomic expression or a string literal cannot fit, leave it intact and report an unresolved overflow, without splitting arbitrary expressions.
9. Apply only final targeted whitespace correction so that the Roslyn formatter cannot collapse new breaks or move the closing `)` onto its own line.
10. Verify the second pass is byte-identical.

**Nested example:**

```csharp
var result = service.Process(
    CreateRequest(
        customerId,
        config,
        GetOptions(environment, retryCount)),
    cancellationToken);
```

This is a possible canonical result when both outer and `CreateRequest` lists overflow but `GetOptions` fits. Preserve existing multiline calls without unwrapping them.

**Edge cases:**

| Case | Behavior |
| --- | --- |
| Single long string/raw string argument | Do not split literal; unresolved-overflow diagnostic |
| Lambda block argument | Lambda remains one argument; do not rewrite its body |
| Named arguments / `ref`, `out`, `in` | Preserve syntax and order |
| Comments between arguments | Keep with intended argument or skip if ambiguous |
| Final `//` comment on argument | Preserve comment and keep syntax valid; skip if closing `)` placement conflicts |
| Nested eligible calls | Outer first, then nested overflows |
| `#if` inside argument list | Treat as protected; skip if unsafe |
| Attribute/indexer/tuple arguments | Off by default; enabled only if explicitly supported and tested |
| Fluent method chains | No new break at `.` in v1 |
| Single-line expression that fits | No new wrapping |
| Already multiline expression | Preserve existing breaks when possible |
| Line overflow caused by unrelated code | Do not wrap a short innocent call just because its enclosing statement is long unless doing so decreases that line's overflow |

### 8.3 Blank lines, newline and encoding

- Custom blank-line rules apply between direct type members and generated regions; never blindly rewrite every blank line in the file.
- Resolve `end_of_line` from EditorConfig if specified; otherwise preserve the file's dominant original newline convention. For mixed line endings without explicit preference, preserve untouched lines and normalize only generated lines to the dominant convention (document this explicitly).
- Preserve UTF-8 BOM, UTF-16 BOM, and encoding where reliably detected; if encoding cannot be preserved exactly, reject the write with a diagnostic rather than corrupt it.
- Preserve a final newline if present; follow EditorConfig `insert_final_newline` where set.
- Remove trailing whitespace only in syntactically safe text segments; never inside raw string literal data or other whitespace-significant text.

### 8.4 Import organization and unused imports

- For each existing import scope (`global using`, compilation-unit `using`, namespace-level `using`), use Roslyn's import organizer or equivalent supported API with EditorConfig preferences, keeping imports in their existing scope by default.
- Sort safely by established policy (including `System` grouping); preserve aliases, `using static`, global usings, and conditional import boundaries.
- Deduplicate imports **only where semantics and trivia permit it**; never delete a directive carrying comments or special context by string equality alone.
- Remove unused imports **only** in project-aware mode, using a **stable, public** diagnostic/code-fix or semantically validated implementation corresponding to the unnecessary-using rule (`IDE0005`). The stable API surface must be verified in a spike before relying on it.
- A using can be removed only if unnecessary in **all applicable project and TFM contexts** referencing the physical file. Compare binding/compilation diagnostics after removal.
- Syntax-only mode may sort imports but **must not** remove unused imports by guessing.
- Conditional imports are independently protected spans; do not move them outside `#if` branches.

**Implementation gate:** An early M0 spike must establish a maintainable public-API method for IDE0005-equivalent cleanup. If the first approach fails, investigate another public Roslyn analyzer/code-fix integration; do **not** substitute token-based guessing. If no safe implementation is feasible, flag the semantic cleanup milestone as **blocked** and request an explicit product-scope decision before calling v1 complete. A reduced preview may transparently disable the operation with an unsupported diagnostic, but that preview does not satisfy the v1 definition of done.

## 9. Validation and commit strategy

### 9.1 Gate A — Structural validation (always)

For each changed candidate:

1. Parse with **every relevant** C# parse context; no newly introduced syntax diagnostics.
2. Match declarations/tokens before and after, permitting only known intentional changes (imports removed after approval, member reorder, generated region trivia, allowed whitespace). Ensure no user code tokens are lost or duplicated.
3. Verify per-type initializer sequence ordering, per-type layout-sensitive relative field ordering, and COM interface member ordering constraints.
4. Verify directive balance and appropriate directive scoping (especially `#if/#nullable/#pragma/#line`).
5. Verify comment, XML-doc, and attribute preservation and ownership where movable.
6. Verify every generated region encloses a run with the correct matching rule and at least one member.
7. Run the full enabled pipeline on candidate source **again in memory** and require byte-for-byte identical result (including newline/encoding policy).
8. If a test fails, do not write that candidate file. Report the failing invariant and the scope involved.

The token-equivalence verifier must be **stage-aware**, since import removal changes the token stream intentionally and region directives are trivia rather than ordinary tokens. Maintain a ledger of explicitly permitted edits.

### 9.2 Gate B — Project compilation diagnostics (project-aware mode)

- Compute baseline compilation diagnostics for all relevant project/TFM contexts before transformations.
- Apply all candidate text to a consistent in-memory workspace snapshot across linked documents and dependent projects.
- Recompute compilation diagnostics using public Roslyn compilation APIs.
- No **new** compiler errors are allowed; existing baseline errors may persist.
- Do not compare only line numbers. Compare a multiset of `(project context, diagnostic ID, severity, normalized message, syntax/declaration identity)`; use source annotation/origin mapping to account for moved members. Unmatched new errors fail safe.
- Warnings are reported; `--fail-on-new-warnings` makes new warnings blocking.
- If a project baseline cannot be reliably computed because loading is incomplete, fail semantic validation.
- Analyzer/source-generator diagnostics can be treated separately from compiler diagnostics; document which are included in v1 and do not claim a full `dotnet build` was performed.

When a file fails validation, discard its candidate, recompute affected project diagnostic sets for the remaining candidates, and reject any dependent set that cannot be proven valid. Never commit a mixture of candidates validated against an earlier, different workspace state.

### 9.3 Gate C — Optional build validation (post-v1 extension)

A complete MSBuild build can execute custom build tasks and requires materializing candidate source in an isolated, faithful project tree. It is **not required for v1**. Do not expose a misleading `--verify-build` that simply compiles the original repository. A future implementation may stage a complete trusted workspace copy and run a controlled build before commit.

### 9.4 Write transaction

1. Capture file bytes, size, timestamp, permissions (where applicable), and a strong content hash upon read.
2. Prepare all changed documents in memory; validate the complete affected compilation set.
3. For each file, recheck the current content hash immediately before commit. If changed, mark conflict, do not overwrite it, and invalidate/revalidate affected pending candidates as necessary.
4. Write to a temporary file in the same filesystem/directory, preserving encoding/BOM and appropriate permissions. Flush, then perform a platform-appropriate replacement/move. This is best-effort per-file atomicity, **not repository-wide atomicity**.
5. Sort commit/report order for determinism. A write error returns a nonzero exit code and identifies files successfully committed versus not committed; do not falsely claim an all-or-nothing transaction.
6. Cancel gracefully on Ctrl+C; never leave a half-written target file.

Check/diff modes perform gates A/B but skip the commit stage entirely.

## 10. Testing strategy and acceptance matrix

### 10.1 Test layers

1. **Unit:** configuration path/precedence, matching, grouping, signature comparer, ordering graph, comment ownership, format-width calculation, diagnostics mapper.
2. **Golden:** `input.cs`, `expected.cs`, `config.json` (and optional `.editorconfig`) per case. Compare exact text and second-run equality.
3. **Invariant/property-based:** randomized member orders with precedence edges; assert output is a permutation, preserves required edges, and is idempotent; randomized comments/directives with safe generators.
4. **Integration:** project and solution fixtures, multi-targeting, linked files, compiler diagnostics, CLI modes, encoding, filesystem race conditions.
5. **Smoke:** run against representative external/open-source C# repositories in disposable copies, measure behavior, collect manual diff reviews, and ensure baseline builds when feasible.

### 10.2 Mandatory named acceptance tests

| ID | Fixture | Expected result |
| --- | --- | --- |
| ORG-01 | Mixed methods/properties/fields | Group order and accessibility/name sorting match configured rules |
| ORG-02 | User-defined group before fields | First matching rule and custom priority honored |
| ORG-03 | Two same-name overloads | Correct generic arity/parameter-count/type/modifier signature order |
| ORG-04 | Overloads with aliases and generic type syntax | Stable textual normalized signature order across modes |
| ORG-05 | Equal sort keys | Original order retained |
| ORG-06 | Multiple declarators in one field | Declaration remains indivisible |
| ORG-07 | Static and instance initializers interspersed with properties | Two respective initializer sequences preserved |
| ORG-08 | Initializers conflict with group order | Valid constrained result plus repeated canonical region names |
| ORG-09 | Enum with implicit values | Enum member order unchanged |
| ORG-10 | Partial type in two files | Each file organized independently; no members migrate |
| ORG-11 | Sequential-layout struct | Relative instance field order unchanged |
| ORG-12 | COM interop interface | Original member order preserved |
| ORG-13 | Explicit-interface methods | Grouping/sorting includes interface qualification |
| REG-01 | Old incorrect member-level regions | Removed; canonical regions inserted |
| REG-02 | One-member group | Region generated |
| REG-03 | Members split by initializer constraints | Duplicate `#region Fields` accepted |
| REG-04 | Nested organizational regions | Flattened and regenerated when safe |
| REG-05 | Region inside method body | Unchanged |
| REG-06 | Region surrounding entire type | Preserved |
| REG-07 | Region intersects conditional directives | Affected type skipped and diagnosed; no directive damage |
| TRV-01 | XML docs + attribute + method | All move as one declaration, text unchanged |
| TRV-02 | Trailing `//` comment | Remains attached to original declaration |
| TRV-03 | Standalone section header | Anchored; no unsafe reassociation |
| WRAP-01 | Line exactly at threshold | No new wrap |
| WRAP-02 | Line one column above threshold | Eligible list wrapped, one item/line |
| WRAP-03 | Wrapped call | Final argument followed by `);` on same line |
| WRAP-04 | Wrapped declaration | Final parameter followed by `)` same line before body/braces |
| WRAP-05 | Nested calls | Only required nested calls wrapped; second pass unchanged |
| WRAP-06 | Already multiline, under threshold | Existing breaks retained |
| WRAP-07 | Raw string/lambda/commented argument | Unchanged text and valid syntax, or skip with reason |
| WRAP-08 | One unbreakable long argument | Unresolved overflow diagnostic, no invalid rewrite |
| FMT-01 | EditorConfig tabs/brace rules | Roslyn settings honored |
| FMT-02 | Different directory configs | Per-file effective wrapping/organization honored |
| FMT-03 | UTF-8 BOM, UTF-16, CRLF | Encoding/BOM and newline policy preserved |
| FMT-04 | Raw string whitespace | Contents unchanged |
| IMP-01 | Usings in various scopes | Organized without movement between scopes |
| IMP-02 | Unused imports across two TFMs | Delete only when unused in both |
| IMP-03 | Syntax-only unused imports | Never removed; skipped capability reported |
| IMP-04 | Alias/static/global usings and comments | No unsafe deduplication/removal |
| WS-01 | Direct `.cs` path | Syntax backend; no MSBuild required |
| WS-02 | Single `.csproj` | Project-aware; full relevant context loaded |
| WS-03 | Multi-targeted project | All expected inner-build contexts validated |
| WS-04 | Linked file used by multiple projects | Exactly one physical candidate/write |
| WS-05 | Failed project load | Exit 3; no source modifications or silent fallback |
| WS-06 | Explicit `--on-load-failure syntax` | Degraded mode and missing semantic capability reported |
| WS-07 | Two solutions in directory | Explicit selection required |
| WS-08 | Unsupported `.slnx` on pinned version | Clear error, never silent omission |
| SAFE-01 | Baseline compilation errors | No *new* errors; preexisting ones can remain |
| SAFE-02 | Candidate introduces new compiler error | Reject candidate, no source write |
| SAFE-03 | `#nullable` / `#pragma` scope change risk | Skip affected reorganization |
| SAFE-04 | File externally edited between read/commit | Conflict; no overwrite |
| SAFE-05 | One broken type + one valid type | Broken type skipped; valid type can be organized |
| SAFE-06 | Two full runs | Byte-identical second run |
| CLI-01 | `--check` on dirty files | Exit 1, no source writes |
| CLI-02 | `--diff` | Valid unified diff; no source writes |
| CLI-03 | `--only wrap` | No reordering/region/import changes |
| CLI-04 | `--fail-on-skipped` | Nonzero when incomplete |
| CFG-01 | Child array replacement | Parent rules not merged into child |
| CFG-02 | Exclusions inheritance | Additive patterns |
| CFG-03 | Malformed/unknown configuration | Exit 2 before source changes |
| CFG-04 | `--config` override | Explicit organizer config wins; per-file `.editorconfig` remains active |

### 10.3 Golden fixtures and example expected output

```text
tests/Fixtures/
  Organization/InitializersAndRegions/{input.cs,expected.cs,config.json}
  Organization/SignatureSorting/{input.cs,expected.cs,config.json}
  Directives/ConditionalRegionSkip/{input.cs,expected.cs,config.json}
  Wrapping/NestedCalls/{input.cs,expected.cs,config.json,.editorconfig}
  Wrapping/ExistingMultiline/{input.cs,expected.cs,config.json}
  Imports/MultiTargetedUsings/{Project.csproj,Program.cs,...}
  Configuration/InheritedRules/{root-config.json,child-config.json,...}
```

Every golden test runs `input → output`, asserts `expected`, then runs `output → output2` and asserts exact equality. If a skipped type is expected, also assert its diagnostic ID and that its original organization/regions were retained.

### 10.4 Test portability and CI

- Run CI on Linux and Windows, and preferably macOS for SDK/path/line-ending coverage.
- Test against the project's pinned SDK and one newer compatible SDK where feasible.
- Restore dependencies explicitly in CI, then run `dotnet test` without restoring during workspace processing.
- Use small deterministic repository fixtures; avoid snapshot tests that depend on culture, OS locale, dictionary iteration order, or absolute temp paths.
- Measure basic throughput on a synthetic large solution without setting hard performance promises before a benchmark baseline exists.

## 11. Milestones and Codex execution plan

Each milestone must be independently testable and leave the repository building. **Do not implement all transformations in one unreviewable change.** Deliver changes in the order below; an API feasibility spike may reveal a necessary documented adjustment to the technical mechanism, but not to the user-confirmed behavior.

### M0 — Scaffold and public API spikes

**Tasks:** Create .NET 11 solution and test projects; pin supported Roslyn/MSBuild packages; verify `MSBuildLocator` setup; confirm `MSBuildWorkspace` project and solution loading on Linux; inspect public formatting/import API; investigate stable IDE0005-style cleanup integration; confirm `.slnx` support or establish explicit unsupported diagnostic.

**Exit criteria:** `dotnet build` and `dotnet test` pass; tests demonstrate one syntax-only document formatting pass and one project-aware read with no writes; a short feasibility note records exact API/package versions and any constraints.

### M1 — Configuration, CLI and file snapshots

**Tasks:** Versioned JSON Schema validation, nested inheritance, array replacement, exclusions, EditorConfig bridge, CLI parser, discovery, source bytes/BOM/newline snapshots, check/diff output, stable diagnostic IDs/JSON report.

**Exit criteria:** CFG-01..04, WS-01, CLI-01/02 pass; no source writes when `--check` or `--diff` is used.

### M2 — Member classification and constrained ordering

**Tasks:** Roslyn member descriptors, first-match rule engine, default category rules, effective accessibility, signature comparer, initializer/layout/COM constraints, priority-queue topological sorter, protected segment partitioning.

**Exit criteria:** ORG-01..13 and SAFE-05 test fixtures pass; per-type skipped transformations do not block other types.

### M3 — Region/trivia transformation

**Tasks:** Directive-pair analysis, managed region classification/removal, comment/attribute ownership, canonical region creation and blank-line normalization, repeat-region handling.

**Exit criteria:** REG-01..07 and TRV-01..03 pass; region balance and comment-preservation invariants pass.

### M4 — Formatting and wrapping

**Tasks:** EditorConfig-aware Roslyn formatting, threshold-triggered wrapping, nested-call fixed-point algorithm, existing-multiline preservation, same-line closing parenthesis, encoding/newline formatting, target-span formatting.

**Exit criteria:** WRAP-01..08 and FMT-01..04 pass; full-pipeline idempotency passes for golden fixtures.

### M5 — Workspace integration and semantic cleanup

**Tasks:** project/solution discovery, multi-TFM inner-build enumeration, linked-file identity map, no-restore behavior, strict load failure, explicit syntax fallback, project-aware import cleanup, baseline/candidate compiler diagnostics.

**Exit criteria:** WS-02..08, IMP-01..04, SAFE-01..03 pass. If the first IDE0005 integration is not viable, investigate another public-API implementation. Block v1 completion pending a safe approach or explicit user approval to change scope; do not ship an unsafe surrogate.

### M6 — Validation, writes and CI hardening

**Tasks:** stage-aware token/trivia integrity checks, second-run idempotency runtime gate, in-memory workspace validation, file hash rechecks, best-effort atomic writes, cancellation, JSON report, deterministic output, platform CI.

**Exit criteria:** SAFE-04/06, CLI-03/04 pass; a medium-sized fixture solution produces no new compilation errors and no second-run diff.

### M7 — Documentation, distribution and dogfooding

**Tasks:** CLI README, `docs/configuration.md`, `docs/safety.md`, examples, dotnet-tool packaging, local manifest instructions, regression corpus, changelog and known-limitations list.

**Exit criteria:** Clean installation on Linux/Windows; `dotnet tool run formatthias ./fixture.sln --check` runs as documented; tests pass; publish only after package-name verification and any requested review.

## 12. Definition of done

The first release is ready for review when:

- [ ] The CLI formats `.cs`, directories, projects and supported solutions using the specified workspace selection logic.
- [ ] Per-file effective JSON config resolves correctly; child rule arrays replace inherited arrays; EditorConfig remains authoritative for standard formatting.
- [ ] Default member ordering and fully custom rule matching work with deterministic stable sorting.
- [ ] Initialization and layout constraints are enforced and tested; unsafe types are skipped with diagnostics.
- [ ] Signature ordering covers constructors and method overloads.
- [ ] Existing member-level organizational regions are replaced with canonical groups, including one-member groups and repeated names.
- [ ] Comment, attributes, directives, and XML docs are preserved as specified.
- [ ] Wrapping is threshold-triggered, one item per line, and closes on the last-item line.
- [ ] Import sorting works; unused-using removal is semantically sound across project/TFM contexts. If this cannot be delivered, request explicit approval for a reduced-scope release rather than claiming v1 is complete.
- [ ] `--check`, `--diff`, `--only`, reports, exit codes, and strict project-load failure operate as specified.
- [ ] Multiple TFMs and linked files are handled without duplicate or conflicting physical writes.
- [ ] Structural and project compilation checks reject unsafe candidate changes.
- [ ] The full enabled pipeline is byte-for-byte idempotent on every supported golden fixture.
- [ ] Build/test CI is green on supported platforms; documentation states limitations and trust requirements.

## 13. Explicit non-goals and release boundaries

**Do not implement in v1:** rename/move/split types; reorder enum entries; source rewriting based on analyzer style suggestions (e.g. `var`, expression bodies, null-check syntax); comment/XML doc text reflow; fluent-chain break insertion; general expression folding; custom IDE integration; persistent incremental cache; Git-changed-lines-only mode; broad third-party analyzer code fixes; automatic NuGet restore; cross-repository atomic transactions; full MSBuild build verification against unstaged original sources.

**Supported but conservative:** unknown attributes, malformed directives, syntax errors, unusual comments, unsupported language versions, conditional compilation contexts, and interop types. The correct behavior in such cases is **skip and explain**, not "best effort" source mutation.

## 14. Suggested Codex workflow

1. Create an issue/task list from M0–M7; link tests to milestones.
2. Implement M0 first and record actual public API signatures and package versions. If `.slnx` or IDE0005 support differs from expectations, use the specified capability gates; do not silently omit it.
3. Build immutable transformation plans before mutating syntax trees.
4. Add each golden fixture **before** its implementation; keep regression fixtures minimal and readable.
5. Enforce the safety gates in test code and production code; they are not optional assertions only in debug builds.
6. Report complete changes per milestone: files changed, tests run, known unsupported cases, and measured deviations from this document.
7. Avoid broad repo formatting until the full transformation pipeline passes golden and idempotency tests.
8. Treat the decisions in §0.1 as binding and do not change them without explicit user approval.

## 15. Reference documentation verified for this design

These are implementation references, not dependencies to vendor into the repository. Resolve API signatures against the pinned package version during M0.

- [C# language specification — classes and field initializer order](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes)
- [C# language specification — declaration ordering](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts)
- [Microsoft Learn — .NET SDK `dotnet format`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- [Roslyn `Formatter` public API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.formatting.formatter)
- [Roslyn `MSBuildWorkspace` source/API](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs)
- [Microsoft.Build.Locator integration guidance](https://github.com/microsoft/msbuildlocator/blob/main/src/MSBuildLocator/README.md)
- [MSBuild multi-targeting inner-build evaluation](https://github.com/dotnet/msbuild/blob/main/documentation/specs/static-graph.md)
- [IDE0005 unnecessary using directive rule](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0005)
- [.NET release/support policy](https://learn.microsoft.com/dotnet/core/releases-and-support)

---

**End of design.** Companion files: `config.schema.json` (machine-readable schema) and `sample-config.json` (valid configuration illustrating v1 defaults). This is the agreed Codex implementation baseline; any functionality outside these boundaries should be proposed separately.
