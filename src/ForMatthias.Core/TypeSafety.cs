using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForMatthias.Core;

public sealed record TypeSafety(bool PinFields = false, bool PinAll = false)
{
    public TypeSafety Union(TypeSafety other) => new(PinFields || other.PinFields, PinAll || other.PinAll);

    public static TypeSafety FromSyntax(TypeDeclarationSyntax type, bool semanticContext = false)
    {
        // In syntax mode other partial declarations are unknown, so their attributes cannot be ruled out.
        var unknownPartial = !semanticContext && type.Modifiers.Any(SyntaxKind.PartialKeyword);
        var attributed = type.AttributeLists.Count > 0;
        return new(type is StructDeclarationSyntax || type is RecordDeclarationSyntax r && r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
            || attributed || unknownPartial || type.Members.Any(m => m.AttributeLists.Count > 0 || m.Modifiers.Any(SyntaxKind.FixedKeyword)),
            type is InterfaceDeclarationSyntax && (attributed || unknownPartial));
    }
}

public static class TypeSafetyAnalysis
{
    public static async Task<ImmutableDictionary<string, TypeSafety>> ResolveAsync(Document document, IEnumerable<Document>? contexts, CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;
        var documents = contexts?.ToArray() ?? [];
        var result = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToDictionary(DeclarationIdentity.TypePart, t => TypeSafety.FromSyntax(t, documents.Length > 0), StringComparer.Ordinal);
        foreach (var context in documents)
        {
            var contextRoot = (await context.GetSyntaxRootAsync(ct))!;
            var model = await context.GetSemanticModelAsync(ct) ?? throw new OrganizerException(3, "CSORG5001", "Semantic safety context is unavailable: " + context.FilePath);
            foreach (var type in contextRoot.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var key = DeclarationIdentity.TypePart(type);
                if (!result.TryGetValue(key, out var safety)) { continue; }
                var symbol = model.GetDeclaredSymbol(type, ct);
                if (symbol is null)
                {
                    result[key] = safety.Union(TypeSafety.FromSyntax(type));
                    continue;
                }
                // GetAttributes returns attributes of the combined symbol, including every partial part.
                var attributes = symbol.GetAttributes();
                var layout = attributes.Any(a => a.AttributeClass?.ToDisplayString() is "System.Runtime.InteropServices.StructLayoutAttribute" or "System.Runtime.CompilerServices.InlineArrayAttribute");
                var fields = symbol.GetMembers().OfType<IFieldSymbol>().Any(f => f.IsFixedSizeBuffer || f.GetAttributes().Length > 0);
                var interop = symbol.TypeKind == TypeKind.Interface && (symbol.IsComImport || attributes.Length > 0
                    || symbol.AllInterfaces.Any(i => i.IsComImport || i.GetAttributes().Any(a => a.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.InteropServices")));
                result[key] = safety.Union(new TypeSafety(layout || attributes.Length > 0 || fields, interop));
            }
        }
        return result.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
