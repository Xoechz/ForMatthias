using System.Diagnostics;
using System.Text.Json;
using ForMatthias.Cli;
using ForMatthias.Workspaces;

if (args.Length < 2 || args[0] is not ("external" or "synthetic" or "project" or "restore") || args[0] == "project" && args.Length != 3)
{
    Console.Error.WriteLine("Usage: verification external <disposable-checkout> | synthetic <new-disposable-directory> | project <solution> <new-report-and-backup-directory> | restore <report-and-backup-directory>");
    return 2;
}

var mode = args[0];
var input = Path.GetFullPath(args[1]);
if (mode == "restore")
{
    using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(input, "after.json")));
    var entries = manifest.RootElement.EnumerateArray().ToArray();
    foreach (var entry in entries)
    {
        var path = entry.GetProperty("path").GetString()!;
        if (SourceSnapshot.ContentHash(await File.ReadAllBytesAsync(path)) != entry.GetProperty("afterHash").GetString()) { throw new IOException("Source changed after verification; refusing rollback: " + path); }
    }
    foreach (var entry in entries)
    {
        var path = entry.GetProperty("path").GetString()!;
        var bytes = await File.ReadAllBytesAsync(entry.GetProperty("backup").GetString()!);
        if (SourceSnapshot.ContentHash(bytes) != entry.GetProperty("beforeHash").GetString()) { throw new IOException("Backup hash mismatch: " + path); }
        await File.WriteAllBytesAsync(path, bytes);
    }
    Console.WriteLine($"Restored {entries.Length} source snapshots, preserving the pre-formatter working tree.");
    return 0;
}
var root = mode == "project" ? Path.GetFullPath(args[2]) : input;
var backups = new Dictionary<string, (string Backup, string BeforeHash)>(StringComparer.Ordinal);
if (mode == "project")
{
    if (Directory.Exists(root)) { throw new InvalidOperationException("Report/backup directory must be new."); }
    Directory.CreateDirectory(root);
    var sourceRoot = Path.GetDirectoryName(input)!;
    foreach (var path in Discovery.Scan(sourceRoot, "*.cs"))
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var backup = Path.Combine(root, "sources", Path.GetRelativePath(sourceRoot, path));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        await File.WriteAllBytesAsync(backup, bytes);
        backups[path] = (backup, SourceSnapshot.ContentHash(bytes));
    }
    await File.WriteAllTextAsync(Path.Combine(root, "before.json"), JsonSerializer.Serialize(backups.Select(pair => new { path = pair.Key, backup = pair.Value.Backup, hash = pair.Value.BeforeHash })));
}
if (mode == "synthetic")
{
    if (Directory.Exists(root)) { throw new InvalidOperationException("Synthetic output must be a new disposable directory."); }
    Directory.CreateDirectory(root);
    const int projects = 10;
    const int filesPerProject = 30;
    const int methodsPerFile = 30;
    var entries = new List<string>();
    for (var project = 0; project < projects; project++)
    {
        var directory = Path.Combine(root, "P" + project);
        Directory.CreateDirectory(directory);
        var reference = project == 0 ? "" : $"<ItemGroup><ProjectReference Include=\"../P{project - 1}/P{project - 1}.csproj\" /></ItemGroup>";
        await File.WriteAllTextAsync(Path.Combine(directory, $"P{project}.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>{reference}</Project>");
        entries.Add($"<Project Path=\"P{project}/P{project}.csproj\" />");
        for (var file = 0; file < filesPerProject; file++)
        {
            var methods = string.Join("\n", Enumerable.Range(0, methodsPerFile).Reverse().Select(index => $"    public int M{index:D2}(int value) => value + z;"));
            var linkedType = project == 0 ? "" : $"    private readonly Synthetic.P{project - 1}.C000 previous = new();\n";
            await File.WriteAllTextAsync(Path.Combine(directory, $"C{file:D3}.cs"), $"namespace Synthetic.P{project};\npublic class C{file:D3}\n{{\n{methods}\n    private int z;\n    private int a;\n{linkedType}    public int Value => z;\n}}\n");
        }
    }
    input = Path.Combine(root, "Synthetic.slnx");
    await File.WriteAllTextAsync(input, "<Solution>" + string.Join("", entries) + "</Solution>");
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in new[] { "restore", input, "-nr:false" }) { start.ArgumentList.Add(argument); }
    using var restore = Process.Start(start)!;
    var stdout = restore.StandardOutput.ReadToEndAsync(); var stderr = restore.StandardError.ReadToEndAsync();
    await restore.WaitForExitAsync();
    if (restore.ExitCode != 0) { Console.Error.WriteLine(await stdout + await stderr); return 3; }
}

var firstReport = Path.Combine(root, "formatthias-verification-first.json");
var secondReport = Path.Combine(root, "formatthias-verification-second.json");
using var errors = new StringWriter(); using var output = new StringWriter();
var firstArgs = mode == "external" ? new[] { input, "--workspace", "syntax", "--report", firstReport } : new[] { input, "--report", firstReport };
var timer = Stopwatch.StartNew();
var firstExit = await Application.RunAsync(firstArgs, output, errors);
var firstSeconds = timer.Elapsed.TotalSeconds;
var secondExit = -1;
var secondSeconds = 0.0;
if (firstExit == 0)
{
    timer.Restart();
    var secondArgs = mode == "external" ? new[] { input, "--workspace", "syntax", "--check", "--diff", "--report", secondReport } : new[] { input, "--check", "--diff", "--report", secondReport };
    secondExit = await Application.RunAsync(secondArgs, output, errors);
    secondSeconds = timer.Elapsed.TotalSeconds;
}
using var report = JsonDocument.Parse(await File.ReadAllTextAsync(firstReport));
var files = report.RootElement.GetProperty("files").EnumerateArray().ToArray();
var diagnostics = files.SelectMany(file => file.GetProperty("diagnostics").EnumerateArray()).ToArray();
if (backups.Count > 0)
{
    await File.WriteAllTextAsync(Path.Combine(root, "after.json"), JsonSerializer.Serialize(backups.Select(pair => new { path = pair.Key, backup = pair.Value.Backup, beforeHash = pair.Value.BeforeHash, afterHash = SourceSnapshot.ContentHash(File.ReadAllBytes(pair.Key)) })));
}
var summary = new
{
    mode,
    input,
    files = files.Length,
    changed = files.Count(file => file.GetProperty("status").GetString() == "changed"),
    failed = files.Count(file => file.GetProperty("status").GetString() == "failed"),
    diagnostics = diagnostics.GroupBy(d => d.GetProperty("id").GetString()!).ToDictionary(group => group.Key, group => group.Count()),
    firstExit,
    firstSeconds,
    secondExit,
    secondSeconds,
    secondDiffCharacters = output.ToString().Length
};
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
if (firstExit != 0 || secondExit != 0 || output.ToString().Length != 0) { Console.Error.WriteLine(errors); return 1; }
return 0;
