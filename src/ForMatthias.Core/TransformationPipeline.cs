using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace ForMatthias.Core;

public sealed class TransformationPipeline : ITransformationPipeline
{
    public async Task<TransformResult> TransformAsync(TransformRequest request, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<OrganizerDiagnostic>();
        var document = request.Document;
        var original = (await document.GetTextAsync(cancellationToken)).ToString();
        var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
        var config = request.Config;
        var editor = request.Editor;
        var members = 0; var regions = 0; var wraps = 0;
        var contexts = request.HasSemanticContext ? request.SemanticContexts.IsDefaultOrEmpty ? [document] : request.SemanticContexts.ToArray() : Array.Empty<Document>();
        var safety = await TypeSafetyAnalysis.ResolveAsync(document, contexts, cancellationToken);
        var organizedTypes = ImmutableArray<string>.Empty;
        bool Enabled(string capability, string section) => request.Capabilities.Contains(capability) && config.Bool(section + ".enabled");
        if (Enabled("cleanup", "cleanup"))
        {
            root = new ImportOrganizer(config, editor).Visit(root)!;
            if (config.Bool("cleanup.removeUnusedUsings") && !request.HasSemanticContext)
            {
                diagnostics.Add(new("CSORG5002", "Unused-using removal requires semantic project context; skipped.", document.FilePath, Capability: "cleanup.unusedUsings"));
            }
        }
        if (Enabled("organize", "organization"))
        {
            var organizer = new MemberOrganizer(config, editor, diagnostics, document.FilePath, safety);
            root = organizer.Visit(root)!;
            members = organizer.MemberCount; regions = organizer.RegionCount;
            organizedTypes = [.. organizer.OrganizedTypes];
        }
        // Reparse generated directive trivia into its canonical token ownership before formatting.
        document = document.WithText(SourceText.From(root.ToFullString()));
        if (Enabled("format", "format"))
        {
            Microsoft.CodeAnalysis.Options.OptionSet options = await document.GetOptionsAsync(cancellationToken);
            options = options.WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, editor.UseTabs)
                .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, editor.TabWidth)
                .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, editor.IndentSize)
                .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, editor.NewLine);
            if (config.Bool("wrapping.preserveExistingMultiline") || Enabled("wrap", "wrapping"))
            {
                document = document.WithText(SourceText.From(await FormatPreservingBreaksAsync(document, editor, config, false, cancellationToken)));
            }
            else
            {
                document = await Formatter.FormatAsync(document, options, cancellationToken);
            }
            var formattedRoot = (await document.GetSyntaxRootAsync(cancellationToken))!;
            document = document.WithSyntaxRoot(new MemberSpacing(config, editor).Visit(formattedRoot)!);
        }
        var text = (await document.GetTextAsync(cancellationToken)).ToString();
        var parseOptions = (CSharpParseOptions)document.Project.ParseOptions!;
        if (Enabled("wrap", "wrapping"))
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var wrapped = await ListWrapper.ApplyAsync(text, parseOptions, config, editor, diagnostics, document.FilePath, contexts, cancellationToken);
                text = wrapped.Text; wraps += wrapped.Wraps;
                if (!Enabled("format", "format") || wrapped.Wraps == 0) { break; }
                // Wrapping can make an enclosing block multiline. Normalize affected spans and
                // re-evaluate overflow after indentation changes, without collapsing breakpoints.
                document = document.WithText(SourceText.From(text));
                text = await FormatPreservingBreaksAsync(document, editor, config, true, cancellationToken);
                if (iteration == 15) { throw new OrganizerException(4, "CSORG6003", "Wrapping and targeted formatting did not reach a fixed point."); }
            }
        }
        if (Enabled("cleanup", "cleanup") && config.Bool("cleanup.trimTrailingWhitespace")) { text = TrimWhitespace(text, parseOptions); }
        if (Enabled("format", "format") || Enabled("organize", "organization"))
        {
            if (editor.Values.GetValueOrDefault("insert_final_newline") == "true" || (original.EndsWith('\n') || original.EndsWith('\r')) && editor.Values.GetValueOrDefault("insert_final_newline") != "false")
            {
                if (!text.EndsWith('\n') && !text.EndsWith('\r')) { text += editor.NewLine; }
            }
            else if (editor.Values.GetValueOrDefault("insert_final_newline") == "false") { text = text.TrimEnd('\r', '\n'); }
        }
        CandidateValidator.ValidateImports(original, text, parseOptions, Enabled("cleanup", "cleanup") && config.Bool("cleanup.removeDuplicateUsings"));
        return new TransformResult(text, diagnostics.ToImmutableArray(), members, regions, wraps, safety, organizedTypes);
    }

    private static async Task<string> FormatPreservingBreaksAsync(Document document, EditorSettings editor, EffectiveConfig config, bool targeted, CancellationToken ct)
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var before = (await document.GetTextAsync(ct)).ToString();
            var after = await FormatPreservingBreaksOnceAsync(document, editor, config, targeted, ct);
            if (before == after) { return after; }
            document = document.WithText(SourceText.From(after));
        }
        throw new OrganizerException(4, "CSORG6003", "Breakpoint-preserving formatting did not converge.");
    }

    private static async Task<string> FormatPreservingBreaksOnceAsync(Document document, EditorSettings editor, EffectiveConfig config, bool targeted, CancellationToken ct)
    {
        document = document.WithText(SourceText.From(await NormalizeListIndentationAsync(document, editor, config, ct)));
        var root = (await document.GetSyntaxRootAsync(ct))!;
        var source = await document.GetTextAsync(ct);
        var lists = root.DescendantNodes().Where(n => n is ArgumentListSyntax or ParameterListSyntax)
            .Where(n => n.ToString().Contains('\n') || n.ToString().Contains('\r')).ToArray();
        var tokens = root.DescendantTokens().ToArray();
        var breakpoints = new List<(int Previous, int Next, int Lines)>();
        for (var index = 1; index < tokens.Length; index++)
        {
            var span = TextSpan.FromBounds(tokens[index - 1].Span.End, tokens[index].SpanStart);
            if (!lists.Any(list => list.Span.Contains(span))) { continue; }
            var gap = source.ToString(span);
            if (gap.All(char.IsWhiteSpace) && (gap.Contains('\n') || gap.Contains('\r'))) { breakpoints.Add((index - 1, index, gap.Replace("\r\n", "\n", StringComparison.Ordinal).Count(c => c is '\n' or '\r'))); }
        }
        const string annotation = "ForMatthias.BreakpointToken";
        var tokenIds = tokens.Select((token, index) => (token, index)).ToDictionary(pair => pair.token, pair => pair.index);
        root = root.ReplaceTokens(tokens, (original, rewritten) => rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(annotation, tokenIds[original].ToString(System.Globalization.CultureInfo.InvariantCulture))));
        document = document.WithSyntaxRoot(root);
        Microsoft.CodeAnalysis.Options.OptionSet options = await document.GetOptionsAsync(ct);
        options = options.WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, editor.UseTabs)
            .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, editor.TabWidth)
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, editor.IndentSize)
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, editor.NewLine);
        if (targeted)
        {
            var spans = lists.Select(list => list.Ancestors().FirstOrDefault(n => n is MemberDeclarationSyntax)?.FullSpan ?? list.FullSpan).Distinct().ToArray();
            document = await Formatter.FormatAsync(document, spans, options, ct);
        }
        else { document = await Formatter.FormatAsync(document, options, ct); }
        root = (await document.GetSyntaxRootAsync(ct))!;
        var formattedTokens = root.GetAnnotatedTokens(annotation).ToDictionary(t => int.Parse(t.GetAnnotations(annotation).Single().Data!, System.Globalization.CultureInfo.InvariantCulture));
        var formatted = await document.GetTextAsync(ct);
        var edits = new List<TextChange>();
        foreach (var (previous, next, lines) in breakpoints)
        {
            var left = formattedTokens[previous]; var right = formattedTokens[next];
            var span = TextSpan.FromBounds(left.Span.End, right.SpanStart);
            var gap = formatted.ToString(span);
            if (!gap.All(char.IsWhiteSpace)) { continue; }
            var lastBreak = Math.Max(gap.LastIndexOf('\n'), gap.LastIndexOf('\r'));
            var indentation = lastBreak >= 0 ? gap[(lastBreak + 1)..] : editor.ContinueIndent(new string(formatted.Lines.GetLineFromPosition(right.SpanStart).ToString().TakeWhile(c => c is ' ' or '\t').ToArray()));
            var replacement = string.Concat(Enumerable.Repeat(editor.NewLine, lines)) + indentation;
            if (replacement != gap) { edits.Add(new TextChange(span, replacement)); }
        }
        var text = formatted.WithChanges(edits).ToString();
        return new MemberSpacing(config, editor).Visit(CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)document.Project.ParseOptions!, cancellationToken: ct).GetRoot(ct))!.ToFullString();
    }

    private static async Task<string> NormalizeListIndentationAsync(Document document, EditorSettings editor, EffectiveConfig config, CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;
        var source = await document.GetTextAsync(ct);
        var indentation = new Dictionary<int, string>();
        foreach (var list in root.DescendantNodes().Where(n => n is ArgumentListSyntax or ParameterListSyntax).OrderBy(n => n.SpanStart).ThenByDescending(n => n.Span.Length))
        {
            var openingLine = source.Lines.GetLineFromPosition(list.SpanStart);
            var baseIndent = indentation.GetValueOrDefault(openingLine.Start) ?? new string(openingLine.ToString().TakeWhile(c => c is ' ' or '\t').ToArray());
            var desired = editor.ContinueIndent(baseIndent, config.GetNumber("wrapping.continuationIndentLevels", 1));
            var items = list is ArgumentListSyntax arguments ? arguments.Arguments.Cast<SyntaxNode>() : ((ParameterListSyntax)list).Parameters;
            foreach (var item in items)
            {
                var line = source.Lines.GetLineFromPosition(item.SpanStart);
                if (line.LineNumber == openingLine.LineNumber) { continue; }
                var prefix = source.ToString(TextSpan.FromBounds(line.Start, item.SpanStart));
                if (prefix.All(c => c is ' ' or '\t')) { indentation[line.Start] = desired; }
            }
        }
        return source.WithChanges(indentation.Select(pair => new TextChange(new TextSpan(pair.Key, source.Lines.GetLineFromPosition(pair.Key).ToString().TakeWhile(c => c is ' ' or '\t').Count()), pair.Value))).ToString();
    }

    private static string TrimWhitespace(string text, CSharpParseOptions options)
    {
        var tree = CSharpSyntaxTree.ParseText(text, options);
        var source = tree.GetText();
        var root = tree.GetRoot();
        var tokens = root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken) || ListWrapper.IsRaw(t) || t.Parent?.AncestorsAndSelf().Any(n => n is InterpolatedStringExpressionSyntax) == true).Select(t => t.Span)
            .Concat(root.DescendantTrivia().Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia)).Select(t => t.Span)).ToArray();
        var changes = new List<TextChange>();
        foreach (var line in source.Lines)
        {
            var value = line.ToString();
            var end = value.Length;
            while (end > 0 && value[end - 1] is ' ' or '\t') { end--; }
            if (end == value.Length) { continue; }
            var span = TextSpan.FromBounds(line.Start + end, line.End);
            if (!tokens.Any(t => t.OverlapsWith(span))) { changes.Add(new TextChange(span, "")); }
        }
        return source.WithChanges(changes).ToString();
    }
}

public static class SyntaxDocuments
{
    public static Document Create(AdhocWorkspace workspace, string path, string text, EditorSettings editor, CSharpParseOptions? parseOptions = null)
    {
        var id = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(id, Path.GetFileNameWithoutExtension(path), "Syntax", LanguageNames.CSharp)
            .WithProjectParseOptions(id, parseOptions ?? new CSharpParseOptions(LanguageVersion.Latest, documentationMode: DocumentationMode.Diagnose));
        foreach (var configPath in editor.ConfigPaths)
        {
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(id), ".editorconfig", SourceText.From(File.ReadAllText(configPath)), filePath: configPath);
        }
        var documentId = DocumentId.CreateNewId(id);
        solution = solution.AddDocument(documentId, Path.GetFileName(path), SourceText.From(text), filePath: path);
        return solution.GetDocument(documentId)!;
    }
}
