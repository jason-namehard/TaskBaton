using System.Text;
using System.Text.Json.Nodes;

namespace AgentRelay.Protocol;

/// <summary>A HANDOFF.md document: parsed frontmatter plus the Markdown body and its level-2 sections.</summary>
public sealed class HandoffDocument
{
    public static readonly IReadOnlyList<string> RequiredSectionNames =
        ["Summary", "Instructions", "Acceptance criteria", "Context"];

    public HandoffDocument(HandoffFrontmatter frontmatter, string body, JsonObject? frontmatterJson = null)
    {
        Frontmatter = frontmatter;
        Body = body;
        FrontmatterJson = frontmatterJson ?? frontmatter.ToJsonObject();
        Sections = MarkdownSections.Parse(body);
    }

    public HandoffFrontmatter Frontmatter { get; }

    /// <summary>The source JSON shape of the frontmatter, used for schema validation.</summary>
    public JsonObject FrontmatterJson { get; }

    public string Body { get; }

    public IReadOnlyDictionary<string, string> Sections { get; }

    public bool HasSection(string name) =>
        Sections.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    public static HandoffDocument ReadFile(string path) => HandoffReader.ReadFile(path);

    public static HandoffDocument Parse(string markdown) => HandoffReader.Read(markdown);

    public static HandoffDocument Create(
        HandoffFrontmatter frontmatter,
        IReadOnlyDictionary<string, string> sections)
    {
        var body = new StringBuilder();
        foreach (var pair in sections)
        {
            if (body.Length > 0)
                body.Append('\n');
            body.Append("## ").Append(pair.Key).Append("\n\n");
            body.Append(pair.Value.Trim()).Append('\n');
        }

        return new HandoffDocument(frontmatter, body.ToString(), frontmatter.ToJsonObject());
    }
}
