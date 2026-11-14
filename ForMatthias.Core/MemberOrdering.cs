using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

public sealed record MemberDescriptor(MemberDeclarationSyntax Node, int OriginalIndex, string Kind, string Name, string Accessibility, string[] Modifiers, string TypeKind, string InterfaceIdentity, bool HasInitializer, bool IsStatic, int Group, string Region, string[] SortKeys)
{
    public static MemberDescriptor Create(MemberDeclarationSyntax node, TypeDeclarationSyntax type, int index, EffectiveConfig config)
    {
        var modifiers = node.Modifiers.Select(m => m.ValueText).ToArray();
        var isStatic = modifiers.Contains("static", StringComparer.Ordinal) || modifiers.Contains("const", StringComparer.Ordinal);
        var kind = node switch
        {
            FieldDeclarationSyntax => "field", EventFieldDeclarationSyntax => "event_field", EventDeclarationSyntax => "event_declaration",
            ConstructorDeclarationSyntax => isStatic ? "static_constructor" : "constructor", PropertyDeclarationSyntax => "property",
            IndexerDeclarationSyntax => "indexer", MethodDeclarationSyntax => "method", OperatorDeclarationSyntax => "operator",
            ConversionOperatorDeclarationSyntax => "conversion_operator", ClassDeclarationSyntax => "nested_class", StructDeclarationSyntax => "nested_struct",
            RecordDeclarationSyntax => "nested_record", InterfaceDeclarationSyntax => "nested_interface", EnumDeclarationSyntax => "nested_enum",
            DelegateDeclarationSyntax => "delegate", DestructorDeclarationSyntax => "finalizer", _ => "unsupported"
        };
        var name = node switch
        {
            BaseFieldDeclarationSyntax field => field.Declaration.Variables[0].Identifier.ValueText,
            BaseTypeDeclarationSyntax nested => nested.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax ev => ev.Identifier.ValueText,
            ConstructorDeclarationSyntax ctor => ctor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => destructor.Identifier.ValueText,
            DelegateDeclarationSyntax del => del.Identifier.ValueText,
            OperatorDeclarationSyntax op => op.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.WithoutTrivia().NormalizeWhitespace().ToFullString(),
            IndexerDeclarationSyntax => "this", _ => ""
        };
        var explicitInterface = node switch
        {
            MethodDeclarationSyntax m => m.ExplicitInterfaceSpecifier,
            BasePropertyDeclarationSyntax p => p.ExplicitInterfaceSpecifier,
            _ => null
        };
        var access = modifiers.Contains("public", StringComparer.Ordinal) ? "public"
            : modifiers.Contains("protected", StringComparer.Ordinal) && modifiers.Contains("internal", StringComparer.Ordinal) ? "protected_internal"
            : modifiers.Contains("private", StringComparer.Ordinal) && modifiers.Contains("protected", StringComparer.Ordinal) ? "private_protected"
            : modifiers.Contains("protected", StringComparer.Ordinal) ? "protected"
            : modifiers.Contains("internal", StringComparer.Ordinal) ? "internal"
            : modifiers.Contains("private", StringComparer.Ordinal) || explicitInterface is not null ? "private"
            : type is InterfaceDeclarationSyntax || kind is "operator" or "conversion_operator" ? "public" : "private";
        var typeKind = type switch { InterfaceDeclarationSyntax => "interface", StructDeclarationSyntax => "struct", RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record_struct" : "record", _ => "class" };
        var initializer = !modifiers.Contains("const", StringComparer.Ordinal) && node switch
        {
            BaseFieldDeclarationSyntax field => field.Declaration.Variables.Any(v => v.Initializer is not null),
            PropertyDeclarationSyntax p => p.Initializer is not null,
            _ => false
        };
        var rules = config.Node("organization.rules")!.AsArray();
        var descriptor = new MemberDescriptor(node, index, kind, name, access, modifiers, typeKind, explicitInterface?.Name.NormalizeWhitespace().ToFullString() ?? "", initializer, isStatic, rules.Count, config.GetText("organization.unmatched.region"), []);
        for (var rank = 0; rank < rules.Count; rank++)
        {
            var rule = rules[rank]!;
            if (Matches(descriptor, rule["match"]?.AsObject()))
            {
                return descriptor with { Group = rank, Region = rule["region"]!.GetValue<string>(), SortKeys = (rule["sortBy"] ?? config.Node("organization.sortBy"))!.AsArray().Select(n => n!.GetValue<string>()).ToArray() };
            }
        }
        return descriptor;
    }

    private static bool Matches(MemberDescriptor member, JsonObject? match)
    {
        if (match is null) { return true; }
        string[] Values(string key) => match[key]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];
        bool Contains(string key, string value) => match[key] is null || Values(key).Contains(value, StringComparer.Ordinal);
        return Contains("kind", member.Kind) && Contains("accessibility", member.Accessibility) && Contains("containingTypeKinds", member.TypeKind)
            && Values("modifiersAll").All(m => member.Modifiers.Contains(m, StringComparer.Ordinal))
            && (match["modifiersAny"] is null || Values("modifiersAny").Any(m => member.Modifiers.Contains(m, StringComparer.Ordinal)))
            && !Values("modifiersNone").Any(m => member.Modifiers.Contains(m, StringComparer.Ordinal))
            && (match["nameGlob"] is null || Globs.Match(member.Name, match["nameGlob"]!.GetValue<string>()))
            && (match["explicitInterface"] is null || match["explicitInterface"]!.GetValue<bool>() == (member.InterfaceIdentity.Length > 0));
    }
}

public sealed class MemberComparer : IComparer<MemberDescriptor>
{
    private static readonly string[] Access = ["public", "protected_internal", "protected", "internal", "private_protected", "private"];

    public int Compare(MemberDescriptor? x, MemberDescriptor? y)
    {
        if (x is null || y is null) { return x is null ? y is null ? 0 : -1 : 1; }
        var result = x.Group.CompareTo(y.Group);
        if (result != 0) { return result; }
        foreach (var key in x.SortKeys)
        {
            result = key switch
            {
                "accessibility" => Array.IndexOf(Access, x.Accessibility).CompareTo(Array.IndexOf(Access, y.Accessibility)),
                "static" => y.IsStatic.CompareTo(x.IsStatic),
                "name" => StringComparer.Ordinal.Compare(x.InterfaceIdentity + "." + x.Name, y.InterfaceIdentity + "." + y.Name),
                "signature" => CompareSignature(x.Node, y.Node),
                "original" => x.OriginalIndex.CompareTo(y.OriginalIndex), _ => 0
            };
            if (result != 0) { return result; }
            if (key == "original") { break; }
        }
        return x.OriginalIndex.CompareTo(y.OriginalIndex);
    }

    private static int CompareSignature(MemberDeclarationSyntax x, MemberDeclarationSyntax y)
    {
        static int Arity(MemberDeclarationSyntax node) => node is MethodDeclarationSyntax method ? method.Arity : 0;
        static SeparatedSyntaxList<ParameterSyntax> Parameters(MemberDeclarationSyntax node) => node switch
        {
            BaseMethodDeclarationSyntax m => m.ParameterList.Parameters,
            IndexerDeclarationSyntax i => i.ParameterList.Parameters,
            DelegateDeclarationSyntax d => d.ParameterList.Parameters, _ => default
        };
        var result = Arity(x).CompareTo(Arity(y));
        if (result != 0) { return result; }
        var left = Parameters(x); var right = Parameters(y);
        result = left.Count.CompareTo(right.Count);
        if (result != 0) { return result; }
        for (var i = 0; i < left.Count; i++)
        {
            result = StringComparer.Ordinal.Compare(left[i].Type?.WithoutTrivia().NormalizeWhitespace().ToFullString(), right[i].Type?.WithoutTrivia().NormalizeWhitespace().ToFullString());
            if (result != 0) { return result; }
        }
        static string Passing(ParameterSyntax p)
        {
            var mods = p.Modifiers.Select(m => m.ValueText).ToArray();
            var rank = mods.Contains("ref", StringComparer.Ordinal) && mods.Contains("readonly", StringComparer.Ordinal) ? 4 : mods.Contains("out", StringComparer.Ordinal) ? 3 : mods.Contains("ref", StringComparer.Ordinal) ? 2 : mods.Contains("in", StringComparer.Ordinal) ? 1 : 0;
            return rank + ":" + string.Join(" ", mods);
        }
        for (var i = 0; i < left.Count; i++)
        {
            result = StringComparer.Ordinal.Compare(Passing(left[i]), Passing(right[i]));
            if (result != 0) { return result; }
        }
        return 0;
    }
}

public static class ConstrainedOrdering
{
    public static IReadOnlyList<MemberDescriptor> Order(IReadOnlyList<MemberDescriptor> members, bool pinFields, bool pinAll, out bool constrained)
    {
        var edges = members.Select(_ => new SortedSet<int>()).ToArray();
        var degrees = new int[members.Count];
        void Sequence(IEnumerable<MemberDescriptor> sequence)
        {
            MemberDescriptor? previous = null;
            foreach (var member in sequence)
            {
                if (previous is not null && edges[previous.OriginalIndex].Add(member.OriginalIndex)) { degrees[member.OriginalIndex]++; }
                previous = member;
            }
        }
        Sequence(members.Where(m => m.HasInitializer && m.IsStatic));
        Sequence(members.Where(m => m.HasInitializer && !m.IsStatic));
        if (pinFields) { Sequence(members.Where(m => !m.IsStatic && m.Kind is "field" or "event_field" or "property")); }
        if (pinAll) { Sequence(members); }
        var comparer = new MemberComparer();
        var ready = new PriorityQueue<MemberDescriptor, MemberDescriptor>(comparer);
        foreach (var member in members.Where(m => degrees[m.OriginalIndex] == 0)) { ready.Enqueue(member, member); }
        var ordered = new List<MemberDescriptor>();
        while (ready.TryDequeue(out var next, out _))
        {
            ordered.Add(next);
            foreach (var successor in edges[next.OriginalIndex])
            {
                if (--degrees[successor] == 0) { ready.Enqueue(members[successor], members[successor]); }
            }
        }
        if (ordered.Count != members.Count) { throw new OrganizerException(4, "CSORG6002", "Ordering constraint cycle."); }
        var positions = ordered.Select((member, i) => (member.OriginalIndex, i)).ToDictionary(x => x.OriginalIndex, x => x.i);
        if (edges.SelectMany((targets, from) => targets.Select(to => (from, to))).Any(edge => positions[edge.from] >= positions[edge.to])) { throw new OrganizerException(4, "CSORG6002", "Ordering constraint violation."); }
        constrained = !ordered.SequenceEqual(members.OrderBy(m => m, comparer));
        return ordered;
    }
}
