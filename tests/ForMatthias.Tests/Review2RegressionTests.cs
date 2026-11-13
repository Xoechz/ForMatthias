using ForMatthias.Cli;
using ForMatthias.Core;
using Xunit;

namespace ForMatthias.Tests;

public sealed class Review2RegressionTests
{
    [Theory]
    [InlineData("--check", false)]
    [InlineData("--diff", false)]
    [InlineData("--check", true)]
    [InlineData("--diff", true)]
    public async Task Report_links_never_overwrite_source(string mode, bool introducedDuringProcessing)
    {
        using var dir = new TempDirectory();
        const string source = "class C {}\n";
        var path = dir.Write("Input.cs", source);
        var report = Path.Combine(dir.Path, "report.json");
        if (!introducedDuringProcessing) { File.CreateSymbolicLink(report, path); }
        using var errors = new CallbackWriter(() => { if (introducedDuringProcessing) { File.CreateSymbolicLink(report, path); } });
        Assert.Equal(2, await Application.RunAsync([path, mode, "--only", "format", "--verbose", "--report", report], new StringWriter(), errors));
        Assert.Equal(source, File.ReadAllText(path));
        Assert.Contains("Report path", errors.ToString());
    }

    [Fact]
    public async Task Report_parent_alias_cannot_hide_selected_physical_source()
    {
        using var dir = new TempDirectory();
        var physical = dir.Write("physical/source.data", "class C {}\n");
        var input = Path.Combine(dir.Path, "Input.cs");
        File.CreateSymbolicLink(input, physical);
        var alias = Path.Combine(dir.Path, "alias");
        Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(physical)!);
        Assert.Equal(2, await Application.RunAsync([input, "--check", "--report", Path.Combine(alias, "source.data")], new StringWriter(), new StringWriter()));
        Assert.Equal("class C {}\n", File.ReadAllText(physical));
    }

    [Fact]
    public async Task Ordinary_report_can_be_replaced_without_leftover_temporary_files()
    {
        using var dir = new TempDirectory();
        var input = dir.Write("Input.cs", "class C {}\n");
        var report = dir.Write("report.json", "old report");
        Assert.Equal(0, await Application.RunAsync([input, "--check", "--only", "wrap", "--report", report], new StringWriter(), new StringWriter()));
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(Directory.GetFiles(dir.Path, ".formatthias-report-*"));
    }

    [Theory]
    [InlineData(8, 4, "\t\t", "\t\t\t\t")]
    [InlineData(4, 8, "    ", "\t")]
    [InlineData(4, 3, "\t ", "\t\t  ")]
    public async Task Organization_uses_visual_columns_for_nested_indentation(int size, int width, string levelOne, string levelTwo)
    {
        var editor = Settings(size, width);
        var result = await TransformTest.Run("class C\n{\n" + levelOne + "class Nested\n" + levelOne + "{\nvoid Z() {}\nvoid A() {}\n" + levelOne + "}\n}\n", editor: editor);
        Assert.Contains("\n" + levelOne + "class Nested", result.Text);
        Assert.Contains("\n" + levelTwo + "#region Methods", result.Text);
        Assert.Contains("\n" + levelTwo + "void A() {}", result.Text);
        Assert.Contains("\n" + levelOne + "}\n", result.Text);
    }

    [Theory]
    [InlineData(8, 4, "\t\t", "\t\t\t\t")]
    [InlineData(4, 8, "    ", "\t")]
    [InlineData(4, 3, "\t ", "\t\t  ")]
    public async Task Wrapping_continues_from_existing_visual_column(int size, int width, string originalIndent, string continuation)
    {
        var source = "class C\n{\n" + originalIndent + "void M() => Call(firstArgument, secondArgument, thirdArgument);\n}\n";
        var result = await TransformTest.Run(source, "wrap", json => json["wrapping"]!["maxLineLength"] = 40, Settings(size, width));
        Assert.Contains("Call(\n" + continuation + "firstArgument,\n" + continuation + "secondArgument,\n" + continuation + "thirdArgument);", result.Text);
        Assert.Contains("\n" + originalIndent + "void M()", result.Text);
    }

    private static EditorSettings Settings(int size, int width) => new(new Dictionary<string, string>
    {
        ["indent_style"] = "tab", ["indent_size"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture), ["tab_width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture)
    }, []);

    private sealed class CallbackWriter(Action callback) : StringWriter
    {
        private bool _invoked;
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (!_invoked && buffer.Span.StartsWith("Processing ", StringComparison.Ordinal)) { _invoked = true; callback(); }
            return base.WriteLineAsync(buffer, cancellationToken);
        }
    }
}
