using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ForMatthias.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace ForMatthias.Workspaces;

public sealed class ProjectSession : IDisposable
{
    private readonly List<MSBuildWorkspace> _workspaces = [];
    public List<Solution> Solutions { get; } = [];
    public List<OrganizerDiagnostic> Diagnostics { get; } = [];
    public List<OrganizerDiagnostic> ValidationDiagnostics { get; } = [];
    public List<OrganizerDiagnostic> CleanupDiagnostics { get; } = [];
    public Dictionary<string, List<Document>> Files { get; } = new(PhysicalPaths.Comparer);
    private readonly List<Dictionary<string, int>> _baselines = [];
    private readonly Dictionary<ProjectId, bool> _cleanupEligibility = [];

    public static async Task<ProjectSession> LoadAsync(InputSelection selection, string? framework, bool restore, bool warnings, CancellationToken ct)
    {
        WorkspaceBootstrap.Register();
        var session = new ProjectSession();
        try
        {
            if (restore) { await RunDotnetAsync(["restore", selection.ProjectPath!, "-nr:false"], Path.GetDirectoryName(selection.ProjectPath!)!, ct); }
            var initial = await session.OpenAsync(selection.ProjectPath!, framework, ct);
            var projects = initial.Projects.Where(p => p.Language == LanguageNames.CSharp).ToArray();
            if (projects.Length == 0) { throw LoadError("No C# projects loaded."); }
            var seen = new HashSet<string>(PhysicalPaths.Comparer);
            foreach (var project in projects)
            {
                if (project.FilePath is null || !seen.Add(project.FilePath)) { continue; }
                var frameworks = await FrameworksAsync(project.FilePath, ct);
                if (framework is not null && !frameworks.Contains(framework, StringComparer.Ordinal)) { throw LoadError($"{project.FilePath}: Requested framework {framework} is not supported ({string.Join(", ", frameworks)})."); }
                if (framework is not null)
                {
                    session.Diagnostics.Add(new("CSORG5003", $"Only {framework} validated; other target frameworks omitted.", project.FilePath, Severity: "info"));
                }
                else if (frameworks.Length > 1)
                {
                    foreach (var tfm in frameworks) { await session.OpenAsync(project.FilePath, tfm, ct); }
                }
            }
            for (var i = 0; i < session.Solutions.Count; i++)
            {
                var solution = session.Solutions[i];
                foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp).ToArray())
                {
                    if (project.ParseOptions is CSharpParseOptions options) { solution = solution.WithProjectParseOptions(project.Id, options.WithDocumentationMode(DocumentationMode.Diagnose)); }
                }
                session.Solutions[i] = solution;
                session._baselines.Add(await CompilationDiagnosticsAsync(solution, ct));
                foreach (var document in solution.Projects.Where(p => p.Language == LanguageNames.CSharp).SelectMany(p => p.Documents))
                {
                    if (document.FilePath is null || !File.Exists(document.FilePath)) { throw LoadError("Loaded source document has no readable physical file: " + document.Name); }
                    var path = PhysicalPaths.Normalize(document.FilePath);
                    if (!session.Files.TryGetValue(path, out var contexts)) { session.Files[path] = contexts = []; }
                    contexts.Add(document);
                }
            }
            if (selection.SingleFile is not null && !session.Files.ContainsKey(PhysicalPaths.Normalize(selection.SingleFile))) { throw LoadError("The selected file is not a Compile item in the containing project."); }
            return session;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            session.Dispose();
            if (exception is OrganizerException) { throw; }
            throw LoadError(exception.Message);
        }
    }

    private async Task<Solution> OpenAsync(string path, string? framework, CancellationToken ct)
    {
        var properties = new Dictionary<string, string>();
        if (framework is not null) { properties["TargetFramework"] = framework; }
        var workspace = WorkspaceBootstrap.Create(properties);
        _workspaces.Add(workspace);
        Solution solution;
        if (Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)) { solution = (await workspace.OpenProjectAsync(path, cancellationToken: ct)).Solution; }
        else { solution = await workspace.OpenSolutionAsync(path, cancellationToken: ct); }
        var failures = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToArray();
        if (failures.Length > 0) { throw LoadError(string.Join(Environment.NewLine, failures.Select(d => d.Message))); }
        foreach (var diagnostic in workspace.Diagnostics) { Diagnostics.Add(new("CSORG5004", diagnostic.Message, path)); }
        if (solution.ProjectIds.Count == 0 || solution.Projects.Any(p => p.Language != LanguageNames.CSharp)) { throw LoadError("Workspace contains missing or unsupported non-C# project contexts."); }
        if (solution.Projects.Any(p => p.MetadataReferences.Count == 0)) { throw LoadError("Project has no resolved metadata references. Restore explicitly before running, or use --restore."); }
        if (solution.Projects.SelectMany(p => p.MetadataReferences).OfType<PortableExecutableReference>().Any(r => r.FilePath is not null && !File.Exists(r.FilePath))) { throw LoadError("Project has unresolved metadata reference paths. Restore explicitly before running, or use --restore."); }
        Solutions.Add(solution);
        return solution;
    }

    public async Task<string> RemoveUnusedUsingsAsync(string path, string text, CancellationToken ct)
    {
        var contexts = Files[path];
        HashSet<TextSpan>? unnecessary = null;
        foreach (var doc in contexts)
        {
            var model = await doc.GetSemanticModelAsync(ct) ?? throw LoadError("Missing semantic model: " + path);
            if (!_cleanupEligibility.TryGetValue(doc.Project.Id, out var complete))
            {
                // CS8019 is not trustworthy when binding is incomplete (for example a Razor
                // generated type missing from a design-time compilation). Compilation baselines
                // may retain existing errors for organization, but never justify import deletion.
                complete = !model.Compilation.GetDiagnostics(ct).Any(d => d.Severity == DiagnosticSeverity.Error);
                _cleanupEligibility[doc.Project.Id] = complete;
            }
            if (!complete)
            {
                if (!CleanupDiagnostics.Any(d => d.Path == path)) { CleanupDiagnostics.Add(new("CSORG5005", "Unused-using cleanup skipped: compiler context has errors or missing generated bindings.", path, Capability: "cleanup.unusedUsings")); }
                return text;
            }
            // Public compiler diagnostic, equivalent semantic basis to IDE0005. Never infer from identifiers.
            var spans = model.GetDiagnostics(cancellationToken: ct).Where(d => d.Id == "CS8019").Select(d => d.Location.SourceSpan).ToHashSet();
            if (unnecessary is null) { unnecessary = spans; } else { unnecessary.IntersectWith(spans); }
        }
        if (unnecessary is null || unnecessary.Count == 0) { return text; }
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)contexts[0].Project.ParseOptions!, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct);
        var imports = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(u => unnecessary.Any(span => span.IntersectsWith(u.Span))
            && !u.ContainsDirectives && u.DescendantTrivia().All(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia))).ToArray();
        return root.RemoveNodes(imports, SyntaxRemoveOptions.KeepExteriorTrivia)!.ToFullString();
    }

    public async Task ValidateAsync(IReadOnlyDictionary<string, string> candidates, bool warnings, CancellationToken ct)
    {
        for (var index = 0; index < Solutions.Count; index++)
        {
            var solution = Solutions[index];
            foreach (var document in solution.Projects.SelectMany(p => p.Documents).ToArray())
            {
                if (document.FilePath is not null && candidates.TryGetValue(PhysicalPaths.Normalize(document.FilePath), out var text))
                {
                    var safety = await TypeSafetyAnalysis.ResolveAsync(document, [document], ct);
                    CandidateValidator.ValidateOrderingSafety((await document.GetTextAsync(ct)).ToString(), text, (CSharpParseOptions)document.Project.ParseOptions!, safety);
                    solution = solution.WithDocumentText(document.Id, SourceText.From(text));
                }
            }
            var after = await CompilationDiagnosticsAsync(solution, ct);
            foreach (var (identity, count) in after)
            {
                if (count > _baselines[index].GetValueOrDefault(identity))
                {
                    if (!warnings && identity.Contains("|Warning|", StringComparison.Ordinal)) { ValidationDiagnostics.Add(new("CSORG6001", "New compiler warning: " + identity)); }
                    else { throw new OrganizerException(4, "CSORG6001", "Candidate introduces compiler diagnostic: " + identity); }
                }
            }
        }
    }

    private static async Task<Dictionary<string, int>> CompilationDiagnosticsAsync(Solution solution, CancellationToken ct)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var project in solution.Projects.OrderBy(p => p.FilePath, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            var compilation = await project.GetCompilationAsync(ct) ?? throw LoadError("Compilation unavailable: " + project.FilePath);
            foreach (var diagnostic in compilation.GetDiagnostics(ct).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
            {
                var identity = project.FilePath + "|" + project.Name + "|" + diagnostic.Id + "|" + diagnostic.Severity + "|" + diagnostic.GetMessage(CultureInfo.InvariantCulture);
                if (diagnostic.Location.SourceTree is { } tree)
                {
                    var root = await tree.GetRootAsync(ct);
                    var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                    var declaration = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
                    // A type diagnostic belongs to its header/physical part, never its reorderable body.
                    var declarationIdentity = declaration is null ? DeclarationIdentity.Scope(node) + ":" + node.WithoutTrivia() : DeclarationIdentity.Origin(declaration);
                    identity += "|" + tree.FilePath + "|" + SourceSnapshot.ContentHash(System.Text.Encoding.UTF8.GetBytes(declarationIdentity));
                }
                result[identity] = result.GetValueOrDefault(identity) + 1;
            }
        }
        return result;
    }

    private static async Task<string[]> FrameworksAsync(string projectPath, CancellationToken ct)
    {
        var output = await RunDotnetAsync(["msbuild", projectPath, "-nologo", "-nr:false", "-getProperty:TargetFrameworks,TargetFramework,ProjectAssetsFile,UsingMicrosoftNETSdk"], Path.GetDirectoryName(projectPath)!, ct);
        using var json = JsonDocument.Parse(output);
        var properties = json.RootElement.GetProperty("Properties");
        if (properties.GetProperty("UsingMicrosoftNETSdk").GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            var assets = properties.GetProperty("ProjectAssetsFile").GetString();
            if (string.IsNullOrWhiteSpace(assets) || !File.Exists(Path.GetFullPath(assets, Path.GetDirectoryName(projectPath)!))) { throw LoadError($"{projectPath}: NuGet assets are missing. Restore explicitly first, or use --restore."); }
        }
        var frameworks = properties.GetProperty("TargetFrameworks").GetString();
        if (string.IsNullOrWhiteSpace(frameworks)) { frameworks = properties.GetProperty("TargetFramework").GetString(); }
        if (string.IsNullOrWhiteSpace(frameworks)) { throw LoadError("Cannot discover target frameworks: " + projectPath); }
        return frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<string> RunDotnetAsync(string[] args, string directory, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in args) { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start) ?? throw LoadError("Unable to start dotnet.");
        using var registration = ct.Register(() => { try { if (!process.HasExited) { process.Kill(true); } } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) { throw LoadError((await errors) + (await output)); }
        return await output;
    }

    private static OrganizerException LoadError(string message) => new(3, "CSORG5001", message);
    public void Dispose() { foreach (var workspace in _workspaces) { workspace.Dispose(); } }
}
