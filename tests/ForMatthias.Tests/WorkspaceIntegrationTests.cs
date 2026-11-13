using System.Diagnostics;
using System.Text.Json;
using ForMatthias.Cli;
using ForMatthias.Core;
using ForMatthias.Workspaces;
using Xunit;

namespace ForMatthias.Tests;

public sealed class WorkspaceIntegrationTests
{
    private static async Task Restore(string path)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("restore"); start.ArgumentList.Add(path); start.ArgumentList.Add("-nr:false");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    [Fact]
    public async Task WS_02_IMP_01_04_Semantic_cleanup_uses_real_compiler_binding()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>""");
        var path = dir.Write("C.cs", "using System.Text;\nusing System;\nusing Alias = System.Console;\nclass C { public void M() { Alias.WriteLine(\"hello\"); } }\n");
        await Restore(project);
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([project, "--only", "cleanup"], new StringWriter(), error) == 0, error.ToString());
        var after = File.ReadAllText(path);
        Assert.DoesNotContain("using System.Text;", after);
        Assert.DoesNotContain("using System;", after);
        Assert.Contains("using Alias = System.Console;", after);
        Assert.True(await Application.RunAsync([project, "--check", "--only", "cleanup"], new StringWriter(), error) == 0, error.ToString());
    }

    [Fact]
    public async Task WS_03_IMP_02_All_inner_framework_contexts_intersect_unnecessary_imports()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup></Project>""");
        var source = "using System;\nusing System.Text;\nclass C\n{\n#if NET11_0\n    StringBuilder M() => new();\n#else\n    int M() => 1;\n#endif\n}\n";
        var path = dir.Write("C.cs", source);
        await Restore(project);
        using var session = await ProjectSession.LoadAsync(Discovery.Select(project, "auto"), null, false, false, default);
        Assert.True(session.Files[PhysicalPaths.Normalize(path)].Count >= 2);
        var candidate = await session.RemoveUnusedUsingsAsync(PhysicalPaths.Normalize(path), source, default);
        Assert.Contains("using System.Text;", candidate);
        Assert.DoesNotContain("using System;", candidate);
        await session.ValidateAsync(new Dictionary<string, string> { [PhysicalPaths.Normalize(path)] = candidate }, false, default);
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([project], new StringWriter(), error) == 0, error.ToString());
        Assert.True(await Application.RunAsync([project, "--check"], new StringWriter(), error) == 0, error.ToString());
        Assert.Equal(3, await Application.RunAsync([project, "--framework", "net9.0", "--check"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task WS_04_Linked_source_has_one_physical_candidate_and_write()
    {
        using var dir = new TempDirectory();
        const string project = """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="../Shared.cs" Link="Shared.cs" /></ItemGroup></Project>""";
        dir.Write("A/A.csproj", project); dir.Write("B/B.csproj", project);
        var shared = dir.Write("Shared.cs", "public class C { public void Z() {} private int a; }\n");
        var solution = dir.Write("Fixture.slnx", """<Solution><Project Path="A/A.csproj"/><Project Path="B/B.csproj"/></Solution>""");
        await Restore(solution);
        var report = System.IO.Path.Combine(dir.Path, "report.json");
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([solution, "--report", report], new StringWriter(), error) == 0, error.ToString());
        using var json = JsonDocument.Parse(File.ReadAllText(report));
        var sharedReports = json.RootElement.GetProperty("files").EnumerateArray().Where(f => f.GetProperty("path").GetString() == shared).ToArray();
        Assert.Single(sharedReports);
        Assert.True(sharedReports[0].GetProperty("committed").GetBoolean());
        Assert.True(await Application.RunAsync([solution, "--check"], new StringWriter(), error) == 0, error.ToString());
    }

    [Fact]
    public async Task WS_05_06_Strict_load_failure_and_explicit_fallback()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Broken.csproj", "<Project><Malformed></Project>");
        var source = dir.Write("C.cs", "class C { void Z() {} int a; }");
        Assert.Equal(3, await Application.RunAsync([project], new StringWriter(), new StringWriter()));
        Assert.Equal("class C { void Z() {} int a; }", File.ReadAllText(source));
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([project, "--on-load-failure", "syntax", "--check", "--fail-on-skipped"], new StringWriter(), error) == 1, error.ToString());
        Assert.Contains("Explicit syntax fallback", error.ToString());
    }

    [Fact]
    public async Task SAFE_01_02_Baseline_errors_allowed_new_errors_rejected()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>""");
        var path = dir.Write("C.cs", "class C { Missing M() => null; }\n");
        await Restore(project);
        using var session = await ProjectSession.LoadAsync(Discovery.Select(project, "auto"), null, false, false, default);
        await session.ValidateAsync(new Dictionary<string, string> { [path] = "class C\n{\n Missing M() => null;\n}\n" }, false, default);
        var exception = await Assert.ThrowsAsync<OrganizerException>(() => session.ValidateAsync(new Dictionary<string, string> { [path] = "class C { Missing M() => null; int N() => \"bad\"; }" }, false, default));
        Assert.Equal("CSORG6001", exception.Id);
    }

    [Fact]
    public async Task Classic_sln_loads_and_missing_assets_require_explicit_restore()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>""");
        dir.Write("C.cs", "class C {}\n");
        var solution = dir.Write("Fixture.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Fixture", "Fixture.csproj", "{A77B0719-989A-45DF-B9D2-C6950E21F800}"
            EndProject
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Debug|Any CPU = Debug|Any CPU
                EndGlobalSection
                GlobalSection(ProjectConfigurationPlatforms) = postSolution
                    {A77B0719-989A-45DF-B9D2-C6950E21F800}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {A77B0719-989A-45DF-B9D2-C6950E21F800}.Debug|Any CPU.Build.0 = Debug|Any CPU
                EndGlobalSection
            EndGlobal
            """ + "\n");
        Assert.Equal(3, await Application.RunAsync([project, "--check"], new StringWriter(), new StringWriter()));
        Assert.False(File.Exists(System.IO.Path.Combine(dir.Path, "obj", "project.assets.json")));
        await Restore(project);
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([solution, "--check", "--only", "cleanup"], new StringWriter(), error) == 0, error.ToString());
    }
}
