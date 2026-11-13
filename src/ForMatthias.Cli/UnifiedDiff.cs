using System.Text;

namespace ForMatthias.Cli;

public static class UnifiedDiff
{
    public static string Create(string path, string before, string after)
    {
        if (before == after) { return ""; }
        var builder = new StringBuilder();
        builder.Append("--- a/").AppendLine(path.Replace('\\', '/'));
        builder.Append("+++ b/").AppendLine(path.Replace('\\', '/'));
        var oldLines = Lines(before);
        var newLines = Lines(after);
        builder.Append("@@ -").Append(oldLines.Length == 0 ? 0 : 1).Append(',').Append(oldLines.Length)
            .Append(" +").Append(newLines.Length == 0 ? 0 : 1).Append(',').Append(newLines.Length).AppendLine(" @@");
        foreach (var line in oldLines) { Append('-', line); }
        foreach (var line in newLines) { Append('+', line); }
        return builder.ToString();

        void Append(char prefix, string line)
        {
            builder.Append(prefix).Append(line);
            if (!line.EndsWith('\n')) { builder.AppendLine().AppendLine("\\ No newline at end of file"); }
        }
    }

    private static string[] Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') { continue; }
            lines.Add(text[start..(i + 1)]); start = i + 1;
        }
        if (start < text.Length) { lines.Add(text[start..]); }
        return lines.ToArray();
    }
}
