# Source access and rebuilding

ForMatthias is Copyright (C) 2026 Elias Leonhardsberger, licensed under
**GNU GPL version 3 only (SPDX: GPL-3.0-only)**. See `LICENSE` for the complete
terms. The project repository is <https://github.com/Xoechz/ForMatthias>.

## Matching project source

Published releases must identify the matching source commit in the package's
`.nuspec` repository metadata and link to that source alongside the download.
Given that commit, obtain the complete project and build files at:

`https://github.com/Xoechz/ForMatthias/tree/<commit>`

or download its archive at:

`https://github.com/Xoechz/ForMatthias/archive/<commit>.zip`

Release information is at <https://github.com/Xoechz/ForMatthias/releases>.
Source access is free and does not require a source archive inside the tool
package. A locally packed build containing uncommitted changes is not represented
by its Git commit alone: publish the matching source before distributing that
build. Do not use a moving branch as the sole reference for a binary release.

With that source checked out and a suitable .NET 11 SDK installed:

```sh
dotnet restore ForMatthias.slnx
dotnet build ForMatthias.slnx --no-restore -c Release
dotnet test ForMatthias.slnx --no-restore -c Release
dotnet pack ForMatthias.Cli --no-restore -c Release -o artifacts
dotnet tool install dotnet-formatthias --tool-path artifacts/tool --add-source artifacts --version 1.0.0
```

## Dependency source

Dependencies retain their MIT licenses. The following version-matched source
archives cover the libraries shipped by the current package, including the
dependencies embedded in Roslyn's .NET Framework and .NET build hosts. Each
archive contains the upstream source and its build files.

| Components | Source archive |
| --- | --- |
| Humanizer.Core 2.14.1 | [Humanizer, 3ebc38de585fc641a04b0e78ed69468453b0f8a1](https://github.com/Humanizr/Humanizer/archive/3ebc38de585fc641a04b0e78ed69468453b0f8a1.zip) |
| Microsoft.CodeAnalysis.* 5.9.0, including build hosts and Razor compiler bridge | [Roslyn, 35d9211b841e7613c1d2f8f5af6d628ace696c4c](https://github.com/dotnet/roslyn/archive/35d9211b841e7613c1d2f8f5af6d628ace696c4c.zip) |
| Microsoft.Build.Locator 1.11.2 | [MSBuildLocator, 89a9d0858a83930f5066e4fa0bf0d154e24095d7](https://github.com/microsoft/MSBuildLocator/archive/89a9d0858a83930f5066e4fa0bf0d154e24095d7.zip) |
| Embedded Microsoft.Build.Locator 1.10.2 | [MSBuildLocator, 694ff392b2dcf6ac58fe865afd1c982eb9449014](https://github.com/microsoft/MSBuildLocator/archive/694ff392b2dcf6ac58fe865afd1c982eb9449014.zip) |
| Microsoft.VisualStudio.SolutionPersistence 1.0.52 | [SolutionPersistence, 19c3ca7dc997dba2b3a86f6c666e9f717f34c8fe](https://github.com/microsoft/vs-solutionpersistence/archive/19c3ca7dc997dba2b3a86f6c666e9f717f34c8fe.zip) |
| Microsoft.Extensions.DependencyInjection/Logging, System.Composition.*, Microsoft.Bcl.AsyncInterfaces, System.IO.Pipelines, System.Text.Encodings.Web and System.Text.Json 10.0.1 | [.NET, fad253f51b461736dfd3cd9c15977bb7493becef](https://github.com/dotnet/dotnet/archive/fad253f51b461736dfd3cd9c15977bb7493becef.zip) |
| Embedded Microsoft.IO.Redist 6.1.0 | [Maintenance packages, d0c2a5a83211e271826172a6b0510c25a52dbd53](https://github.com/dotnet/maintenance-packages/archive/d0c2a5a83211e271826172a6b0510c25a52dbd53.zip) |
| Embedded System.Buffers/System.Numerics.Vectors 4.6.1 | [Maintenance packages, 6b84308c9ad012f53240d72c1d716d7e42546483](https://github.com/dotnet/maintenance-packages/archive/6b84308c9ad012f53240d72c1d716d7e42546483.zip) |
| Embedded System.Memory/System.Threading.Tasks.Extensions 4.6.3 and System.Runtime.CompilerServices.Unsafe 6.1.2 | [Maintenance packages, f62ca0009b038cab4725a720f386623a969d73ad](https://github.com/dotnet/maintenance-packages/archive/f62ca0009b038cab4725a720f386623a969d73ad.zip) |

The installed .NET SDK/runtime is not bundled. Central package management records
the direct build dependencies; NuGet metadata records their transitive versions.
Update this table and review upstream notices when changing dependencies.

GPLv3 §6(d) permits source on a different server, but the distributor remains
responsible for equivalent, no-charge access. Keep these source links available
with the binary release; mirror the archives if upstream availability cannot be
maintained. Preserve `LICENSE`, `THIRD-PARTY-NOTICES.md`, and the upstream files
under `licenses/` when redistributing the package.
