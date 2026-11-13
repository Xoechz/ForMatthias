using ForMatthias.Workspaces;
using Xunit;

namespace ForMatthias.Tests;

public sealed class WorkspaceSpikeTests
{
    [Fact]
    public async Task Public_workspace_reads_restored_project_and_slnx()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        WorkspaceBootstrap.Register();
        using var workspace = WorkspaceBootstrap.Create();
        var solution = await workspace.OpenSolutionAsync(Path.Combine(root, "ForMatthias.slnx"));
        Assert.Equal(4, solution.ProjectIds.Count);
        Assert.DoesNotContain(workspace.Diagnostics, d => d.Kind == Microsoft.CodeAnalysis.WorkspaceDiagnosticKind.Failure);
        Assert.All(solution.Projects, p => Assert.NotEmpty(p.Documents));
    }
}
