using System.Collections.Immutable;
using ForMatthias.Core;

namespace ForMatthias.Cli;

public sealed class CliOptions
{
    public string Path { get; private set; } = ".";
    public string Workspace { get; private set; } = "auto";
    public string? Framework { get; private set; }
    public string? Config { get; private set; }
    public string? Report { get; private set; }
    public bool Check { get; private set; }
    public bool Diff { get; private set; }
    public bool Restore { get; private set; }
    public bool SyntaxFallback { get; private set; }
    public bool IncludeGenerated { get; private set; }
    public bool FailOnSkipped { get; private set; }
    public bool FailOnNewWarnings { get; private set; }
    public bool Verbose { get; private set; }
    public bool Help { get; private set; }
    public bool Version { get; private set; }
    public List<string> Includes { get; } = [];
    public List<string> Excludes { get; } = [];
    public ImmutableHashSet<string> Capabilities { get; private set; } = ImmutableHashSet.Create(StringComparer.Ordinal, "organize", "format", "wrap", "cleanup");

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var hasPath = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) ? args[i] : throw Error("Missing value for " + args[i - 1]);
            switch (args[i])
            {
                case "--help": case "-h": options.Help = true; break;
                case "--version": options.Version = true; break;
                case "--check": options.Check = true; break;
                case "--diff": options.Diff = true; break;
                case "--restore": options.Restore = true; break;
                case "--include-generated": options.IncludeGenerated = true; break;
                case "--fail-on-skipped": options.FailOnSkipped = true; break;
                case "--fail-on-new-warnings": options.FailOnNewWarnings = true; break;
                case "--verbose": options.Verbose = true; break;
                case "--workspace": options.Workspace = Value(); break;
                case "--framework": options.Framework = Value(); break;
                case "--config": options.Config = Value(); break;
                case "--report": options.Report = Value(); break;
                case "--include": options.Includes.Add(Value()); break;
                case "--exclude": options.Excludes.Add(Value()); break;
                case "--on-load-failure":
                    var failure = Value();
                    if (failure is not ("fail" or "syntax")) { throw Error("Invalid --on-load-failure value."); }
                    options.SyntaxFallback = failure == "syntax";
                    break;
                case "--only":
                    var only = Value().Split(',');
                    if (only.Any(cap => !options.Capabilities.Contains(cap))) { throw Error("Unknown --only capability."); }
                    options.Capabilities = only.ToImmutableHashSet(StringComparer.Ordinal);
                    break;
                default:
                    if (args[i].StartsWith('-') || hasPath) { throw Error("Unexpected argument: " + args[i]); }
                    options.Path = args[i]; hasPath = true; break;
            }
        }
        if (options.Workspace is not ("auto" or "project" or "syntax")) { throw Error("Invalid workspace mode."); }
        foreach (var pattern in options.Includes.Concat(options.Excludes)) { Globs.Match("", pattern); }
        return options;
    }

    private static OrganizerException Error(string message) => new(2, "CSORG1002", message);
}
