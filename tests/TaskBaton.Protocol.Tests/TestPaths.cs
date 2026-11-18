using System.Text.Json.Nodes;

namespace TaskBaton.Protocol.Tests;

internal static class TestPaths
{
    public static string ProtocolRoot => Path.Combine(AppContext.BaseDirectory, "protocol");

    public static string ConformanceRoot => Path.Combine(ProtocolRoot, "conformance");

    public static string SchemaDirectory => Path.Combine(ProtocolRoot, "schemas");

    public static ProtocolSchemaSet SchemaSet => ProtocolSchemaSet.FromDirectory(SchemaDirectory);

    public static string Fixture(string relative) =>
        Path.Combine(ConformanceRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    public static JsonObject FrontmatterJson(string relative = "cases/valid/minimal.handoff.json") =>
        JsonNode.Parse(File.ReadAllText(Fixture(relative)))!.AsObject();

    public static HandoffDocument HandoffFromJson(JsonObject frontmatter, bool includeContext = true) =>
        HandoffDocument.Create(HandoffFrontmatter.FromJson(frontmatter), Sections(includeContext));

    public static HandoffDocument HandoffFromFixture(
        string relative = "cases/valid/minimal.handoff.json",
        bool includeContext = true) =>
        HandoffFromJson(FrontmatterJson(relative), includeContext);

    public static ReceiptDocument ReceiptFromFixture(string relative) =>
        ReceiptDocument.LoadFile(Fixture(relative));

    public static Dictionary<string, string> Sections(bool includeContext = true)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Summary"] = "Make the requested change.",
            ["Instructions"] = "- Change only the named files.",
            ["Acceptance criteria"] = "- [ ] The requested files contain the new wording.",
        };
        if (includeContext)
            sections["Context"] = "The wording was flagged in the release review.";
        return sections;
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TaskBaton.Protocol.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A failed cleanup must not hide the assertion result.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the assertion result.
        }
    }
}
