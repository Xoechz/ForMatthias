using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

public static class CandidateValidator
{
    public static void Validate(string original, string candidate, CSharpParseOptions options, IReadOnlyDictionary<string, TypeSafety>? safety = null,
        EffectiveConfig? config = null, IEnumerable<string>? organizedTypes = null)
    {
        var before = CSharpSyntaxTree.ParseText(original, options).GetRoot();
        var after = CSharpSyntaxTree.ParseText(candidate, options).GetRoot();
        var errors = Counts(before.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + ":" + d.GetMessage(CultureInfo.InvariantCulture)));
        foreach (var (key, count) in Counts(after.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + ":" + d.GetMessage(CultureInfo.InvariantCulture))))
        {
            if (count > errors.GetValueOrDefault(key)) { Fail("New syntax error: " + key); }
        }
        // Ordinary declaration tokens may move but may not be changed, lost, or duplicated.
        static IEnumerable<string> Tokens(SyntaxNode root) => root.DescendantTokens().Where(t => !t.Parent!.AncestorsAndSelf().Any(n => n is UsingDirectiveSyntax)).Select(t => t.RawKind + ":" + t.Text);
        EqualCounts(Tokens(before), Tokens(after), "Non-import token ledger changed.");
        static IEnumerable<string> Comments(SyntaxNode root) => root.DescendantTrivia(descendIntoTrivia: true).Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)).Select(t => t.ToFullString());
        EqualCounts(Comments(before), Comments(after), "Comment or documentation text changed.");
        static IEnumerable<string> Directives(SyntaxNode root) => root.DescendantTrivia().Where(t => t.IsDirective && !t.IsKind(SyntaxKind.RegionDirectiveTrivia) && !t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)).Select(t => t.ToFullString().TrimEnd('\r', '\n'));
        if (!Directives(before).SequenceEqual(Directives(after), StringComparer.Ordinal)) { Fail("Context-bearing directive ledger changed."); }
        // Ownership includes the entire declaration's token identity, ignoring only whitespace/managed regions.
        static IEnumerable<string> Ownership(SyntaxNode root) => root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(n => n is not BaseNamespaceDeclarationSyntax && n is not TypeDeclarationSyntax && n is not EnumDeclarationSyntax && n is not EnumMemberDeclarationSyntax)
            .Select(n => string.Join("|", n.DescendantTokens().Select(t => t.Text)) + "::" + string.Join("|", Comments(n)));
        EqualCounts(Ownership(before), Ownership(after), "Comment/declaration ownership changed.");
        static IEnumerable<string> Enums(SyntaxNode root) => root.DescendantNodes().OfType<EnumDeclarationSyntax>().Select(n => string.Join("|", n.DescendantTokens().Select(t => t.Text)));
        EqualCounts(Enums(before), Enums(after), "Enum declaration or entry order changed.");
        ValidateTypeSequences(before, after, safety);
        ValidateOwnership(before, after);
        ValidateProtectedRegions(before, after, organizedTypes?.ToHashSet(StringComparer.Ordinal) ?? []);
        if (config is not null && organizedTypes is not null)
        {
            foreach (var type in after.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t => organizedTypes.Contains(DeclarationIdentity.TypePart(t), StringComparer.Ordinal))) { ValidateRegions(type, config); }
        }
    }

    public static void ValidateImports(string approvedSource, string candidate, CSharpParseOptions options, bool allowDuplicateRemoval)
    {
        static IEnumerable<string> Imports(SyntaxNode root) => root.DescendantNodes().OfType<UsingDirectiveSyntax>().Select(u =>
            DeclarationIdentity.Scope(u.Parent) + "::" + string.Join("|", u.DescendantTokens().Select(t => t.Text)));
        var expected = Counts(Imports(CSharpSyntaxTree.ParseText(approvedSource, options).GetRoot()));
        var actual = Counts(Imports(CSharpSyntaxTree.ParseText(candidate, options).GetRoot()));
        if (actual.Keys.Any(key => !expected.ContainsKey(key)) || expected.Any(pair => actual.GetValueOrDefault(pair.Key) != pair.Value && !(allowDuplicateRemoval && pair.Value > 1 && actual.GetValueOrDefault(pair.Key) >= 1))) { Fail("Unapproved import insertion, modification, or removal."); }
    }

    public static void ValidateOrderingSafety(string original, string candidate, CSharpParseOptions options, IReadOnlyDictionary<string, TypeSafety> safety) =>
        ValidateTypeSequences(CSharpSyntaxTree.ParseText(original, options).GetRoot(), CSharpSyntaxTree.ParseText(candidate, options).GetRoot(), safety);

    private static void ValidateTypeSequences(SyntaxNode before, SyntaxNode after, IReadOnlyDictionary<string, TypeSafety>? safety)
    {
        static string TypeId(TypeDeclarationSyntax type) => DeclarationIdentity.TypePart(type);
        static string MemberId(MemberDeclarationSyntax node) => string.Join("|", node.DescendantTokens().Select(t => t.Text));
        var available = after.DescendantNodes().OfType<TypeDeclarationSyntax>().GroupBy(TypeId).ToDictionary(g => g.Key, g => new Queue<TypeDeclarationSyntax>(g), StringComparer.Ordinal);
        foreach (var type in before.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!available.TryGetValue(TypeId(type), out var matches) || matches.Count == 0) { Fail("Type declaration lost."); }
            var candidate = matches!.Dequeue();
            var left = type.Members.Select((m, i) => MemberDescriptor.Create(m, type, i, EffectiveConfig.Default)).ToArray();
            var right = candidate.Members.Select((m, i) => MemberDescriptor.Create(m, candidate, i, EffectiveConfig.Default)).ToArray();
            foreach (var isStatic in new[] { true, false })
            {
                if (!left.Where(m => m.HasInitializer && m.IsStatic == isStatic).Select(m => MemberId(m.Node)).SequenceEqual(right.Where(m => m.HasInitializer && m.IsStatic == isStatic).Select(m => MemberId(m.Node)), StringComparer.Ordinal)) { Fail("Initializer execution order changed."); }
            }
            var constraints = safety?.GetValueOrDefault(TypeId(type)) ?? TypeSafety.FromSyntax(type);
            if (constraints.PinFields || constraints.PinAll)
            {
                bool Protected(MemberDescriptor m) => constraints.PinAll || !m.IsStatic && m.Kind is "field" or "event_field" or "property";
                if (!left.Where(Protected).Select(m => MemberId(m.Node)).SequenceEqual(right.Where(Protected).Select(m => MemberId(m.Node)), StringComparer.Ordinal)) { Fail("Layout/interop declaration order changed."); }
            }
        }
    }

    private static void ValidateOwnership(SyntaxNode before, SyntaxNode after)
    {
        static IEnumerable<SyntaxNode> Containers(SyntaxNode root) => root.DescendantNodesAndSelf().Where(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax);
        static IEnumerable<string> Comments(SyntaxNode node) => node.DescendantTrivia(descendIntoTrivia: true).Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)).Select(t => t.ToFullString());
        static string MemberIdentity(MemberDeclarationSyntax member)
        {
            if (member is TypeDeclarationSyntax or BaseNamespaceDeclarationSyntax)
            {
                return DeclarationIdentity.Header(member) + ":" + member.GetLeadingTrivia().ToFullString().Trim() + ":" + member.GetTrailingTrivia().ToFullString().Trim();
            }
            return DeclarationIdentity.Tokens(member) + ":" + string.Join("|", Comments(member));
        }
        // Region and whitespace trivia can change on a type header; comment ownership cannot.
        static string Identity(MemberDeclarationSyntax member) => member is TypeDeclarationSyntax or BaseNamespaceDeclarationSyntax
            ? DeclarationIdentity.Header(member) + ":" + string.Join("|", member.DescendantTrivia(descendIntoTrivia: true)
                .Where(t => t.Token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault() == member)
                .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)).Select(t => t.ToFullString()))
            : MemberIdentity(member);
        var afterScopes = Containers(after).ToDictionary(DeclarationIdentity.Scope, StringComparer.Ordinal);
        var beforeScopes = Containers(before).ToDictionary(DeclarationIdentity.Scope, StringComparer.Ordinal);
        if (!beforeScopes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(afterScopes.Keys)) { Fail("Containing namespace/type/partial-part scope changed."); }
        foreach (var (key, scope) in beforeScopes)
        {
            var candidate = afterScopes[key];
            EqualCounts(DeclarationIdentity.Members(scope).Select(Identity), DeclarationIdentity.Members(candidate).Select(Identity), "Declaration ownership changed in " + key);
            // Top-level execution order is never an organization target.
            if (!DeclarationIdentity.Members(scope).OfType<GlobalStatementSyntax>().Select(DeclarationIdentity.Tokens).SequenceEqual(DeclarationIdentity.Members(candidate).OfType<GlobalStatementSyntax>().Select(DeclarationIdentity.Tokens), StringComparer.Ordinal)) { Fail("Top-level statement order changed."); }
        }
        static IEnumerable<string> Contexts(SyntaxNode root)
        {
            var directives = root.DescendantTrivia().Where(t => t.IsDirective && !t.IsKind(SyntaxKind.RegionDirectiveTrivia) && !t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)).OrderBy(t => t.SpanStart).ToArray();
            foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                yield return DeclarationIdentity.Origin(declaration) + "::" + string.Join("|", directives.Where(t => t.SpanStart < declaration.SpanStart).Select(t => t.ToFullString().TrimEnd('\r', '\n')));
            }
        }
        EqualCounts(Contexts(before), Contexts(after), "Declaration crossed a compiler-directive context boundary.");
        static IEnumerable<string> TokenContexts(SyntaxNode root)
        {
            var directives = root.DescendantTrivia().Where(t => t.IsDirective && !t.IsKind(SyntaxKind.RegionDirectiveTrivia) && !t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)).OrderBy(t => t.SpanStart).ToArray();
            var ordinal = new Dictionary<MemberDeclarationSyntax, int>();
            var identities = new Dictionary<MemberDeclarationSyntax, string>();
            var state = "";
            var next = 0;
            foreach (var token in root.DescendantTokens().Where(t => !t.Parent!.AncestorsAndSelf().Any(n => n is UsingDirectiveSyntax)))
            {
                while (next < directives.Length && directives[next].SpanStart < token.SpanStart) { state += "|" + directives[next++].ToFullString().TrimEnd('\r', '\n'); }
                var owner = token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
                var index = owner is null ? 0 : ordinal.GetValueOrDefault(owner);
                if (owner is not null) { ordinal[owner] = index + 1; }
                if (owner is not null && !identities.ContainsKey(owner)) { identities[owner] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DeclarationIdentity.Origin(owner)))); }
                yield return (owner is null ? "$" : identities[owner]) + ":" + index + ":" + token.RawKind + ":" + token.Text + "::" + state;
            }
        }
        EqualCounts(TokenContexts(before), TokenContexts(after), "Token crossed a compiler-directive context boundary.");
        EqualCounts(before.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.DisabledTextTrivia)).Select(t => t.ToFullString()), after.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.DisabledTextTrivia)).Select(t => t.ToFullString()), "Disabled source text changed.");
    }

    private static void ValidateRegions(TypeDeclarationSyntax type, EffectiveConfig config)
    {
        var expected = new List<string>();
        var descriptors = type.Members.Select((m, i) => MemberDescriptor.Create(m, type, i, config)).ToArray();
        if (config.Bool("regions.enabled"))
        {
            for (var start = 0; start < descriptors.Length;)
            {
                var end = start + 1;
                while (end < descriptors.Length && descriptors[end].Group == descriptors[start].Group) { end++; }
                var matched = descriptors[start].Group < config.Node("organization.rules")!.AsArray().Count;
                if (end - start >= config.GetNumber("regions.minMembers", 1) && (matched || config.Bool("regions.emitForUnmatched"))) { expected.Add(descriptors[start].Region + ":" + start + ":" + end); }
                start = end;
            }
        }
        var directives = type.Members.SelectMany(m => m.GetLeadingTrivia().Concat(m.GetTrailingTrivia())).Concat(type.OpenBraceToken.TrailingTrivia).Concat(type.CloseBraceToken.LeadingTrivia)
            .Where(t => t.IsKind(SyntaxKind.RegionDirectiveTrivia) || t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)).OrderBy(t => t.SpanStart).ToArray();
        var actual = new List<string>();
        SyntaxTrivia? opening = null;
        foreach (var directive in directives)
        {
            if (directive.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                if (opening is not null) { Fail("Generated organizational regions must be flat contiguous runs."); }
                opening = directive;
            }
            else
            {
                if (opening is null) { Fail("Generated region has no opening directive."); }
                var members = descriptors.Where(m => m.Node.SpanStart > opening!.Value.Span.End && m.Node.Span.End <= directive.SpanStart).ToArray();
                if (members.Length == 0) { Fail("Generated region is empty or crosses a member boundary."); }
                var label = opening!.Value.GetStructure()!.ToString()["#region".Length..].Trim();
                actual.Add(label + ":" + members[0].OriginalIndex + ":" + (members[^1].OriginalIndex + 1));
                opening = null;
            }
        }
        if (opening is not null || !expected.SequenceEqual(actual, StringComparer.Ordinal)) { Fail("Generated regions do not match configured contiguous rule runs."); }
    }

    private static void ValidateProtectedRegions(SyntaxNode before, SyntaxNode after, HashSet<string> organizedTypes)
    {
        static IEnumerable<SyntaxTrivia> DirectRegions(TypeDeclarationSyntax type) => type.Members.SelectMany(m => m.GetLeadingTrivia().Concat(m.GetTrailingTrivia()))
            .Concat(type.OpenBraceToken.TrailingTrivia).Concat(type.CloseBraceToken.LeadingTrivia).Where(t => t.IsKind(SyntaxKind.RegionDirectiveTrivia) || t.IsKind(SyntaxKind.EndRegionDirectiveTrivia));
        IEnumerable<string> Protected(SyntaxNode root)
        {
            var managed = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t => organizedTypes.Contains(DeclarationIdentity.TypePart(t))).SelectMany(DirectRegions).Select(t => t.SpanStart).ToHashSet();
            foreach (var directive in root.DescendantTrivia().Where(t => (t.IsKind(SyntaxKind.RegionDirectiveTrivia) || t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)) && !managed.Contains(t.SpanStart)))
            {
                var declaration = directive.Token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
                var owner = declaration is null ? "$" : DeclarationIdentity.Origin(declaration);
                var boundary = declaration?.DescendantTokens().Count(t => t.SpanStart < directive.SpanStart) ?? 0;
                yield return owner + ":" + boundary + "::" + directive.ToFullString().TrimEnd('\r', '\n');
            }
        }
        EqualCounts(Protected(before), Protected(after), "Unmanaged region ownership or content changed.");
    }

    private static Dictionary<string, int> Counts(IEnumerable<string> values) => values.GroupBy(v => v, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    private static void EqualCounts(IEnumerable<string> before, IEnumerable<string> after, string message)
    {
        var left = Counts(before); var right = Counts(after);
        if (left.Count != right.Count || left.Any(pair => right.GetValueOrDefault(pair.Key) != pair.Value)) { Fail(message); }
    }
    private static void Fail(string message) => throw new OrganizerException(4, "CSORG6002", message);
}
