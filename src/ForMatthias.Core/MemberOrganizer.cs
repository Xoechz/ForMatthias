using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

public sealed class MemberOrganizer(EffectiveConfig config, EditorSettings editor, List<OrganizerDiagnostic> diagnostics, string? path, IReadOnlyDictionary<string, TypeSafety>? safety = null) : CSharpSyntaxRewriter
{
    public int MemberCount { get; private set; }
    public int RegionCount { get; private set; }
    public List<string> OrganizedTypes { get; } = [];
    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) => Organize((TypeDeclarationSyntax)base.VisitClassDeclaration(node)!, node);
    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) => Organize((TypeDeclarationSyntax)base.VisitStructDeclaration(node)!, node);
    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) => Organize((TypeDeclarationSyntax)base.VisitRecordDeclaration(node)!, node);
    public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => Organize((TypeDeclarationSyntax)base.VisitInterfaceDeclaration(node)!, node);

    private TypeDeclarationSyntax Organize(TypeDeclarationSyntax type, TypeDeclarationSyntax originalType)
    {
        if (type.Members.Count == 0) { return type; }
        var directTrivia = type.Members.SelectMany(m => m.GetLeadingTrivia().Concat(m.GetTrailingTrivia()))
            .Concat(type.OpenBraceToken.TrailingTrivia).Concat(type.CloseBraceToken.LeadingTrivia).ToArray();
        var directives = directTrivia.Where(t => t.IsDirective).ToArray();
        if (directives.Length > 0 && !config.Bool("regions.enabled")) { return Skip(type, "CSORG2002", "Existing regions protect member boundaries while region regeneration is disabled."); }
        if (directives.Any(t => t.GetStructure()!.DescendantTrivia().Any(child => child.IsKind(SyntaxKind.SingleLineCommentTrivia) || child.IsKind(SyntaxKind.MultiLineCommentTrivia)))) { return Skip(type, "CSORG2004", "Comments anchored to organizational directives protect this type."); }
        if (type.ContainsDiagnostics && type.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) { return Skip(type, "CSORG2001", "Parser errors in type; organization skipped."); }
        if (type.DescendantTrivia().Any(t => t.IsDirective && t.GetStructure() is not (RegionDirectiveTriviaSyntax or EndRegionDirectiveTriviaSyntax))
            || directTrivia.Any(t => t.IsKind(SyntaxKind.DisabledTextTrivia))) { return Skip(type, "CSORG2002", "Context-bearing or conditional directive protects this type."); }
        var depth = 0;
        foreach (var directive in directives.OrderBy(t => t.SpanStart))
        {
            depth += directive.IsKind(SyntaxKind.RegionDirectiveTrivia) ? 1 : -1;
            if (depth < 0) { return Skip(type, "CSORG2002", "Region crosses the type-member boundary."); }
        }
        if (depth != 0) { return Skip(type, "CSORG2002", "Unbalanced or cross-scope member regions."); }
        foreach (var member in type.Members)
        {
            var leading = WithoutRegions(member.GetLeadingTrivia()).ToFullString();
            if (Regex.IsMatch(leading, @"(?m)//[^\r\n]*\r?\n[\t ]*\r?\n|/\*[\s\S]*?\*/[\t ]*\r?\n[\t ]*\r?\n", RegexOptions.CultureInvariant)) { return Skip(type, "CSORG2004", "Ambiguous standalone section comment protects this type."); }
        }
        var members = type.Members.Select((m, i) => MemberDescriptor.Create(m, type, i, config)).ToArray();
        if (members.Any(m => m.Kind == "unsupported")) { return Skip(type, "CSORG2001", "Unsupported member declaration protects this type."); }
        MemberCount += members.Length;
        var typeKey = DeclarationIdentity.TypePart(originalType);
        var constraints = safety?.GetValueOrDefault(typeKey) ?? TypeSafety.FromSyntax(type);
        var pinAll = constraints.PinAll;
        var pinFields = constraints.PinFields;
        OrganizedTypes.Add(typeKey);
        if (pinAll || pinFields) { diagnostics.Add(new("CSORG2003", "Layout/interop-sensitive declarations retain their relative order.", path, type.SpanStart, type.Span.Length, Capability: "organize")); }
        var ordered = ConstrainedOrdering.Order(members, pinFields, pinAll, out var constrained);
        if (constrained) { diagnostics.Add(new("CSORG3001", "Safety constraints prevent the preferred unconstrained order.", path, type.SpanStart, type.Span.Length)); }
        var generate = config.Bool("regions.enabled");
        var newline = editor.NewLine;
        var indent = string.Concat(Enumerable.Repeat(editor.Indent, originalType.AncestorsAndSelf().Count(n => n is BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax) - (originalType.Ancestors().Any(n => n is FileScopedNamespaceDeclarationSyntax) ? 1 : 0)));
        // Prefer the declaration's own existing indentation where available.
        var output = new List<MemberDeclarationSyntax>();
        var pendingClose = "";
        for (var start = 0; start < ordered.Count;)
        {
            var end = start + 1;
            while (end < ordered.Count && ordered[end].Group == ordered[start].Group) { end++; }
            var region = generate && end - start >= config.GetNumber("regions.minMembers", 1) && (ordered[start].SortKeys.Length > 0 || ordered[start].Group < config.Node("organization.rules")!.AsArray().Count || config.Bool("regions.emitForUnmatched"));
            if (region) { RegionCount++; }
            for (var i = start; i < end; i++)
            {
                var member = ordered[i];
                var leading = WithoutRegions(member.Node.GetLeadingTrivia()).ToFullString().Trim('\r', '\n', ' ', '\t');
                var trailing = WithoutRegions(member.Node.GetTrailingTrivia()).ToFullString().TrimEnd('\r', '\n', ' ', '\t');
                var prefix = pendingClose;
                pendingClose = "";
                if (i == start)
                {
                    if (start > 0) { prefix += string.Concat(Enumerable.Repeat(newline, config.GetNumber("format.blankLines.betweenGroups", 1))); }
                    if (region) { prefix += indent + "#region " + member.Region + newline; }
                }
                else
                {
                    var key = member.Kind switch { "field" or "event_field" => "betweenFields", "property" or "indexer" => "betweenProperties", "constructor" or "static_constructor" => "betweenConstructors", _ => "betweenMethods" };
                    prefix += string.Concat(Enumerable.Repeat(newline, Math.Min(config.GetNumber("format.blankLines." + key, 1), config.GetNumber("format.blankLines.maxConsecutive", 1))));
                }
                prefix += indent + (leading.Length > 0 ? leading + newline + indent : "");
                var suffix = trailing + newline;
                if (i == end - 1 && region) { pendingClose = indent + "#endregion" + newline; }
                output.Add(member.Node.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(prefix)).WithTrailingTrivia(SyntaxFactory.ParseLeadingTrivia(suffix)));
            }
            start = end;
        }
        var openTrailing = WithoutRegions(type.OpenBraceToken.TrailingTrivia).ToFullString().TrimEnd('\r', '\n', ' ', '\t') + newline + string.Concat(Enumerable.Repeat(newline, config.GetNumber("format.blankLines.afterTypeOpenBrace", 0)));
        var closeLeading = WithoutRegions(type.CloseBraceToken.LeadingTrivia).ToFullString().Trim('\r', '\n', ' ', '\t');
        // Comments immediately before the closing brace remain at that boundary.
        closeLeading = pendingClose + string.Concat(Enumerable.Repeat(newline, config.GetNumber("format.blankLines.beforeTypeCloseBrace", 0))) + (closeLeading.Length > 0 ? closeLeading + newline : "") + indent[..Math.Max(0, indent.Length - editor.Indent.Length)];
        return type.WithOpenBraceToken(type.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.ParseTrailingTrivia(openTrailing)))
            .WithMembers(SyntaxFactory.List(output)).WithCloseBraceToken(type.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(closeLeading)));
    }

    private TypeDeclarationSyntax Skip(TypeDeclarationSyntax type, string id, string reason)
    {
        diagnostics.Add(new(id, reason, path, type.SpanStart, type.Span.Length, Capability: "organize"));
        return type;
    }

    private static SyntaxTriviaList WithoutRegions(SyntaxTriviaList trivia) => SyntaxFactory.TriviaList(trivia.Where(t => !t.IsKind(SyntaxKind.RegionDirectiveTrivia) && !t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)));
}
