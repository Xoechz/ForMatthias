using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace ForMatthias.Workspaces;

public static class WorkspaceBootstrap
{
    private static readonly object Sync = new();

    public static void Register()
    {
        lock (Sync)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MSBuildWorkspace Create(IDictionary<string, string>? properties = null)
    {
        Register();
        return MSBuildWorkspace.Create(properties ?? new Dictionary<string, string>());
    }
}
