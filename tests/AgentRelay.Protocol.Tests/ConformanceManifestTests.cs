using System.Text.Json.Nodes;

namespace AgentRelay.Protocol.Tests;

[TestClass]
public sealed class ConformanceManifestTests
{
    private static readonly Lazy<IReadOnlyList<ManifestCase>> Manifest = new(LoadManifest);

    [TestMethod]
    public void Manifest_contains_all_sixteen_cases()
    {
        Assert.HasCount(16, Manifest.Value);
    }

    [TestMethod]
    public void Embedded_schemas_are_usable_without_the_protocol_directory()
    {
        using var temp = new TempDirectory();
        var path = temp.File("HANDOFF.md");
        HandoffWriter.WriteFile(path, TestPaths.HandoffFromFixture());

        var result = new HandoffVerifier().Verify(path);

        Assert.AreEqual(0, result.ExitCode, Describe(result));
    }

    public static IEnumerable<object?[]> Cases()
    {
        foreach (var testCase in Manifest.Value)
        {
            yield return
            [
                testCase.Id,
                testCase.File,
                testCase.Kind,
                testCase.Level,
                testCase.Expect,
                testCase.Error,
                testCase.Field,
                testCase.Semantic,
            ];
        }
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void Fixture_matches_manifest(
        string id,
        string file,
        string kind,
        string level,
        string expect,
        string? error,
        string? field,
        string? semantic)
    {
        _ = id;
        using var temp = new TempDirectory();
        var verifier = new HandoffVerifier(TestPaths.SchemaSet);
        var handoffPath = temp.File("HANDOFF.md");

        // The manifest's L1 label must describe what the schema layer actually decides.
        if (level == "L1")
        {
            var schemaDiagnostics = kind == "handoff"
                ? TestPaths.SchemaSet.ValidateHandoff(TestPaths.FrontmatterJson(file))
                : TestPaths.SchemaSet.ValidateReceipt(TestPaths.ReceiptFromFixture(file).Raw);
            Assert.AreEqual(expect == "valid", !schemaDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                $"{file}: manifest says L1 {expect}, schema says " +
                string.Join(" | ", schemaDiagnostics.Select(diagnostic => $"{diagnostic.Code}@{diagnostic.Field}")));
        }

        VerificationResult result;
        if (kind == "handoff")
        {
            var handoff = TestPaths.HandoffFromFixture(file);
            HandoffWriter.WriteFile(handoffPath, handoff);
            result = verifier.Verify(handoffPath);
        }
        else
        {
            var handoff = semantic == "late"
                ? TestPaths.HandoffFromFixture("cases/valid/full.handoff.json")
                : TestPaths.HandoffFromFixture("cases/valid/minimal.handoff.json");
            HandoffWriter.WriteFile(handoffPath, handoff);

            HandoffTaskContext? context = null;
            if (semantic == "duplicate")
            {
                var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
                new HandoffTaskStateMachine(state).Apply(
                    TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json"));
                context = new HandoffTaskContext { State = state };
            }
            else if (semantic == "late")
            {
                context = new HandoffTaskContext { State = HandoffTaskState.FromHandoff(handoff.Frontmatter) };
            }

            result = verifier.Verify(handoffPath, TestPaths.Fixture(file), context);
        }

        if (expect == "valid")
        {
            Assert.AreEqual(0, result.ExitCode, Describe(result));
            Assert.IsFalse(result.HasErrors, Describe(result));
            if (error is not null)
                Assert.IsTrue(result.HasCode(error), Describe(result));
        }
        else
        {
            Assert.AreNotEqual(0, result.ExitCode, Describe(result));
            Assert.IsNotNull(error);
            Assert.IsTrue(result.HasCode(error), Describe(result));
            if (field is not null)
            {
                Assert.IsTrue(
                    result.Diagnostics.Any(diagnostic =>
                        string.Equals(diagnostic.Code, error, StringComparison.Ordinal)
                        && string.Equals(diagnostic.Field, field, StringComparison.Ordinal)),
                    Describe(result));
            }
        }
    }

    private static string Describe(VerificationResult result) =>
        $"exit={result.ExitCode}; " + string.Join(
            " | ",
            result.Diagnostics.Select(d => $"{d.Code}@{d.Field}: {d.Message}"));

    private static IReadOnlyList<ManifestCase> LoadManifest()
    {
        var manifestPath = Path.Combine(TestPaths.ConformanceRoot, "manifest.json");
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        return root["cases"]!.AsArray()
            .Select(node =>
            {
                var item = node!.AsObject();
                return new ManifestCase(
                    item["id"]!.GetValue<string>(),
                    item["file"]!.GetValue<string>(),
                    item["kind"]!.GetValue<string>(),
                    item["level"]!.GetValue<string>(),
                    item["expect"]!.GetValue<string>(),
                    item["error"]?.GetValue<string>(),
                    item["field"]?.GetValue<string>(),
                    item["semantic"]?.GetValue<string>());
            })
            .ToArray();
    }

    private sealed record ManifestCase(
        string Id,
        string File,
        string Kind,
        string Level,
        string Expect,
        string? Error,
        string? Field,
        string? Semantic);
}
