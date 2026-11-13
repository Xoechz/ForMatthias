using System.Text.Json;
using ForMatthias.Cli;
using ForMatthias.Workspaces;
using Xunit;

namespace ForMatthias.Tests;

public sealed class CliTests
{
    [Fact]
    public async Task CLI_01_02_Check_and_diff_never_write()
    {
        using var dir = new TempDirectory();
        const string source = "class C { void Z() {} int a; }";
        var path = dir.Write("C.cs", source);
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await Application.RunAsync([path, "--check", "--diff"], output, error);
        Assert.True(exit == 1, error.ToString());
        Assert.Contains("@@", output.ToString());
        Assert.Equal(source, File.ReadAllText(path));
    }

    [Fact]
    public async Task CLI_03_Wrap_only_preserves_imports_and_member_order()
    {
        using var dir = new TempDirectory();
        const string source = "using Z;\nusing A;\nclass C { void Z() {} int a; }";
        var path = dir.Write("C.cs", source);
        Assert.Equal(0, await Application.RunAsync([path, "--only", "wrap"], new StringWriter(), new StringWriter()));
        Assert.Equal(source, File.ReadAllText(path));
    }

    [Fact]
    public async Task CLI_04_IMP_03_Skipped_semantic_cleanup_reported()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.cs", "using System;\nclass C {}\n");
        var report = System.IO.Path.Combine(dir.Path, "report.json");
        Assert.Equal(1, await Application.RunAsync([path, "--check", "--fail-on-skipped", "--report", report], new StringWriter(), new StringWriter()));
        using var json = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal("syntax", json.RootElement.GetProperty("mode").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("files")[0].GetProperty("skippedCapabilities").EnumerateArray());
        Assert.Contains("using System;", File.ReadAllText(path));
    }

    [Fact]
    public async Task CFG_03_Invalid_later_config_prevents_all_writes()
    {
        using var dir = new TempDirectory();
        var first = dir.Write("a/C.cs", "class C{void Z(){}int a;}");
        dir.Write("z/D.cs", "class D{}");
        dir.Write("z/.csharp-organizer.json", "{\"schemaVersion\":1,\"unknown\":0}");
        Assert.Equal(2, await Application.RunAsync([dir.Path], new StringWriter(), new StringWriter()));
        Assert.Equal("class C{void Z(){}int a;}", File.ReadAllText(first));
    }

    [Fact]
    public async Task SAFE_04_Commit_conflict_does_not_overwrite_external_edit()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.cs", "class C{}");
        var snapshot = await SourceSnapshot.ReadAsync(path);
        File.WriteAllText(path, "external edit");
        var exception = await Assert.ThrowsAsync<ForMatthias.Core.OrganizerException>(() => new ChangeCommitter().CommitAsync(snapshot, "candidate", default));
        Assert.Equal("CSORG7001", exception.Id);
        Assert.Equal("external edit", File.ReadAllText(path));
    }

    [Fact]
    public async Task SAFE_06_Real_write_second_run_has_no_diff()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.cs", "class C { void Z() {} int a; }\n");
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([path], new StringWriter(), error) == 0, error.ToString());
        var bytes = File.ReadAllBytes(path);
        Assert.True(await Application.RunAsync([path, "--check"], new StringWriter(), error) == 0, error.ToString());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
