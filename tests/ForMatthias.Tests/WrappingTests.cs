using ForMatthias.Core;
using Xunit;

namespace ForMatthias.Tests;

public sealed class WrappingTests
{
    [Fact]
    public async Task WRAP_01_02_03_Threshold_and_closing_parenthesis()
    {
        const string source = "class C { void M() { Call(firstArgument, secondArgument); } }";
        var width = ListWrapper.Width(source, 4);
        var fits = await TransformTest.Run(source, "wrap", json => json["wrapping"]!["maxLineLength"] = width);
        Assert.Equal(source, fits.Text);
        var wrapped = await TransformTest.Run(source, "wrap", json => json["wrapping"]!["maxLineLength"] = width - 1);
        Assert.Contains("Call(\n    firstArgument,\n    secondArgument);", wrapped.Text);
        Assert.DoesNotContain("#region", wrapped.Text);
    }

    [Fact]
    public async Task WRAP_04_Declaration_final_parameter_closes_same_line()
    {
        var result = await TransformTest.Run("class C\n{\n    public void Execute(int customerIdentifier, string customerName, bool enabled) {}\n}", "wrap", json => json["wrapping"]!["maxLineLength"] = 50);
        Assert.Contains("bool enabled) {}", result.Text);
        Assert.Contains("Execute(\n", result.Text);
    }

    [Fact]
    public async Task WRAP_05_Outer_first_nested_only_when_still_overflowing()
    {
        var result = await TransformTest.Run("class C\n{\n    void M()\n    {\n        Process(Create(firstArgument, secondArgument, thirdArgument), cancellationToken);\n    }\n}\n", "wrap", json => json["wrapping"]!["maxLineLength"] = 55);
        Assert.Contains("Process(\n", result.Text);
        Assert.Contains("Create(\n", result.Text);
        Assert.Contains("thirdArgument),\n", result.Text);
    }

    [Fact]
    public async Task WRAP_06_Existing_multiline_grouping_preserved()
    {
        var source = "class C { void M() { Call(\n    first, second,\n    third); } }";
        Assert.Equal(source, (await TransformTest.Run(source, "wrap")).Text);
    }

    [Theory]
    [InlineData("Call(firstArgument, /* keep me */ secondArgument)")]
    [InlineData("Call(\"\"\"raw whitespace   \"\"\", secondArgument)")]
    public async Task WRAP_07_Protected_lists_skip_safely(string expression)
    {
        var source = "class C { void M() { " + expression + "; } }";
        var result = await TransformTest.Run(source, "wrap", json => json["wrapping"]!["maxLineLength"] = 40);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG4001");
    }

    [Fact]
    public async Task WRAP_08_Atomic_overflow_reported_without_splitting_string()
    {
        var result = await TransformTest.Run("class C { void M() { Call(\"a very long string argument that cannot be split into smaller expressions\"); } }", "wrap", json => json["wrapping"]!["maxLineLength"] = 40);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG4001");
        Assert.Contains("\"a very long string argument that cannot be split into smaller expressions\"", result.Text);
    }

    [Fact]
    public async Task FMT_04_Raw_string_contents_unchanged_by_full_pipeline()
    {
        var source = "class C\n{\n    string value = \"\"\"\n        raw content   \n        more text\n        \"\"\";\n}\n";
        var result = await TransformTest.Run(source, "organize,format,wrap,cleanup");
        Assert.Contains("        raw content   \n        more text\n        \"\"\"", result.Text);
    }

    [Fact]
    public void Width_uses_tabs_and_unicode_scalars()
    {
        Assert.Equal(6, ListWrapper.Width("\t😀a", 4));
    }
}
