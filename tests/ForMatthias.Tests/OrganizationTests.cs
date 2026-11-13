using System.Collections.Immutable;
using System.Text.Json.Nodes;
using ForMatthias.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ForMatthias.Tests;

public static class TransformTest
{
    public static async Task<TransformResult> Run(string source, string capabilities = "organize", Action<JsonObject>? configure = null, EditorSettings? editor = null)
    {
        var config = EffectiveConfig.Default;
        configure?.Invoke(config.Json);
        editor ??= new EditorSettings(new Dictionary<string, string>(), []);
        using var workspace = new AdhocWorkspace();
        var doc = SyntaxDocuments.Create(workspace, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Fixture.cs"), source, editor);
        var request = new TransformRequest(doc, config, editor, capabilities.Split(',').ToImmutableHashSet(StringComparer.Ordinal));
        var pipeline = new TransformationPipeline();
        var result = await pipeline.TransformAsync(request);
        CandidateValidator.Validate(source, result.Text, (CSharpParseOptions)doc.Project.ParseOptions!, result.Safety, config, result.OrganizedTypes);
        var second = await pipeline.TransformAsync(request with { Document = doc.WithText(SourceText.From(result.Text)) });
        Assert.Equal(result.Text, second.Text);
        return result;
    }

    public static TypeDeclarationSyntax Type(string text) => CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().First();
    public static string[] Names(string text) => Type(text).Members.Select((m, i) => MemberDescriptor.Create(m, Type(text), i, EffectiveConfig.Default).Name).ToArray();
}

public sealed class OrganizationTests
{
    [Fact]
    public async Task ORG_01_Default_categories_and_accessibility()
    {
        var result = await TransformTest.Run("class C { private void Z() {} public void A() {} int z; public int P {get;} const int K=1; }");
        Assert.Equal(new[] { "K", "z", "P", "A", "Z" }, TransformTest.Names(result.Text));
        Assert.Contains("#region Constants", result.Text);
    }

    [Fact]
    public async Task ORG_02_First_matching_custom_rule()
    {
        var result = await TransformTest.Run("class C { int field; void ZAsync() {} void A() {} }", configure: json => json["organization"]!["rules"] = JsonNode.Parse("""[{"id":"async","region":"Async","match":{"kind":["method"],"nameGlob":"*Async"}},{"id":"rest","region":"Rest","sortBy":[]}]"""));
        Assert.Equal(new[] { "ZAsync", "field", "A" }, TransformTest.Names(result.Text));
    }

    [Fact]
    public async Task ORG_03_04_Signatures_use_arity_count_type_then_passing()
    {
        var result = await TransformTest.Run("class C { void M<T>() {} void M(int x) {} void M() {} void M(string x, int y) {} void M(ref int x) {} void M(Alias x) {} }");
        var methods = TransformTest.Type(result.Text).Members.OfType<MethodDeclarationSyntax>().ToArray();
        Assert.Empty(methods[0].ParameterList.Parameters);
        Assert.Null(methods[0].TypeParameterList);
        Assert.Equal("Alias", methods[1].ParameterList.Parameters[0].Type!.ToString());
        Assert.Equal("int", methods[2].ParameterList.Parameters[0].Type!.ToString());
        Assert.Empty(methods[2].ParameterList.Parameters[0].Modifiers);
        Assert.Single(methods[^1].TypeParameterList!.Parameters);
    }

    [Fact]
    public async Task ORG_05_06_Equal_keys_stable_and_multi_declarators_indivisible()
    {
        var result = await TransformTest.Run("class C { void Z() {} int z=F(), a=G(); void A() {} }", configure: json => json["organization"]!["sortBy"] = new JsonArray());
        Assert.Equal(new[] { "z", "Z", "A" }, TransformTest.Names(result.Text));
        Assert.Equal(2, TransformTest.Type(result.Text).Members.OfType<FieldDeclarationSyntax>().Single().Declaration.Variables.Count);
    }

    [Fact]
    public async Task ORG_07_08_REG_03_Initializer_edges_can_repeat_regions()
    {
        var source = "class C { int z=F(); public int P {get;}=F(); int a=F(); static int y=F(); static int Q {get;}=F(); static int b=F(); }";
        var result = await TransformTest.Run(source);
        var names = TransformTest.Names(result.Text);
        Assert.True(Array.IndexOf(names, "z") < Array.IndexOf(names, "P") && Array.IndexOf(names, "P") < Array.IndexOf(names, "a"));
        Assert.True(Array.IndexOf(names, "y") < Array.IndexOf(names, "Q") && Array.IndexOf(names, "Q") < Array.IndexOf(names, "b"));
        Assert.True(result.Text.Split("#region Fields").Length >= 3);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG3001");
    }

    [Fact]
    public async Task ORG_09_10_Enum_unchanged_and_partial_parts_local()
    {
        var result = await TransformTest.Run("partial class C { void Z() {} enum E { Z, A, B } int b; } partial class C { int z; int a; }");
        var root = CSharpSyntaxTree.ParseText(result.Text).GetRoot();
        Assert.Equal(new[] { "Z", "A", "B" }, root.DescendantNodes().OfType<EnumDeclarationSyntax>().Single().Members.Select(m => m.Identifier.ValueText));
        Assert.Equal(3, root.DescendantNodes().OfType<ClassDeclarationSyntax>().First().Members.Count);
        Assert.Equal(2, root.DescendantNodes().OfType<ClassDeclarationSyntax>().Last().Members.Count);
    }

    [Theory]
    [InlineData("struct C { int z; int a; }")]
    [InlineData("[LayoutAlias] class C { int z; int a; }")]
    [InlineData("record struct C { int z; int a; }")]
    public async Task ORG_11_Layout_sensitive_fields_pinned(string source)
    {
        Assert.Equal(new[] { "z", "a" }, TransformTest.Names((await TransformTest.Run(source)).Text));
    }

    [Fact]
    public async Task ORG_12_COM_interface_order_preserved()
    {
        Assert.Equal(new[] { "Z", "A" }, TransformTest.Names((await TransformTest.Run("[InteropAlias] interface C { void Z(); void A(); }")).Text));
    }

    [Fact]
    public async Task ORG_13_Explicit_interface_qualification_in_key()
    {
        var result = await TransformTest.Run("class C { void Z.M() {} void A.M() {} }");
        Assert.Equal("A", TransformTest.Type(result.Text).Members.OfType<MethodDeclarationSyntax>().First().ExplicitInterfaceSpecifier!.Name.ToString());
    }

    [Fact]
    public async Task REG_01_02_04_Nested_old_regions_become_single_canonical_region()
    {
        var result = await TransformTest.Run("class C\n{\n#region Old\n#region Nested\nvoid M() {}\n#endregion\n#endregion\n}\n");
        Assert.DoesNotContain("Old", result.Text);
        Assert.DoesNotContain("Nested", result.Text);
        Assert.Contains("#region Methods", result.Text);
        Assert.Equal(1, result.Regions);
    }

    [Fact]
    public async Task REG_05_06_Implementation_and_outer_regions_preserved()
    {
        var source = "#region Outer\nclass C\n{\nvoid M()\n{\n#region Inner\nSystem.Console.WriteLine();\n#endregion\n}\n}\n#endregion\n";
        var result = await TransformTest.Run(source);
        Assert.Contains("#region Outer", result.Text);
        Assert.Contains("#region Inner", result.Text);
        Assert.Contains("#region Methods", result.Text);
    }

    [Theory]
    [InlineData("#if UNKNOWN\nvoid Z() {}\n#endif")]
    [InlineData("#nullable enable\nvoid Z() {}")]
    [InlineData("#pragma warning disable CS0169\nint z;")]
    public async Task REG_07_SAFE_03_Context_directives_protect_type(string body)
    {
        var source = "class C\n{\n" + body + "\nvoid A() {}\n}\n";
        var result = await TransformTest.Run(source);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG2002");
    }

    [Fact]
    public async Task TRV_01_02_Docs_attributes_and_trailing_comments_move_with_member()
    {
        var result = await TransformTest.Run("class C\n{\n/// <summary>Zed</summary>\n[A]\nvoid Z() {} // tail Z\n/// <summary>Alpha</summary>\nvoid A() {} // tail A\n}\n");
        Assert.True(result.Text.IndexOf("Alpha", StringComparison.Ordinal) < result.Text.IndexOf("Zed", StringComparison.Ordinal));
        Assert.Contains("void Z() {} // tail Z", result.Text);
        Assert.Contains("[A]", result.Text);
    }

    [Fact]
    public async Task TRV_03_Standalone_banner_protects_scope()
    {
        var source = "class C\n{\n// Section\n\nvoid Z() {}\nvoid A() {}\n}\n";
        var result = await TransformTest.Run(source);
        Assert.Equal(source, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG2004");
    }

    [Fact]
    public async Task SAFE_05_Broken_type_does_not_block_valid_type()
    {
        var result = await TransformTest.Run("class Broken { int x = ; } class Valid { void Z() {} int a; }");
        Assert.Contains(result.Diagnostics, d => d.Id == "CSORG2001");
        Assert.Contains("#region Fields", result.Text);
    }

    [Fact]
    public void Randomized_topological_order_is_permutation_and_respects_edges()
    {
        var random = new Random(734);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var source = "class C {" + string.Join(" ", Enumerable.Range(0, 25).OrderBy(_ => random.Next()).Select(i => $"int f{i} {(i % 2 == 0 ? "= F()" : "")};")) + "}";
            var type = TransformTest.Type(source);
            var members = type.Members.Select((m, i) => MemberDescriptor.Create(m, type, i, EffectiveConfig.Default)).ToArray();
            var ordered = ConstrainedOrdering.Order(members, false, false, out _);
            Assert.Equal(members.Length, ordered.Distinct().Count());
            Assert.Equal(members.Where(m => m.HasInitializer).Select(m => m.Name), ordered.Where(m => m.HasInitializer).Select(m => m.Name));
        }
    }
}
