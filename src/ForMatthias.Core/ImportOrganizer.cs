using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

public sealed class ImportOrganizer(EffectiveConfig config, EditorSettings editor) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var visited = (CompilationUnitSyntax)base.VisitCompilationUnit(node)!;
        return visited.WithUsings(Organize(visited.Usings));
    }
    public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        var visited = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node)!;
        return visited.WithUsings(Organize(visited.Usings));
    }
    public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        var visited = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node)!;
        return visited.WithUsings(Organize(visited.Usings));
    }

    private SyntaxList<UsingDirectiveSyntax> Organize(SyntaxList<UsingDirectiveSyntax> usings)
    {
        if (usings.Count < 2 || usings.Any(u => u.ContainsDirectives || u.DescendantTrivia().Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia)))) { return usings; }
        IEnumerable<UsingDirectiveSyntax> result = usings;
        if (config.Bool("cleanup.removeDuplicateUsings")) { result = result.DistinctBy(u => u.WithoutTrivia().NormalizeWhitespace().ToFullString(), StringComparer.Ordinal); }
        if (config.Bool("cleanup.organizeUsings"))
        {
            var systemFirst = editor.Values.GetValueOrDefault("dotnet_sort_system_directives_first", "true") != "false";
            result = result.OrderBy(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) ? 0 : 1)
                .ThenBy(u => u.Alias is null ? 0 : 1)
                .ThenBy(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0)
                .ThenBy(u => systemFirst && (u.Name?.ToString() is "System" || u.Name?.ToString().StartsWith("System.", StringComparison.Ordinal) == true) ? 0 : 1)
                .ThenBy(u => u.Name?.ToString(), StringComparer.Ordinal)
                .ThenBy(u => u.Alias?.ToString(), StringComparer.Ordinal);
        }
        var first = usings[0].GetLeadingTrivia();
        var separate = editor.Values.GetValueOrDefault("dotnet_separate_import_directive_groups") == "true";
        string Group(UsingDirectiveSyntax u) => u.Alias is not null ? "alias" : u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? "static" : (u.Name?.ToString().Split('.')[0] ?? "");
        var sorted = result.ToArray();
        return SyntaxFactory.List(sorted.Select((u, i) => u.WithLeadingTrivia(i == 0 ? first : separate && Group(sorted[i - 1]) != Group(u) ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(editor.NewLine)) : default).WithTrailingTrivia(SyntaxFactory.EndOfLine(editor.NewLine))));
    }
}
