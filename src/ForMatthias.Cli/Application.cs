using System.Text.Json;
using ForMatthias.Core;
using ForMatthias.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ForMatthias.Cli;

public static class Application
{
    public const string ToolVersion = "1.0.0";
    private const string HelpText = """
        ForMatthias — C# organizer and formatter
        Usage: formatthias [PATH] [options]
        PATH: .cs | directory | .csproj | .sln | .slnx (default: .)
          --workspace auto|project|syntax    Hybrid selection (default auto)
          --framework <TFM>                 Validate only this target framework
          --on-load-failure fail|syntax      Explicit fallback (default fail)
          --restore                         Explicitly restore a trusted project
          --config <path>                   Complete organizer configuration
          --check                           Read-only; exit 1 if changes needed
          --diff                            Read-only unified diff
          --only organize,format,wrap,cleanup
          --include <glob>                  Repeatable include filter
          --exclude <glob>                  Repeatable exclude filter
          --include-generated               Include generated physical sources
          --report <path>                   JSON diagnostic/change report
          --fail-on-skipped                  Exit 1 for incomplete work
          --fail-on-new-warnings             Reject new compiler warnings
          --verbose                         Progress on stderr
          --version
          --help
        Project evaluation executes MSBuild logic: use trusted projects only.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null, CancellationToken cancellationToken = default)
    {
        output ??= Console.Out; error ??= Console.Error;
        CliOptions? options = null;
        var report = new RunReport();
        using var interrupt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; interrupt.Cancel(); };
        Console.CancelKeyPress += handler;
        var ct = interrupt.Token;
        var exit = 0;
        try
        {
            options = CliOptions.Parse(args);
            if (options.Help) { await output.WriteLineAsync(HelpText.AsMemory(), ct); return 0; }
            if (options.Version) { await output.WriteLineAsync(ToolVersion.AsMemory(), ct); return 0; }
            var selection = Discovery.Select(options.Path, options.Workspace);
            if (options.Report is { } requestedReport && (Path.GetExtension(requestedReport).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || PhysicalPaths.Comparer.Equals(Path.GetFullPath(requestedReport), selection.Input)
                || selection.ProjectPath is not null && PhysicalPaths.Comparer.Equals(Path.GetFullPath(requestedReport), selection.ProjectPath)))
            {
                throw new OrganizerException(2, "CSORG1002", "Report path must not overwrite a source, project, or selected input.");
            }
            report.Input = selection.Input;
            var resolver = new ConfigurationResolver();
            // Validate explicit configuration even when discovery yields zero source files.
            if (options.Config is not null) { resolver.Resolve(Path.Combine(selection.Root, "_.cs"), options.Config); }
            ProjectSession? session = null;
            try
            {
                if (selection.IsProject)
                {
                    try { session = await ProjectSession.LoadAsync(selection, options.Framework, options.Restore, options.FailOnNewWarnings, ct); }
                    catch (OrganizerException exception) when (options.SyntaxFallback && exception.ExitCode == 3)
                    {
                        report.Diagnostics.Add(new("CSORG5001", "Explicit syntax fallback: " + exception.Message, selection.Input, Capability: "workspace"));
                    }
                }
                if (session is null && options.Framework is not null) { throw new OrganizerException(2, "CSORG1002", "--framework requires successfully loaded project context."); }
                report.Mode = session is null ? "syntax" : "project";
                if (session is not null) { report.Diagnostics.AddRange(session.Diagnostics); }
                var paths = session is null
                    ? selection.SingleFile is not null ? new[] { selection.SingleFile } : Discovery.Scan(selection.Root, "*.cs", options.IncludeGenerated)
                    : session.Files.Keys.Where(p => selection.SingleFile is null || PhysicalPaths.Comparer.Equals(p, PhysicalPaths.Normalize(selection.SingleFile)));
                var loaded = new List<(SourceSnapshot Snapshot, EffectiveConfig Config, EditorSettings Editor, FileReport Report)>();
                foreach (var path in paths.Select(PhysicalPaths.Normalize).Distinct(PhysicalPaths.Comparer).Order(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var snapshot = await SourceSnapshot.ReadAsync(path, ct);
                        var config = resolver.Resolve(path, options.Config);
                        if (!Discovery.Included(path, selection.Root, snapshot.Text, config, options.IncludeGenerated, options.Includes, options.Excludes)) { continue; }
                        var fileReport = new FileReport { Path = path, BeforeHash = snapshot.Hash };
                        report.Files.Add(fileReport);
                        loaded.Add((snapshot, config, EditorSettings.Resolve(path, snapshot.NewLine), fileReport));
                    }
                    catch (OrganizerException exception) { Record(exception, path); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        report.Files.Add(new FileReport { Path = path, Status = "failed" });
                        Record(new OrganizerException(5, "CSORG7002", exception.Message), path);
                    }
                }
                // Config validation for every file finishes before any transformation or commit.
                if (exit == 0)
                {
                    using var workspace = new AdhocWorkspace();
                    var pipeline = new TransformationPipeline();
                    var candidates = new Dictionary<string, string>(PhysicalPaths.Comparer);
                    foreach (var (snapshot, config, editor, fileReport) in loaded)
                    {
                        if (options.Verbose) { await error.WriteLineAsync(("Processing " + snapshot.Path).AsMemory(), ct); }
                        try
                        {
                            var original = snapshot.Text;
                            var semantic = session is not null;
                            var document = semantic ? session!.Files[snapshot.Path][0] : SyntaxDocuments.Create(workspace, snapshot.Path, original, editor);
                            if (semantic)
                            {
                                foreach (var context in session!.Files[snapshot.Path])
                                {
                                    if ((await context.GetTextAsync(ct)).ToString() != original) { throw new OrganizerException(3, "CSORG5001", "Workspace/source snapshot mismatch: " + snapshot.Path); }
                                }
                                if (options.Capabilities.Contains("cleanup") && config.Bool("cleanup.enabled") && config.Bool("cleanup.removeUnusedUsings"))
                                {
                                    original = await session!.RemoveUnusedUsingsAsync(snapshot.Path, original, ct);
                                    fileReport.Diagnostics.AddRange(session.CleanupDiagnostics.Where(d => d.Path == snapshot.Path));
                                }
                            }
                            document = document.WithText(SourceText.From(original));
                            var request = new TransformRequest(document, config, editor, options.Capabilities, semantic, semantic ? [.. session!.Files[snapshot.Path]] : default);
                            var transformed = await pipeline.TransformAsync(request, ct);
                            var parseContexts = semantic ? session!.Files[snapshot.Path].Select(d => (CSharpParseOptions)d.Project.ParseOptions!).Distinct() : [(CSharpParseOptions)document.Project.ParseOptions!];
                            foreach (var parse in parseContexts) { CandidateValidator.Validate(snapshot.Text, transformed.Text, parse, transformed.Safety, config, transformed.OrganizedTypes); }
                            var second = await pipeline.TransformAsync(request with { Document = document.WithText(SourceText.From(transformed.Text)) }, ct);
                            if (transformed.Text != second.Text) { throw new OrganizerException(4, "CSORG6003", "Pipeline failed byte-for-byte idempotency: " + snapshot.Path); }
                            fileReport.Diagnostics.AddRange(transformed.Diagnostics);
                            fileReport.EmittedRegionCount = transformed.Regions; fileReport.MemberCount = transformed.Members; fileReport.Wraps = transformed.Wraps;
                            fileReport.AppliedCapabilities.AddRange(options.Capabilities.Where(c => config.Bool((c == "organize" ? "organization" : c == "wrap" ? "wrapping" : c) + ".enabled")).Order(StringComparer.Ordinal));
                            fileReport.SkippedCapabilities.AddRange(fileReport.Diagnostics.Where(IsSkip).Select(d => new SkippedCapability(d.Capability ?? "organize", d.Message, d.Start, d.Length)));
                            fileReport.AfterHash = SourceSnapshot.ContentHash(snapshot.Encode(transformed.Text));
                            fileReport.Status = snapshot.Text == transformed.Text ? fileReport.SkippedCapabilities.Count > 0 ? "skipped" : "unchanged" : "changed";
                            if (snapshot.Text != transformed.Text) { candidates[snapshot.Path] = transformed.Text; }
                        }
                        catch (OrganizerException exception) { fileReport.Status = "failed"; fileReport.Diagnostics.Add(new(exception.Id, exception.Message, snapshot.Path, Severity: "error")); exit = Prioritize(exit, exception.ExitCode); }
                    }
                    if (exit == 0 && session is not null)
                    {
                        await session.ValidateAsync(candidates, options.FailOnNewWarnings, ct);
                        report.Diagnostics.AddRange(session.ValidationDiagnostics);
                    }
                    if (exit == 0)
                    {
                        if (options.Diff)
                        {
                            foreach (var item in loaded.Where(item => candidates.ContainsKey(item.Snapshot.Path))) { await output.WriteAsync(UnifiedDiff.Create(Path.GetRelativePath(selection.Root, item.Snapshot.Path), item.Snapshot.Text, candidates[item.Snapshot.Path]).AsMemory(), ct); }
                        }
                        if (!options.Check && !options.Diff)
                        {
                            var committer = new ChangeCommitter();
                            // Preflight the entire validated set before its first physical write.
                            foreach (var item in loaded.Where(item => candidates.ContainsKey(item.Snapshot.Path))) { await committer.VerifyAsync(item.Snapshot, ct); }
                            foreach (var item in loaded.Where(item => candidates.ContainsKey(item.Snapshot.Path)))
                            {
                                try
                                {
                                    await committer.CommitAsync(item.Snapshot, candidates[item.Snapshot.Path], ct);
                                    item.Report.Committed = true;
                                }
                                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OrganizerException)
                                {
                                    item.Report.Status = "failed";
                                    throw;
                                }
                            }
                        }
                        if (options.Check && candidates.Count > 0 || options.FailOnSkipped && (report.Diagnostics.Any(IsSkip) || report.Files.Any(f => f.SkippedCapabilities.Count > 0))) { exit = 1; }
                    }
                }
            }
            finally { session?.Dispose(); }
        }
        catch (OrganizerException exception) { Record(exception, options?.Path); }
        catch (OperationCanceledException) { exit = 130; report.Diagnostics.Add(new("CSORG7004", "Operation cancelled.", Severity: "error")); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        { exit = Prioritize(exit, 5); report.Diagnostics.Add(new("CSORG7002", exception.Message, Severity: "error")); }
        finally { Console.CancelKeyPress -= handler; }
        foreach (var diagnostic in report.Diagnostics.Concat(report.Files.SelectMany(f => f.Diagnostics)).OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Start).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            await error.WriteLineAsync($"{diagnostic.Path}: {diagnostic.Id}: {diagnostic.Message}".AsMemory(), CancellationToken.None);
        }
        report.Summary = new RunSummary(report.Files.Count, report.Files.Count(f => f.Status == "changed"), report.Files.Count(f => f.SkippedCapabilities.Count > 0), report.Files.Count(f => f.Status == "failed"), exit);
        if (options?.Report is { } reportPath && !report.Diagnostics.Any(d => d.Id == "CSORG1002" && d.Message.StartsWith("Report path", StringComparison.Ordinal)))
        {
            try
            {
                var fullReportPath = Path.GetFullPath(reportPath);
                if (report.Files.Any(f => PhysicalPaths.Comparer.Equals(f.Path, fullReportPath))) { throw new IOException("Report path must not overwrite an input source file."); }
                Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
                await File.WriteAllTextAsync(fullReportPath, JsonSerializer.Serialize(report, JsonOptions) + "\n", CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { await error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None); exit = Prioritize(exit, 5); }
        }
        return exit;

        void Record(OrganizerException exception, string? path)
        {
            report.Diagnostics.Add(new(exception.Id, exception.Message, path, Severity: "error"));
            exit = Prioritize(exit, exception.ExitCode);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static bool IsSkip(OrganizerDiagnostic diagnostic) => diagnostic.Capability is not null && diagnostic.Id is "CSORG2001" or "CSORG2002" or "CSORG2004" or "CSORG4001" or "CSORG4002" or "CSORG5001" or "CSORG5002" or "CSORG5005";
    private static int Prioritize(int current, int next)
    {
        int[] priority = [2, 3, 4, 5, 1, 0];
        return Array.IndexOf(priority, next) < Array.IndexOf(priority, current) ? next : current;
    }
}

public sealed class RunReport
{
    public int SchemaVersion { get; } = 1;
    public string ToolVersion { get; } = Application.ToolVersion;
    public string Mode { get; set; } = "unknown";
    public string Input { get; set; } = "";
    public List<FileReport> Files { get; } = [];
    public List<OrganizerDiagnostic> Diagnostics { get; } = [];
    public RunSummary? Summary { get; set; }
}

public sealed class FileReport
{
    public string Path { get; set; } = "";
    public string Status { get; set; } = "unchanged";
    public List<string> AppliedCapabilities { get; } = [];
    public List<SkippedCapability> SkippedCapabilities { get; } = [];
    public int EmittedRegionCount { get; set; }
    public int MemberCount { get; set; }
    public int Wraps { get; set; }
    public List<OrganizerDiagnostic> Diagnostics { get; } = [];
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
    public bool Committed { get; set; }
}

public sealed record SkippedCapability(string Capability, string Reason, int Start, int Length);
public sealed record RunSummary(int Files, int Changed, int Skipped, int Failed, int ExitCode);
