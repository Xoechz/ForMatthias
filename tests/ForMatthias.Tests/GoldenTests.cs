using System.Collections.Immutable;
using ForMatthias.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ForMatthias.Tests;

public sealed class GoldenTests
{
    [Theory]
    [InlineData("Organization/InitializersAndRegions", "organize")]
    [InlineData("Organization/SignatureSorting", "organize")]
    [InlineData("Directives/ConditionalRegionSkip", "organize")]
    [InlineData("Wrapping/NestedCalls", "wrap")]
    [InlineData("Wrapping/ExistingMultiline", "wrap")]
    public async Task Golden_output_and_second_run_match_exactly(string fixture, string capability)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/Fixtures", fixture));
        var input = File.ReadAllText(Path.Combine(root, "input.cs"));
        var expected = File.ReadAllText(Path.Combine(root, "expected.cs"));
        var path = Path.Combine(root, "input.cs");
        var config = new ConfigurationResolver().Resolve(path, Path.Combine(root, "config.json"));
        using var workspace = new AdhocWorkspace();
        var editor = new EditorSettings(new Dictionary<string, string>(), []);
        var document = SyntaxDocuments.Create(workspace, path, input, editor);
        var request = new TransformRequest(document, config, editor, ImmutableHashSet.Create(capability));
        var pipeline = new TransformationPipeline();
        var result = await pipeline.TransformAsync(request);
        Assert.Equal(expected, result.Text);
        CandidateValidator.Validate(input, result.Text, (CSharpParseOptions)document.Project.ParseOptions!, result.Safety, config, result.OrganizedTypes);
        Assert.Equal(expected, (await pipeline.TransformAsync(request with { Document = document.WithText(SourceText.From(result.Text)) })).Text);
        if (fixture.StartsWith("Directives", StringComparison.Ordinal)) { Assert.Contains(result.Diagnostics, d => d.Id == "CSORG2002"); }
    }
}
