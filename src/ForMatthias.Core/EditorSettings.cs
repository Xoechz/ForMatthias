using System.Text.RegularExpressions;

namespace ForMatthias.Core;

public static class Globs
{
    public static bool Match(string text, string pattern)
    {
        text = text.Replace('\\', '/');
        pattern = pattern.Replace('\\', '/');
        var regex = "";
        for (var index = 0; index < pattern.Length; index++)
        {
            var c = pattern[index];
            if (c == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                index++;
                if (index + 1 < pattern.Length && pattern[index + 1] == '/') { regex += "(?:.*/)?"; index++; }
                else { regex += ".*"; }
            }
            else if (c == '*') { regex += "[^/]*"; }
            else if (c == '?') { regex += "[^/]"; }
            else if (c == '{')
            {
                var end = pattern.IndexOf('}', index + 1);
                var range = end > index ? pattern[(index + 1)..end].Split("..", StringSplitOptions.None) : [];
                if (range.Length == 2 && int.TryParse(range[0], out var low) && int.TryParse(range[1], out var high) && (long)high - low is >= 0 and <= 1000)
                {
                    regex += "(?:" + string.Join("|", Enumerable.Range(low, high - low + 1)) + ")"; index = end;
                }
                else { regex += "(?:"; }
            }
            else if (c == '}') { regex += ")"; }
            else if (c == ',') { regex += "|"; }
            else if (c == '[')
            {
                var end = pattern.IndexOf(']', index + 1);
                if (end < 0) { regex += "\\["; }
                else { var characterClass = pattern[(index + 1)..end]; regex += "[" + (characterClass.StartsWith('!') ? "^" + characterClass[1..] : characterClass) + "]"; index = end; }
            }
            else { regex += Regex.Escape(c.ToString()); }
        }
        try { return Regex.IsMatch(text, "^(?:" + regex + ")$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException exception) { throw new OrganizerException(2, "CSORG1001", $"Invalid glob '{pattern}': {exception.Message}"); }
        catch (RegexMatchTimeoutException) { throw new OrganizerException(2, "CSORG1001", $"Glob matching exceeded its time budget: {pattern}"); }
    }
}

public sealed record EditorSettings(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> ConfigPaths, string NewLine = "\n")
{
    public int Number(string key, int fallback) => Values.TryGetValue(key, out var value) && int.TryParse(value, out var number) && number > 0 ? number : fallback;
    public int IndentSize => Number("indent_size", TabWidth);
    public int TabWidth => Number("tab_width", Number("indent_size", 4));
    public bool UseTabs => Values.GetValueOrDefault("indent_style") == "tab";
    public string Indent => UseTabs ? "\t" : new string(' ', IndentSize);
    public int LineLength => Number("max_line_length", 120);

    public static EditorSettings Resolve(string path, string originalNewLine = "\n")
    {
        var files = new List<string>();
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, ".editorconfig");
            if (!File.Exists(file)) { continue; }
            files.Insert(0, file);
            if (File.ReadLines(file).TakeWhile(line => !line.TrimStart().StartsWith('[')).Any(line => Regex.IsMatch(line, @"^\s*root\s*=\s*true\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) { break; }
        }
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var active = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') { continue; }
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var pattern = line[1..^1];
                    var relative = Path.GetRelativePath(Path.GetDirectoryName(file)!, path).Replace('\\', '/');
                    active = Globs.Match(pattern.Contains('/') ? relative : Path.GetFileName(path), pattern.TrimStart('/'));
                }
                else if (active && line.IndexOf('=') is var equals && equals > 0)
                {
                    var key = line[..equals].Trim();
                    var value = line[(equals + 1)..].Trim();
                    if (value.Equals("unset", StringComparison.OrdinalIgnoreCase)) { values.Remove(key); }
                    else { values[key] = value; }
                }
            }
        }
        var newline = values.GetValueOrDefault("end_of_line") switch { "crlf" => "\r\n", "cr" => "\r", "lf" => "\n", _ => originalNewLine };
        return new EditorSettings(values, files, newline);
    }
}
