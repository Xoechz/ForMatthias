using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Xunit;

namespace ForMatthias.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Public_formatter_formats_syntax_document()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Spike", LanguageNames.CSharp);
        var doc = workspace.AddDocument(project.Id, "C.cs", Microsoft.CodeAnalysis.Text.SourceText.From("class C{void M(){}}"));
        var formatted = await Formatter.FormatAsync(doc);
        Assert.Contains("class C", (await formatted.GetTextAsync()).ToString());
        Assert.Equal("class C { void M() { } }", (await formatted.GetTextAsync()).ToString());
    }

    [Fact]
    public void Compiler_exposes_semantically_unnecessary_imports()
    {
        var tree = CSharpSyntaxTree.ParseText("using System; class C {}", new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose));
        var compilation = CSharpCompilation.Create("Spike", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Contains(compilation.GetDiagnostics(), d => d.Id == "CS8019");
    }
}
