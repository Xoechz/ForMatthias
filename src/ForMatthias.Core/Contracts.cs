using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ForMatthias.Core;

public sealed record OrganizerDiagnostic(string Id, string Message, string? Path = null, int Start = 0, int Length = 0, string Severity = "warning", string? Capability = null);

public sealed class OrganizerException(int exitCode, string id, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string Id { get; } = id;
}

public sealed record TransformResult(string Text, ImmutableArray<OrganizerDiagnostic> Diagnostics, int Members = 0, int Regions = 0, int Wraps = 0,
    ImmutableDictionary<string, TypeSafety>? Safety = null, ImmutableArray<string> OrganizedTypes = default);

public sealed record TransformRequest(Document Document, EffectiveConfig Config, EditorSettings Editor, ImmutableHashSet<string> Capabilities, bool HasSemanticContext = false,
    ImmutableArray<Document> SemanticContexts = default);

public interface ITransformationPipeline
{
    Task<TransformResult> TransformAsync(TransformRequest request, CancellationToken cancellationToken);
}
