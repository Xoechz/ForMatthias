using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

/// <summary>Physical declaration-part identities independent of sibling member order and whitespace.</summary>
public static class DeclarationIdentity
{
    public static string Tokens(SyntaxNode node) => string.Join("|", node.DescendantTokens().Select(t => t.RawKind + ":" + t.Text));

    public static string Header(MemberDeclarationSyntax member) => member switch
    {
        TypeDeclarationSyntax type => string.Join("|", type.DescendantTokens().TakeWhile(t => t != type.OpenBraceToken && t != type.SemicolonToken).Select(t => t.RawKind + ":" + t.Text)),
        EnumDeclarationSyntax type => string.Join("|", type.DescendantTokens().TakeWhile(t => t != type.OpenBraceToken).Select(t => t.RawKind + ":" + t.Text)),
        BaseNamespaceDeclarationSyntax ns => ns.RawKind + ":" + Tokens(ns.Name),
        _ => Tokens(member)
    };

    public static string Scope(SyntaxNode? node)
    {
        if (node is null or CompilationUnitSyntax) { return "$"; }
        if (node is TypeDeclarationSyntax type) { return TypePart(type); }
        if (node is BaseNamespaceDeclarationSyntax ns)
        {
            var peers = Members(ns.Parent).OfType<BaseNamespaceDeclarationSyntax>().Where(n => n.RawKind == ns.RawKind && Tokens(n.Name) == Tokens(ns.Name));
            return Scope(ns.Parent) + "/namespace:" + Tokens(ns.Name) + "#" + peers.TakeWhile(n => n != ns).Count();
        }
        return Scope(node.Parent);
    }

    public static string TypePart(TypeDeclarationSyntax type)
    {
        var name = type.RawKind + ":" + type.Identifier.ValueText + "`" + (type.TypeParameterList?.Parameters.Count ?? 0);
        var peers = Members(type.Parent).OfType<TypeDeclarationSyntax>().Where(t => t.RawKind == type.RawKind && t.Identifier.ValueText == type.Identifier.ValueText && t.TypeParameterList?.Parameters.Count == type.TypeParameterList?.Parameters.Count);
        return Scope(type.Parent) + "/type:" + name + "#" + peers.TakeWhile(t => t != type).Count();
    }

    public static string Origin(MemberDeclarationSyntax declaration) => declaration is TypeDeclarationSyntax type
        ? TypePart(type) + ":" + Header(type)
        : Scope(declaration.Parent) + ":" + Header(declaration);

    public static IEnumerable<MemberDeclarationSyntax> Members(SyntaxNode? container) => container switch
    {
        CompilationUnitSyntax unit => unit.Members,
        BaseNamespaceDeclarationSyntax ns => ns.Members,
        TypeDeclarationSyntax type => type.Members,
        _ => []
    };
}
