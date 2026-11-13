using System.Collections.Immutable;
using ForMatthias.Core;
using ForMatthias.Cli;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ForMatthias.Tests;

public sealed class DogfoodTests
{
    [Fact]
    public async Task Disposable_repository_solution_validates_writes_and_has_no_second_diff()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var copy = new TempDirectory();
        foreach (var source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj" or ".git" or "artifacts")))
        {
            var target = Path.Combine(copy.Path, Path.GetRelativePath(root, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        using var output = new StringWriter(); using var error = new StringWriter();
        var solution = Path.Combine(copy.Path, "ForMatthias.slnx");
        Assert.True(await Application.RunAsync([solution, "--restore"], output, error) == 0, error.ToString());
        Assert.True(await Application.RunAsync([solution, "--check", "--diff"], output, error) == 0, error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task Repository_sources_reach_a_byte_identical_fixed_point()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var workspace = new AdhocWorkspace();
        var failures = new List<string>();
        foreach (var path in ForMatthias.Workspaces.Discovery.Scan(Path.Combine(root, "src"), "*.cs").Order(StringComparer.Ordinal))
        {
            var source = File.ReadAllText(path);
            var editor = EditorSettings.Resolve(path);
            var doc = SyntaxDocuments.Create(workspace, path, source, editor);
            var request = new TransformRequest(doc, EffectiveConfig.Default, editor, ImmutableHashSet.Create("organize", "format", "wrap", "cleanup"));
            var pipeline = new TransformationPipeline();
            var first = await pipeline.TransformAsync(request);
            var second = await pipeline.TransformAsync(request with { Document = doc.WithText(SourceText.From(first.Text)) });
            if (first.Text != second.Text)
            {
                var index = 0;
                while (index < Math.Min(first.Text.Length, second.Text.Length) && first.Text[index] == second.Text[index]) { index++; }
                var start = Math.Max(0, index - 100);
                failures.Add(path + " at " + index + "\nFIRST: " + first.Text.Substring(start, Math.Min(400, first.Text.Length - start)) + "\nSECOND: " + second.Text.Substring(start, Math.Min(400, second.Text.Length - start)));
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }
}
