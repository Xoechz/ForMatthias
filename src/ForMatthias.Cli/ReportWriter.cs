using ForMatthias.Core;
using ForMatthias.Workspaces;

namespace ForMatthias.Cli;

internal static class ReportWriter
{
    public static string Validate(string path, IEnumerable<string> protectedPaths)
    {
        var fullPath = Path.GetFullPath(path);
        // Reject even dangling leaf links. Resolve parent aliases before comparing identities.
        if (new FileInfo(fullPath).LinkTarget is not null) { throw Invalid("must not be a symbolic link."); }
        var physical = PhysicalPaths.Normalize(fullPath);
        if (Path.GetExtension(physical).ToLowerInvariant() is ".cs" or ".csproj" or ".sln" or ".slnx"
            || protectedPaths.Any(p => PhysicalPaths.Comparer.Equals(PhysicalPaths.Normalize(p), physical)))
        {
            throw Invalid("must not overwrite a source, project, or selected input.");
        }
        return physical;
    }

    public static async Task WriteAsync(string path, string? expectedDestination, IEnumerable<string> protectedPaths, string text)
    {
        var paths = protectedPaths.ToArray();
        var destination = Validate(path, paths);
        if (expectedDestination is not null && !PhysicalPaths.Comparer.Equals(expectedDestination, destination)) { throw Invalid("changed its physical destination during processing."); }
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".formatthias-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text));
                stream.Flush(true);
            }
            if (!PhysicalPaths.Comparer.Equals(destination, Validate(path, paths))) { throw Invalid("changed its physical destination during processing."); }
            // Replace the directory entry rather than opening the destination for writing.
            // A leaf symlink/hard link introduced after validation cannot truncate its target.
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private static OrganizerException Invalid(string reason) => new(2, "CSORG1002", "Report path " + reason);
}
