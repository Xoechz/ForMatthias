using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

public sealed class MemberSpacing(EffectiveConfig config, EditorSettings editor) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) => Apply((TypeDeclarationSyntax)base.VisitClassDeclaration(node)!);
    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) => Apply((TypeDeclarationSyntax)base.VisitStructDeclaration(node)!);
    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) => Apply((TypeDeclarationSyntax)base.VisitRecordDeclaration(node)!);
    public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => Apply((TypeDeclarationSyntax)base.VisitInterfaceDeclaration(node)!);

    private TypeDeclarationSyntax Apply(TypeDeclarationSyntax type)
    {
        if (type.Members.Count == 0 || type.ContainsDiagnostics) { return type; }
        // Region boundaries are managed by the organizer, and directive scopes are protected.
        if (type.Members.Any(m => m.GetLeadingTrivia().Any(t => t.IsDirective) || m.GetTrailingTrivia().Any(t => t.IsDirective)) || type.CloseBraceToken.LeadingTrivia.Any(t => t.IsDirective)) { return type; }
        static bool Whitespace(SyntaxTriviaList trivia) => trivia.All(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia));
        var depth = type.AncestorsAndSelf().Count(n => n is TypeDeclarationSyntax or NamespaceDeclarationSyntax);
        var indent = editor.IndentLevels(depth);
        var members = type.Members.ToArray();
        for (var index = 0; index < members.Length; index++)
        {
            if (!Whitespace(members[index].GetLeadingTrivia()) || index > 0 && !Whitespace(members[index - 1].GetTrailingTrivia())) { continue; }
            var member = members[index];
            var key = member switch { BaseFieldDeclarationSyntax => "betweenFields", PropertyDeclarationSyntax or IndexerDeclarationSyntax => "betweenProperties", ConstructorDeclarationSyntax => "betweenConstructors", _ => "betweenMethods" };
            var count = index == 0 ? 0 : Math.Min(config.GetNumber("format.blankLines." + key, member is BaseFieldDeclarationSyntax ? 0 : 1), config.GetNumber("format.blankLines.maxConsecutive", 1));
            if (index > 0) { members[index - 1] = members[index - 1].WithTrailingTrivia(SyntaxFactory.EndOfLine(editor.NewLine)); }
            members[index] = member.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(string.Concat(Enumerable.Repeat(editor.NewLine, count)) + indent));
        }
        var open = type.OpenBraceToken;
        if (Whitespace(open.TrailingTrivia) && Whitespace(type.Members[0].GetLeadingTrivia())) { open = open.WithTrailingTrivia(SyntaxFactory.ParseLeadingTrivia(string.Concat(Enumerable.Repeat(editor.NewLine, 1 + config.GetNumber("format.blankLines.afterTypeOpenBrace", 0))))); }
        var close = type.CloseBraceToken;
        if (Whitespace(close.LeadingTrivia) && Whitespace(members[^1].GetTrailingTrivia()))
        {
            members[^1] = members[^1].WithTrailingTrivia(SyntaxFactory.EndOfLine(editor.NewLine));
            close = close.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(string.Concat(Enumerable.Repeat(editor.NewLine, config.GetNumber("format.blankLines.beforeTypeCloseBrace", 0))) + editor.IndentLevels(depth - 1)));
        }
        return type.WithOpenBraceToken(open).WithMembers(SyntaxFactory.List(members)).WithCloseBraceToken(close);
    }
}
