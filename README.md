# ForMatthias

ForMatthias is a cross-platform **.NET 11 C# organizer and formatter**, packaged as the `dotnet-formatthias` tool. It integrates configurable member ordering, canonical regions, Roslyn whitespace formatting, threshold-based argument/parameter wrapping, and safe import cleanup.

**Declaration order is externally observable.** Initializer execution sequences and recognized layout/interop constraints are protected, but reflection, serializers, generators, and other arbitrary external observers may depend on declaration order. Review diffs before adopting any organizer. Project mode evaluates MSBuild: use it only with trusted projects. See [safety](docs/safety.md).

## Build and install locally

Install a .NET 11 SDK, then:

```sh
dotnet restore ForMatthias.slnx
dotnet build ForMatthias.slnx --no-restore -c Release
dotnet test ForMatthias.slnx --no-restore -c Release
dotnet pack src/ForMatthias.Cli --no-restore -c Release -o artifacts
dotnet tool install dotnet-formatthias --tool-path artifacts/tool --add-source artifacts --version 1.0.0
artifacts/tool/formatthias --help
```

This repository includes a local tool manifest. After packing, verify it with:

```sh
dotnet tool restore --add-source artifacts
dotnet restore tests/Repositories/Smoke/Smoke.slnx
dotnet tool run formatthias tests/Repositories/Smoke/Smoke.slnx --check
```

On Windows the installed command is `artifacts/tool/formatthias.exe`. A suitable .NET SDK/MSBuild environment is needed for project mode; syntax mode does not invoke MSBuild or require Visual Studio.

For a standard repository-local manifest, run these commands in the consuming repository:

```sh
dotnet new tool-manifest
dotnet tool install dotnet-formatthias --add-source /absolute/path/to/ForMatthias/artifacts --version 1.0.0
dotnet tool run formatthias ./Fixture.slnx --check
```

The package is built locally; it has not been published. The NuGet exact package-name search returned no collision on 2026-11-12. Check availability again before publication. After publication, installation can use `dotnet tool install --global dotnet-formatthias`, followed by `formatthias`. Local-manifest installations additionally support `dotnet formatthias` and `dotnet tool run formatthias`.

## Usage

```sh
formatthias ./MySolution.slnx --check
formatthias ./src/App.csproj --diff
formatthias ./Feature.cs --workspace syntax --only organize,wrap
formatthias ./src --workspace project --framework net11.0
formatthias . --report ./artifacts/formatthias-report.json
```

The default path is the current directory. Direct `.cs` input defaults to syntax mode; projects and SLN/SLNX solutions use semantic mode. A directory selects its unique solution, otherwise its unique project, otherwise a recursive syntax scan. Multiple candidates are an error. Project-load errors are fatal unless `--on-load-failure syntax` is explicitly supplied. Dependencies must be restored beforehand; `--restore` explicitly permits a restore.

| Option | Behavior |
| --- | --- |
| `--workspace auto\|project\|syntax` | Hybrid selection, forced semantic context, or no MSBuild |
| `--framework <TFM>` | Narrow validation; otherwise load every discovered inner-build TFM |
| `--on-load-failure fail\|syntax` | Strict default or explicitly reported fallback |
| `--restore` | Explicit trusted restore |
| `--config <path>` | Complete organizer config above built-in defaults |
| `--check` | No source writes; exit 1 when changes are required |
| `--diff` | Unified diff on stdout; no source writes |
| `--only organize,format,wrap,cleanup` | Select capabilities |
| `--include <glob>`, `--exclude <glob>` | Repeatable selection filters |
| `--include-generated` | Opt in to generated physical source; explicit exclusions still apply |
| `--report <path>` | JSON statuses, hashes, diagnostics and commit results |
| `--fail-on-skipped` | Exit 1 if a requested capability/scope could not run |
| `--fail-on-new-warnings` | New compiler warnings block the candidate |
| `--verbose`, `--version`, `--help` | Progress, version and help |

Diagnostics/progress go to stderr so stdout remains suitable for patches. `--check --diff` combines diff output with check exit status. Reports contain no full source. A report is the only requested output file permitted in read-only modes, and cannot target a source file.

## Configuration and behavior

Place `.csharp-organizer.json` files in directory hierarchies. Objects merge recursively, child arrays replace parents, and exclusions are additive. `.editorconfig` controls standard Roslyn formatting. See [configuration](docs/configuration.md), [sample-config.json](sample-config.json) and [config.schema.json](config.schema.json).

Default order: constants, static fields, fields, static constructors, constructors, events, properties, indexers, methods, operators, nested types, finalizers. Safety edges may prevent this preferred order; repeated region names are intentional. Overloads sort by textual generic arity, parameter count, types and passing modifiers.

An overflowing eligible list is wrapped one item per line, with the closing parenthesis on the final item's line:

```csharp
var result = client.Process(
    customerIdentifier,
    requestOptions,
    cancellationToken);
```

Fitting lines and existing multiline breakpoints are retained. Long atomic expressions are diagnosed without splitting their contents. Syntax mode never guesses whether a using is unused. Semantic mode intersects compiler-proven unnecessary imports across every loaded context, then validates all candidate compilations together.

## Exit codes

`0` success; `1` check changes/skipped policy; `2` CLI/config; `3` workspace/evaluation/restore; `4` candidate validation; `5` IO/conflict; `130` cancellation. Fixed mixed-error priority is `2,3,4,5,1`.

## Development and limitations

See [development](docs/development.md), [API feasibility](docs/feasibility.md), [acceptance coverage](docs/acceptance.md), [CHANGELOG.md](CHANGELOG.md), and [ISSUES.md](ISSUES.md). CI is configured for Linux, Windows and macOS. Runtime gates check syntax, declaration/comment ownership, initialization/layout sequences, approved imports, compiler diagnostics, and byte-identical second execution before committing.

## Copyright

Copyright (C) 2026 Elias Leonhardsberger

This project is licensed under the GNU General Public License v3.0.
See the LICENSE file for details.
