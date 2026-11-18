using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace TaskBaton.Protocol;

/// <summary>Reads YAML frontmatter and the Markdown body without executing or interpreting the body.</summary>
public static class HandoffReader
{
    public static HandoffDocument ReadFile(string path) => Read(File.ReadAllText(path));

    public static HandoffDocument Read(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length > 0 && normalized[0] == '\uFEFF')
            normalized = normalized[1..];

        var lines = normalized.Split('\n');
        if (lines.Length == 0 || !string.Equals(lines[0].TrimEnd(), "---", StringComparison.Ordinal))
        {
            throw new ProtocolFormatException(
                ProtocolDiagnosticCodes.InvalidFrontmatter,
                "HANDOFF.md must start with a '---' YAML frontmatter delimiter.");
        }

        var closing = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            if (string.Equals(lines[index].TrimEnd(), "---", StringComparison.Ordinal))
            {
                closing = index;
                break;
            }
        }

        if (closing < 0)
        {
            throw new ProtocolFormatException(
                ProtocolDiagnosticCodes.InvalidFrontmatter,
                "HANDOFF.md frontmatter is missing its closing '---' delimiter.");
        }

        var yaml = string.Join('\n', lines[1..closing]);
        var body = string.Join('\n', lines[(closing + 1)..]);

        JsonObject json;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is null)
            {
                json = new JsonObject();
            }
            else if (stream.Documents[0].RootNode is YamlMappingNode mapping)
            {
                json = YamlJson.ToJson(mapping) as JsonObject ?? new JsonObject();
            }
            else
            {
                throw new ProtocolFormatException(
                    ProtocolDiagnosticCodes.InvalidFrontmatter,
                    "HANDOFF.md frontmatter must be a YAML mapping.");
            }
        }
        catch (ProtocolFormatException)
        {
            throw;
        }
        catch (YamlException ex)
        {
            throw new ProtocolFormatException(
                ProtocolDiagnosticCodes.InvalidFrontmatter,
                $"HANDOFF.md frontmatter is not valid YAML: {ex.Message}",
                innerException: ex);
        }

        return new HandoffDocument(HandoffFrontmatter.FromJson(json), body, json);
    }
}
