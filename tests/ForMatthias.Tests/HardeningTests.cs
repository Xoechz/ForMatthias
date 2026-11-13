using System.Text;
using ForMatthias.Cli;
using ForMatthias.Core;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ForMatthias.Tests;

public sealed class HardeningTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Full_pipeline_preserves_statement_relative_nested_list_indentation(bool preserveExisting)
    {
        const string source = """
            class C
            {
                object M() => Build(new Options { A = 1, B = 2 }, context.Values.Where(value => value.Enabled).Select(value => value.Name).ToArray());
            }
            """;
        var result = await TransformTest.Run(source, "organize,format,wrap,cleanup", json =>
        {
            json["wrapping"]!["maxLineLength"] = 65;
            json["wrapping"]!["preserveExistingMultiline"] = preserveExisting;
        });
        Assert.True(result.Wraps > 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FMT_03_Real_writes_preserve_encoding_bom_and_crlf(bool bom, bool utf16)
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, "C.cs");
        Encoding encoding = utf16 ? new UnicodeEncoding(false, bom) : new UTF8Encoding(bom);
        await File.WriteAllBytesAsync(path, [.. encoding.GetPreamble(), .. encoding.GetBytes("class C\r\n{\r\nvoid Z() {}\r\nint a;\r\n}\r\n")]);
        using var error = new StringWriter();
        Assert.True(await Application.RunAsync([path], new StringWriter(), error) == 0, error.ToString());
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith(encoding.GetPreamble()));
        var text = encoding.GetString(bytes.AsSpan(encoding.GetPreamble().Length));
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.True(await Application.RunAsync([path, "--check"], new StringWriter(), error) == 0, error.ToString());
    }

    [Fact]
    public async Task Report_cannot_overwrite_source_even_when_configuration_fails()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.cs", "class C{}");
        Assert.Equal(2, await Application.RunAsync([path, "--check", "--report", path], new StringWriter(), new StringWriter()));
        Assert.Equal("class C{}", File.ReadAllText(path));
    }

    [Fact]
    public async Task Context_directive_inside_method_protects_following_declarations()
    {
        var source = "class C\n{\nvoid Z()\n{\n#pragma warning disable CS0169\n}\nint a;\n}\n";
        var result = await TransformTest.Run(source);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG2002");
    }

    [Fact]
    public async Task Cleanup_preserves_comment_whitespace_verbatim()
    {
        var source = "// keep trailing comment spaces   \nclass C {}   \n";
        var result = await TransformTest.Run(source, "cleanup");
        Assert.Equal("// keep trailing comment spaces   \nclass C {}\n", result.Text);
    }

    [Fact]
    public void Structural_gate_rejects_tokens_enum_reordering_and_unapproved_import_deletion()
    {
        var options = new CSharpParseOptions();
        Assert.Throws<OrganizerException>(() => CandidateValidator.Validate("class C { int x = 1; }", "class C { int x = 2; }", options));
        Assert.Throws<OrganizerException>(() => CandidateValidator.Validate("enum E { A, B }", "enum E { B, A }", options));
        Assert.Throws<OrganizerException>(() => CandidateValidator.ValidateImports("using System; class C {}", "class C {}", options, false));
    }

    [Fact]
    public async Task Format_only_applies_custom_member_blank_lines()
    {
        var result = await TransformTest.Run("class C\n{\nvoid A() {}\nvoid B() {}\n}\n", "format", json => json["format"]!["blankLines"]!["betweenMethods"] = 1);
        Assert.Contains("void A() { }\n\n    void B()", result.Text);
        Assert.DoesNotContain("#region", result.Text);
    }

    [Fact]
    public async Task Cancellation_returns_130_without_source_write()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.cs", "class C{}");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Equal(130, await Application.RunAsync([path], new StringWriter(), new StringWriter(), cancellation.Token));
        Assert.Equal("class C{}", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("new C(firstArgument, secondArgument, thirdArgument)")]
    [InlineData("new(firstArgument, secondArgument, thirdArgument)")]
    [InlineData("(int firstParameter, string secondParameter, bool thirdParameter) => 1")]
    public async Task Supported_constructor_and_lambda_lists_wrap_without_token_changes(string expression)
    {
        var result = await TransformTest.Run("class C { void M() { var result = " + expression + "; } }", "wrap", json => json["wrapping"]!["maxLineLength"] = 40);
        Assert.True(result.Wraps > 0);
    }

    [Fact]
    public async Task Record_primary_constructor_and_constructor_initializer_are_targets()
    {
        var result = await TransformTest.Run("record C(int firstParameter, string secondParameter, bool thirdParameter); class D { public D(int firstParameter, string secondParameter) : base(firstParameter, secondParameter) {} }", "wrap", json => json["wrapping"]!["maxLineLength"] = 40);
        Assert.Contains("record C(\n", result.Text);
        Assert.Contains("base(\n", result.Text);
    }

    [Fact]
    public async Task Disabled_optional_targets_are_rejected_and_generated_selection_respects_excludes()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("C.g.cs", "class C{int z;int a;}");
        Assert.Equal(0, await Application.RunAsync([dir.Path, "--check"], new StringWriter(), new StringWriter()));
        Assert.Equal(1, await Application.RunAsync([dir.Path, "--check", "--include-generated"], new StringWriter(), new StringWriter()));
        Assert.Equal(0, await Application.RunAsync([dir.Path, "--check", "--include-generated", "--exclude", "*.g.cs"], new StringWriter(), new StringWriter()));
        var config = dir.Write("config.json", """{"schemaVersion":1,"wrapping":{"targets":{"attributes":true}}}""");
        Assert.Equal(2, await Application.RunAsync([path, "--config", config], new StringWriter(), new StringWriter()));
    }
}
