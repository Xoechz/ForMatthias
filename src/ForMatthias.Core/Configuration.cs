using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ForMatthias.Core;

public sealed class EffectiveConfig(JsonObject json)
{
    public JsonObject Json { get; } = json;
    public bool Bool(string path, bool fallback = true) => Node(path)?.GetValue<bool>() ?? fallback;
    public int GetNumber(string path, int fallback) => Node(path)?.GetValue<int>() ?? fallback;
    public string GetText(string path, string fallback = "") => Node(path)?.GetValue<string>() ?? fallback;
    public JsonNode? Node(string path) => path.Split('.').Aggregate((JsonNode?)Json, (node, key) => node?[key]);
    public string[] Strings(string path) => (Node(path)?.AsArray() ?? []).Select(n => n!.GetValue<string>()).ToArray();
    public static EffectiveConfig Default => new(ConfigurationResolver.Defaults());
}

public interface IConfigurationResolver
{
    EffectiveConfig Resolve(string physicalPath, string? explicitConfig = null);
}

public sealed class ConfigurationResolver : IConfigurationResolver
{
    private static JsonObject Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    public static JsonObject Defaults()
    {
        var defaults = Resource("defaults.json");
        defaults["wrapping"]!["maxLineLength"] = null;
        return defaults;
    }

    public EffectiveConfig Resolve(string physicalPath, string? explicitConfig = null)
    {
        var merged = Defaults();
        var layers = new List<JsonObject>();
        if (explicitConfig is not null)
        {
            layers.Add(Read(explicitConfig));
        }
        else
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(physicalPath))!); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, ".csharp-organizer.json");
                if (!File.Exists(path))
                {
                    continue;
                }
                var layer = Read(path);
                layers.Insert(0, layer);
                if (layer["root"]?.GetValue<bool>() == true)
                {
                    break;
                }
            }
        }
        foreach (var layer in layers)
        {
            Merge(merged, layer);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in merged["organization"]!["rules"]!.AsArray())
        {
            if (!ids.Add(rule!["id"]!.GetValue<string>()))
            {
                throw Invalid(explicitConfig ?? physicalPath, "$.organization.rules", "Rule IDs must be unique.");
            }
            if (rule["match"]?["nameGlob"] is { } nameGlob) { Globs.Match("", nameGlob.GetValue<string>()); }
        }
        foreach (var exclusion in merged["exclusions"]!.AsArray()) { Globs.Match("", exclusion!.GetValue<string>()); }
        foreach (var target in new[] { "attributes", "indexers", "tuples" })
        {
            if (merged["wrapping"]!["targets"]![target]?.GetValue<bool>() == true)
            {
                throw Invalid(explicitConfig ?? physicalPath, "$.wrapping.targets." + target, "This wrapping target is unsupported in v1.");
            }
        }
        return new EffectiveConfig(merged);
    }

    private static JsonObject Read(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
            Validate(node, Resource("config.schema.json"), path, "$");
            return node!.AsObject();
        }
        catch (JsonException exception)
        {
            throw Invalid(path, exception.Path ?? "$", exception.Message);
        }
    }

    private static OrganizerException Invalid(string file, string path, string message) => new(2, "CSORG1001", $"{file}: {path}: {message}");

    // This schema has no references: validate every Draft 2020-12 keyword it uses.
    private static void Validate(JsonNode? node, JsonObject schema, string file, string path)
    {
        if (schema["oneOf"] is JsonArray alternatives)
        {
            var matches = 0;
            foreach (var alternative in alternatives)
            {
                try { Validate(node, alternative!.AsObject(), file, path); matches++; }
                catch (OrganizerException) { }
            }
            if (matches != 1) { throw Invalid(file, path, "Value does not match exactly one allowed schema."); }
        }
        var type = schema["type"]?.GetValue<string>();
        var valid = type switch
        {
            "object" => node is JsonObject,
            "array" => node is JsonArray,
            "null" => node is null,
            "string" => node is JsonValue value && value.TryGetValue<string>(out _),
            "boolean" => node is JsonValue value && value.TryGetValue<bool>(out _),
            "integer" => node is JsonValue value && value.TryGetValue<int>(out _),
            _ => true
        };
        if (!valid) { throw Invalid(file, path, $"Expected {type}."); }
        if (schema.ContainsKey("const") && !JsonNode.DeepEquals(node, schema["const"])) { throw Invalid(file, path, "Unsupported schema version or constant."); }
        if (schema["enum"] is JsonArray options && !options.Any(option => JsonNode.DeepEquals(node, option))) { throw Invalid(file, path, "Unknown option value."); }
        if (node is JsonObject obj)
        {
            foreach (var required in schema["required"]?.AsArray() ?? [])
            {
                if (!obj.ContainsKey(required!.GetValue<string>())) { throw Invalid(file, path, "Missing " + required); }
            }
            foreach (var (key, child) in obj)
            {
                if (schema["properties"]?[key] is JsonObject property) { Validate(child, property, file, path + "." + key); }
                else if (schema["additionalProperties"]?.GetValue<bool>() == false) { throw Invalid(file, path + "." + key, "Unknown property."); }
            }
        }
        if (node is JsonArray array)
        {
            if (array.Count < (schema["minItems"]?.GetValue<int>() ?? 0)) { throw Invalid(file, path, "Array is empty."); }
            if (schema["uniqueItems"]?.GetValue<bool>() == true && array.Select(n => n?.ToJsonString()).Distinct(StringComparer.Ordinal).Count() != array.Count) { throw Invalid(file, path, "Duplicate array entries."); }
            for (var index = 0; index < array.Count; index++)
            {
                if (schema["items"] is JsonObject item) { Validate(array[index], item, file, path + $"[{index}]"); }
            }
        }
        if (type == "integer")
        {
            var number = node!.GetValue<int>();
            if (number < (schema["minimum"]?.GetValue<int>() ?? int.MinValue) || number > (schema["maximum"]?.GetValue<int>() ?? int.MaxValue)) { throw Invalid(file, path, "Number is out of range."); }
        }
        if (type == "string")
        {
            var text = node!.GetValue<string>();
            if (text.Length < (schema["minLength"]?.GetValue<int>() ?? 0) || (schema["pattern"] is { } pattern && !Regex.IsMatch(text, pattern.GetValue<string>(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))) { throw Invalid(file, path, "Invalid string."); }
        }
    }

    private static void Merge(JsonObject parent, JsonObject child)
    {
        foreach (var (key, value) in child)
        {
            if (key == "exclusions" && value is JsonArray additions)
            {
                var all = parent[key]!.AsArray().Select(n => n!.GetValue<string>()).Concat(additions.Select(n => n!.GetValue<string>())).Distinct(StringComparer.Ordinal);
                parent[key] = new JsonArray(all.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
            }
            else if (value is JsonObject childObject && parent[key] is JsonObject parentObject) { Merge(parentObject, childObject); }
            else { parent[key] = value?.DeepClone(); }
        }
    }
}
