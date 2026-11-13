using ForMatthias.Core;
using ForMatthias.Cli;
using ForMatthias.Workspaces;
using Xunit;

namespace ForMatthias.Tests;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "formatthias-tests-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public string Write(string relative, string text)
    {
        var path = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() => Directory.Delete(Path, true);
}

public sealed class ConfigurationTests
{
    [Fact]
    public void CFG_01_02_04_Arrays_replace_exclusions_add_and_explicit_config_is_complete()
    {
        using var dir = new TempDirectory();
        dir.Write(".csharp-organizer.json", """{"schemaVersion":1,"root":true,"organization":{"rules":[{"id":"parent","region":"Parent"}]},"exclusions":["a.cs"]}""");
        var child = dir.Write("child/.csharp-organizer.json", """{"schemaVersion":1,"organization":{"rules":[]},"exclusions":["b.cs"]}""");
        var path = dir.Write("child/C.cs", "class C{}");
        var resolver = new ConfigurationResolver();
        var config = resolver.Resolve(path);
        Assert.Empty(config.Node("organization.rules")!.AsArray());
        Assert.Equal(new[] { "a.cs", "b.cs" }, config.Strings("exclusions"));
        Assert.Empty(resolver.Resolve(path, child).Strings("exclusions").Except(["b.cs"]));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"unknown\":true}")]
    [InlineData("{\"schemaVersion\":1,\"regions\":{\"minMembers\":0}}")]
    [InlineData("{\"schemaVersion\":1,\"organization\":{\"rules\":[{\"id\":\"x\",\"region\":\"x\",\"match\":{\"kind\":[]}}]}}")]
    public void CFG_03_Invalid_config_rejected(string json)
    {
        using var dir = new TempDirectory();
        var path = dir.Write(".csharp-organizer.json", json);
        Assert.Equal(2, Assert.Throws<OrganizerException>(() => new ConfigurationResolver().Resolve(path)).ExitCode);
    }

    [Fact]
    public void WS_01_07_Discovery_selects_syntax_for_direct_file_and_rejects_ambiguity()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.cs", "class C{}");
        dir.Write("a.sln", ""); dir.Write("b.slnx", "");
        Assert.False(Discovery.Select(path, "auto").IsProject);
        Assert.Throws<OrganizerException>(() => Discovery.Select(dir.Path, "auto"));
    }

    [Fact]
    public void Cli_validates_flags_and_diff_is_unified()
    {
        var cli = CliOptions.Parse(["C.cs", "--check", "--diff", "--only", "wrap"]);
        Assert.True(cli.Check && cli.Diff);
        Assert.Equal("wrap", Assert.Single(cli.Capabilities));
        Assert.Throws<OrganizerException>(() => CliOptions.Parse(["--unknown"]));
        Assert.Contains("@@ -1,1 +1,1 @@", UnifiedDiff.Create("C.cs", "a\n", "b\n"));
    }

    [Fact]
    public async Task FMT_03_Snapshot_roundtrips_utf16_bom_crlf()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, "C.cs");
        var encoding = new System.Text.UnicodeEncoding(false, true);
        await File.WriteAllBytesAsync(path, [.. encoding.GetPreamble(), .. encoding.GetBytes("class C {}\r\n")]);
        var snapshot = await SourceSnapshot.ReadAsync(path);
        Assert.Equal("\r\n", snapshot.NewLine);
        Assert.Equal(snapshot.Bytes, snapshot.Encode(snapshot.Text));
    }
}
