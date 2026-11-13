using System.Collections.Immutable;
using System.Diagnostics;
using ForMatthias.Cli;
using ForMatthias.Core;
using ForMatthias.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ForMatthias.Tests;

public sealed class ReviewRegressionTests
{
    private static async Task Restore(string project)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "restore", project, "-nr:false" }) { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R1_Partial_symbol_attributes_pin_other_parts_in_all_contexts(bool interopInterface)
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup></Project>""");
        var declaration = interopInterface ? "interface" : "class";
        var attribute = interopInterface ? "[System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid(\"00000000-0000-0000-0000-000000000001\")]" : "[LayoutAlias(System.Runtime.InteropServices.LayoutKind.Sequential)]";
        dir.Write("Attributes.cs", "using LayoutAlias = System.Runtime.InteropServices.StructLayoutAttribute;\nnamespace Pack;\n#if NET11_0\n" + attribute + "\n#endif\npublic partial " + declaration + " Packet {}\n");
        var original = "namespace Pack; public partial " + declaration + " Packet { " + (interopInterface ? "void Z(); void A();" : "public int Z; public int A;") + " }\n";
        var path = dir.Write("Members.cs", original);
        await Restore(project);
        using var session = await ProjectSession.LoadAsync(Discovery.Select(project, "auto"), null, false, false, default);
        var contexts = session.Files[path];
        var request = new TransformRequest(contexts[0], EffectiveConfig.Default, EditorSettings.Resolve(path), ImmutableHashSet.Create("organize"), true, [.. contexts]);
        var result = await new TransformationPipeline().TransformAsync(request);
        Assert.Equal(new[] { "Z", "A" }, TransformTest.Names(result.Text));
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG2003");
        var invalid = original.Replace(interopInterface ? "void Z(); void A();" : "public int Z; public int A;", interopInterface ? "void A(); void Z();" : "public int A; public int Z;", StringComparison.Ordinal);
        Assert.Throws<OrganizerException>(() => CandidateValidator.Validate(original, invalid, (CSharpParseOptions)contexts[0].Project.ParseOptions!, result.Safety));
        var exception = await Assert.ThrowsAsync<OrganizerException>(() => session.ValidateAsync(new Dictionary<string, string> { [path] = invalid }, false, default));
        Assert.Equal("CSORG6002", exception.Id);
    }

    [Fact]
    public async Task R1_Syntax_partial_parts_conservatively_pin_unknown_layout()
    {
        Assert.Equal(new[] { "Z", "A" }, TransformTest.Names((await TransformTest.Run("partial class Packet { public int Z; public int A; }")).Text));
        Assert.Equal(new[] { "Z", "A" }, TransformTest.Names((await TransformTest.Run("partial interface Packet { void Z(); void A(); }")).Text));
    }

    [Fact]
    public async Task R2_Type_level_baseline_warnings_survive_member_movement()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup></Project>""");
        var path = dir.Write("C.cs", "namespace Sample; public class C { public void Z() {} public void A() {} }\n");
        await Restore(project);
        using var errors = new StringWriter();
        Assert.True(await Application.RunAsync([project, "--only", "organize", "--fail-on-new-warnings"], new StringWriter(), errors) == 0, errors.ToString());
        Assert.DoesNotContain("New compiler warning", errors.ToString());
        Assert.Equal(new[] { "A", "Z" }, TransformTest.Names(File.ReadAllText(path)));
    }

    [Fact]
    public async Task Incomplete_generated_bindings_never_justify_unused_import_removal()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>""");
        const string source = "using Fixture.Components;\nclass C { App? M() => null; }\nnamespace Fixture.Components { class Placeholder {} }\n";
        var path = dir.Write("C.cs", source);
        await Restore(project);
        using var session = await ProjectSession.LoadAsync(Discovery.Select(project, "auto"), null, false, false, default);
        Assert.Equal(source, await session.RemoveUnusedUsingsAsync(path, source, default));
        Assert.Contains(session.CleanupDiagnostics, d => d.Id == "CSORG5005" && d.Path == path);
        using var errors = new StringWriter();
        Assert.True(await Application.RunAsync([project, "--only", "cleanup", "--fail-on-skipped"], new StringWriter(), errors) == 1, errors.ToString());
        Assert.Contains("using Fixture.Components;", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task R3_Project_invocation_targets_are_independent(bool methodCalls, bool delegateInvocations)
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>""");
        var path = dir.Write("C.cs", "using System;\nclass C\n{\n    void M(Action<int,int,int> callback)\n    {\n        OrdinaryMethod(11111111, 22222222, 33333333);\n        callback(11111111, 22222222, 33333333);\n    }\n    void OrdinaryMethod(int a, int b, int c) {}\n}\n");
        var config = dir.Write("config.json", System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 1, wrapping = new { maxLineLength = 40, targets = new { methodCalls, delegateInvocations } } }));
        await Restore(project);
        using var errors = new StringWriter();
        Assert.True(await Application.RunAsync([project, "--only", "wrap", "--config", config], new StringWriter(), errors) == 0, errors.ToString());
        var text = File.ReadAllText(path);
        Assert.Equal(methodCalls, text.Contains("        OrdinaryMethod(\n", StringComparison.Ordinal));
        Assert.Equal(delegateInvocations, text.Contains("        callback(\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task R3_Syntax_ambiguity_is_diagnosed_without_wrapping_a_disabled_target()
    {
        const string source = "class C { void M() { OrdinaryMethod(11111111, 22222222, 33333333); } }";
        var result = await TransformTest.Run(source, "wrap", json =>
        {
            json["wrapping"]!["maxLineLength"] = 40;
            json["wrapping"]!["targets"]!["methodCalls"] = false;
            json["wrapping"]!["targets"]!["delegateInvocations"] = true;
        });
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG4002");
    }

    [Fact]
    public async Task R3_Inconsistent_invocation_kinds_across_frameworks_are_preserved()
    {
        using var dir = new TempDirectory();
        var project = dir.Write("Fixture.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup></Project>""");
        dir.Write("Target.cs", "partial class C\n{\n#if NET11_0\n    System.Action<int,int,int> Target = (a,b,c) => {};\n#else\n    void Target(int a, int b, int c) {}\n#endif\n}\n");
        const string source = "partial class C\n{\n    void M()\n    {\n        Target(11111111, 22222222, 33333333);\n    }\n}\n";
        var path = dir.Write("Calls.cs", source);
        var config = dir.Write("config.json", """{"schemaVersion":1,"wrapping":{"maxLineLength":40,"targets":{"methodCalls":false,"delegateInvocations":true}}}""");
        await Restore(project);
        using var errors = new StringWriter();
        Assert.Equal(1, await Application.RunAsync([project, "--only", "wrap", "--include", "Calls.cs", "--config", config, "--fail-on-skipped"], new StringWriter(), errors));
        Assert.Equal(source, File.ReadAllText(path));
        Assert.Contains("CSORG4002", errors.ToString());
    }

    [Fact]
    public async Task R4_Full_format_normalizes_list_interiors_and_preserves_breakpoints()
    {
        const string source = "class C\n{\n    void M()\n    {\n        Call(\n a+  b, c,\n              d);\n    }\n}\n";
        const string expected = "class C\n{\n    void M()\n    {\n        Call(\n            a + b, c,\n            d);\n    }\n}\n";
        Assert.Equal(expected, (await TransformTest.Run(source, "format")).Text);
    }

    [Theory]
    [InlineData("class A { void M() {} } class B {}", "class A {} class B { void M() {} }")]
    [InlineData("namespace A { class C { void M() {} } } namespace B { class C {} }", "namespace A { class C {} } namespace B { class C { void M() {} } }")]
    [InlineData("namespace N { class C { void M() {} } } namespace N { class C {} }", "namespace N { class C {} } namespace N { class C { void M() {} } }")]
    [InlineData("[A(/* C */)] class C {} [A(/* D */)] class D {}", "[A(/* D */)] class C {} [A(/* C */)] class D {}")]
    [InlineData("partial class C { void M() {} } partial class C {}", "partial class C {} partial class C { void M() {} }")]
    [InlineData("class O { class A { void M() {} } class B {} }", "class O { class A {} class B { void M() {} } }")]
    [InlineData("class C { void Z() {}\n#nullable enable\nvoid A() {} }", "class C { void A() {}\n#nullable enable\nvoid Z() {} }")]
    [InlineData("class C { void M() {\n#region Body\nint x;\n#endregion\n} }", "class C { void M() { int x; } }")]
    [InlineData("class C { void M() {\n#nullable disable\nstring x;\n} }", "class C { void M() { string x;\n#nullable disable\n} }")]
    [InlineData("class C { void M() {\n#region Body\nint x;\n#endregion\nint y; } }", "class C { void M() { int x;\n#region Body\nint y;\n#endregion\n} }")]
    public void R5_Invalid_ownership_and_directive_candidates_are_rejected(string original, string candidate)
    {
        Assert.Throws<OrganizerException>(() => CandidateValidator.Validate(original, candidate, new CSharpParseOptions()));
    }

    [Theory]
    [InlineData("Wrong", "Fields")]
    [InlineData("Fields", "Methods")]
    public void R5_Generated_regions_must_match_configured_runs(string firstLabel, string secondLabel)
    {
        const string original = "class C { int field; void M() {} }";
        var candidate = "class C\n{\n#region " + firstLabel + "\nint field;\n#endregion\n#region " + secondLabel + "\nvoid M() {}\n#endregion\n}";
        if (firstLabel == "Fields" && secondLabel == "Methods")
        {
            candidate = "class C\n{\n#region Fields\nint field;\nvoid M() {}\n#endregion\n}";
        }
        Assert.Throws<OrganizerException>(() => CandidateValidator.Validate(original, candidate, new CSharpParseOptions(), config: EffectiveConfig.Default, organizedTypes: [DeclarationIdentity.TypePart(TransformTest.Type(original))]));
    }
}
