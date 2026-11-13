using ForMatthias.Cli;
using ForMatthias.Core;
using Xunit;

namespace ForMatthias.Tests;

public sealed class FormattingTests
{
    [Fact]
    public async Task FMT_01_Editorconfig_controls_tabs_and_brace_placement()
    {
        using var dir = new TempDirectory();
        dir.Write(".editorconfig", "root = true\n[*.cs]\nindent_style = tab\nindent_size = 4\ntab_width = 4\ncsharp_new_line_before_open_brace = none\ncsharp_preserve_single_line_blocks = false\n");
        var path = dir.Write("C.cs", "class C\n{\nvoid M()\n{\nint x=1;\n}\n}\n");
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([path, "--only", "format"], new StringWriter(), error) == 0, error.ToString());
        var text = File.ReadAllText(path);
        Assert.Contains("class C {", text);
        Assert.Contains("\tvoid M() {", text);
        Assert.Contains("\t\tint x = 1;", text);
    }

    [Fact]
    public async Task FMT_02_Per_file_configuration_and_editorconfig_width()
    {
        using var dir = new TempDirectory();
        dir.Write(".editorconfig", "root = true\n[*.cs]\nmax_line_length = 40\n");
        dir.Write("child/.csharp-organizer.json", """{"schemaVersion":1,"wrapping":{"maxLineLength":120}}""");
        const string source = "class C { void M() { Call(firstArgument, secondArgument); } }";
        var parent = dir.Write("C.cs", source); var child = dir.Write("child/D.cs", source);
        Assert.Equal(0, await Application.RunAsync([dir.Path, "--only", "wrap"], new StringWriter(), new StringWriter()));
        Assert.Contains("Call(\n", File.ReadAllText(parent));
        Assert.Equal(source, File.ReadAllText(child));
    }

    [Fact]
    public async Task IMP_01_Imports_remain_in_their_existing_scopes()
    {
        var result = await TransformTest.Run("using Z;\nusing System;\nnamespace N {\nusing B;\nusing A;\nclass C {}\n}\n", "cleanup");
        Assert.True(result.Text.IndexOf("using System;", StringComparison.Ordinal) < result.Text.IndexOf("using Z;", StringComparison.Ordinal));
        Assert.True(result.Text.IndexOf("namespace N", StringComparison.Ordinal) < result.Text.IndexOf("using A;", StringComparison.Ordinal));
        Assert.True(result.Text.IndexOf("using A;", StringComparison.Ordinal) < result.Text.IndexOf("using B;", StringComparison.Ordinal));
    }
}
