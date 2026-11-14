using System.Security.Cryptography;
using System.Text;
using ForMatthias.Core;

namespace ForMatthias.Workspaces;

public sealed record SourceSnapshot(string Path, byte[] Bytes, string Text, Encoding Encoding, byte[] Preamble, string Hash, string NewLine, DateTime LastWriteTime, FileAttributes Attributes, UnixFileMode? Mode)
{
    public static string ContentHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static async Task<SourceSnapshot> ReadAsync(string path, CancellationToken ct = default)
    {
        path = PhysicalPaths.Normalize(path);
        var bytes = await File.ReadAllBytesAsync(path, ct);
        Encoding encoding = new UTF8Encoding(false, true);
        var offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
        {
            throw new OrganizerException(5, "CSORG7002", $"{path}: UTF-32 encoding is unsupported; source preserved.");
        }
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { offset = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        string text;
        try { text = encoding.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException exception) { throw new OrganizerException(5, "CSORG7002", $"{path}: Cannot preserve source encoding: {exception.Message}"); }
        var crlf = text.Split("\r\n").Length - 1;
        var lf = text.Count(c => c == '\n') - crlf;
        var cr = text.Count(c => c == '\r') - crlf;
        var newline = crlf > lf && crlf >= cr ? "\r\n" : cr > lf ? "\r" : "\n";
        return new SourceSnapshot(path, bytes, text, encoding, bytes[..offset], ContentHash(bytes), newline, File.GetLastWriteTimeUtc(path), File.GetAttributes(path), OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path));
    }

    public byte[] Encode(string text) => [.. Preamble, .. Encoding.GetBytes(text)];
}

public static class PhysicalPaths
{
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string Normalize(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(path)!;
        var current = root;
        foreach (var segment in path[root.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null) { current = info.ResolveLinkTarget(true)!.FullName; }
        }
        return current;
    }
}

public interface IChangeCommitter
{
    Task VerifyAsync(SourceSnapshot snapshot, CancellationToken ct);
    Task CommitAsync(SourceSnapshot snapshot, string text, CancellationToken ct);
}

public sealed class ChangeCommitter : IChangeCommitter
{
    public async Task VerifyAsync(SourceSnapshot snapshot, CancellationToken ct)
    {
        if (SourceSnapshot.ContentHash(await File.ReadAllBytesAsync(snapshot.Path, ct)) != snapshot.Hash)
        {
            throw new OrganizerException(5, "CSORG7001", $"{snapshot.Path}: Source changed since it was read; refusing to overwrite.");
        }
        if ((File.GetAttributes(snapshot.Path) & FileAttributes.ReadOnly) != 0)
        {
            throw new OrganizerException(5, "CSORG7003", $"{snapshot.Path}: Source is read-only.");
        }
    }

    public async Task CommitAsync(SourceSnapshot snapshot, string text, CancellationToken ct)
    {
        await VerifyAsync(snapshot, ct);
        var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(snapshot.Path)!, ".formatthias-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(snapshot.Encode(text), ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            if (!OperatingSystem.IsWindows() && snapshot.Mode is { } mode) { File.SetUnixFileMode(temp, mode); }
            await VerifyAsync(snapshot, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, snapshot.Path, true);
        }
        finally { if (File.Exists(temp)) { File.Delete(temp); } }
    }
}
