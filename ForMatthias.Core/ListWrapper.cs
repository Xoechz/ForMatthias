using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ForMatthias.Core;

public static class ListWrapper
{
    public static async Task<(string Text, int Wraps)> ApplyAsync(string text, CSharpParseOptions options, EffectiveConfig config, EditorSettings editor, List<OrganizerDiagnostic> diagnostics, string? path,
        IReadOnlyList<Document> contexts, CancellationToken ct)
    {
        var maximum = config.GetNumber("wrapping.maxLineLength", editor.LineLength);
        var wraps = 0;
        var limit = Math.Max(32, text.Length / 2);
        for (var iteration = 0; iteration < limit; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var invocations = await ClassifyInvocationsAsync(text, config, contexts, ct);
            var tree = CSharpSyntaxTree.ParseText(text, options, cancellationToken: ct);
            var source = tree.GetText(ct);
            var changed = false;
            foreach (var list in tree.GetRoot(ct).DescendantNodes().Where(n => n is ArgumentListSyntax or ParameterListSyntax).OrderBy(n => n.SpanStart).ThenByDescending(n => n.Span.Length))
            {
                var items = list is ArgumentListSyntax arguments ? arguments.Arguments.Cast<SyntaxNode>().ToArray() : ((ParameterListSyntax)list).Parameters.Cast<SyntaxNode>().ToArray();
                if (items.Length == 0) { continue; }
                var firstLine = source.Lines.GetLineFromPosition(list.SpanStart);
                var lastLine = source.Lines.GetLineFromPosition(list.Span.End);
                var overflowing = source.Lines.Skip(firstLine.LineNumber).Take(lastLine.LineNumber - firstLine.LineNumber + 1).Any(line => Width(line.ToString(), editor.TabWidth) > maximum);
                if (!overflowing) { continue; }
                if (!Enabled(list, config, invocations, out var ambiguous))
                {
                    if (ambiguous && !diagnostics.Any(d => d.Id == "CSORG4002" && d.Start == list.SpanStart)) { diagnostics.Add(new("CSORG4002", "Invocation target is ambiguous without consistent semantic binding; independent method/delegate target selection skipped this list.", path, list.SpanStart, list.Span.Length, Capability: "wrap")); }
                    continue;
                }
                if (list.ContainsDiagnostics || list.ContainsDirectives || list.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) || t.IsKind(SyntaxKind.DisabledTextTrivia))
                    || list.DescendantTokens().Any(IsRaw))
                {
                    Warn("Protected comments, directives, or raw string prevent safe list wrapping.", list); continue;
                }
                var lineText = firstLine.ToString();
                var leading = new string(lineText.TakeWhile(c => c is ' ' or '\t').ToArray());
                var indentation = editor.ContinueIndent(leading, config.GetNumber("wrapping.continuationIndentLevels", 1));
                var builder = new StringBuilder("(").Append(editor.NewLine);
                for (var index = 0; index < items.Length; index++)
                {
                    builder.Append(indentation).Append(items[index].ToString().Trim());
                    if (index + 1 < items.Length) { builder.Append(',').Append(editor.NewLine); }
                }
                builder.Append(')');
                var replacement = builder.ToString();
                if (replacement == list.ToString()) { continue; }
                // Breaking after '(' must actually shorten the overflowing source line.
                if (firstLine == lastLine && Width(lineText[..(list.SpanStart - firstLine.Start + 1)], editor.TabWidth) >= Width(lineText, editor.TabWidth)) { continue; }
                text = source.WithChanges(new TextChange(list.Span, replacement)).ToString();
                wraps++; changed = true; break;
            }
            if (!changed) { break; }
        }
        var finalTree = CSharpSyntaxTree.ParseText(text, options, cancellationToken: ct);
        var finalInvocations = await ClassifyInvocationsAsync(text, config, contexts, ct);
        foreach (var list in finalTree.GetRoot(ct).DescendantNodes().Where(n => n is ArgumentListSyntax or ParameterListSyntax && Enabled(n, config, finalInvocations, out _)))
        {
            var source = finalTree.GetText(ct);
            var first = source.Lines.GetLineFromPosition(list.SpanStart).LineNumber;
            var last = source.Lines.GetLineFromPosition(list.Span.End).LineNumber;
            if (source.Lines.Skip(first).Take(last - first + 1).Any(line => Width(line.ToString(), editor.TabWidth) > maximum)) { Warn("An atomic expression or protected list still exceeds the line limit.", list); }
        }
        return (text, wraps);

        void Warn(string message, SyntaxNode node)
        {
            if (!diagnostics.Any(d => d.Id == "CSORG4001" && d.Start == node.SpanStart)) { diagnostics.Add(new("CSORG4001", message, path, node.SpanStart, node.Span.Length, Capability: "wrap")); }
        }
    }

    public static int Width(string text, int tabWidth)
    {
        var column = 0;
        foreach (var rune in text.EnumerateRunes()) { column = rune.Value == '\t' ? column + tabWidth - column % tabWidth : column + 1; }
        return column;
    }

    public static bool IsRaw(SyntaxToken token) => token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
        || token.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken) || token.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);

    private enum InvocationTarget { Unknown, Method, Delegate }

    private static async Task<IReadOnlyDictionary<int, InvocationTarget>> ClassifyInvocationsAsync(string text, EffectiveConfig config, IReadOnlyList<Document> contexts, CancellationToken ct)
    {
        var result = new Dictionary<int, InvocationTarget>();
        if (config.Bool("wrapping.targets.methodCalls") == config.Bool("wrapping.targets.delegateInvocations")) { return result; }
        var first = true;
        foreach (var context in contexts)
        {
            var document = context.WithText(SourceText.From(text));
            var root = (await document.GetSyntaxRootAsync(ct))!;
            var model = await document.GetSemanticModelAsync(ct);
            var current = new Dictionary<int, InvocationTarget>();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = model?.GetSymbolInfo(invocation, ct).Symbol as IMethodSymbol;
                current[invocation.ArgumentList.SpanStart] = method is null ? InvocationTarget.Unknown : method.MethodKind == MethodKind.DelegateInvoke ? InvocationTarget.Delegate : InvocationTarget.Method;
            }
            if (first) { foreach (var pair in current) { result[pair.Key] = pair.Value; } first = false; }
            else { foreach (var key in result.Keys.ToArray()) { if (!current.TryGetValue(key, out var kind) || result[key] != kind) { result[key] = InvocationTarget.Unknown; } } }
        }
        return result;
    }

    private static bool Enabled(SyntaxNode list, EffectiveConfig config, IReadOnlyDictionary<int, InvocationTarget> invocations, out bool ambiguous)
    {
        ambiguous = false;
        if (list.Parent is InvocationExpressionSyntax)
        {
            var methods = config.Bool("wrapping.targets.methodCalls");
            var delegates = config.Bool("wrapping.targets.delegateInvocations");
            if (methods == delegates) { return methods; }
            var target = invocations.GetValueOrDefault(list.SpanStart);
            ambiguous = target == InvocationTarget.Unknown;
            return target switch { InvocationTarget.Method => methods, InvocationTarget.Delegate => delegates, _ => false };
        }
        return list.Parent switch
    {
        ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => config.Bool("wrapping.targets.constructorCalls"),
        ConstructorInitializerSyntax => config.Bool("wrapping.targets.constructorInitializers"),
        ConstructorDeclarationSyntax => config.Bool("wrapping.targets.constructorDeclarations"),
        MethodDeclarationSyntax => config.Bool("wrapping.targets.methodDeclarations"),
        LocalFunctionStatementSyntax => config.Bool("wrapping.targets.localFunctions"),
        RecordDeclarationSyntax => config.Bool("wrapping.targets.recordPrimaryConstructors"),
        ParenthesizedLambdaExpressionSyntax => config.Bool("wrapping.targets.lambdaParameters"),
        _ => false
    };
    }
}
