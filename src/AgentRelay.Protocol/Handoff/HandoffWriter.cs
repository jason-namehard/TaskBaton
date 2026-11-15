using System.Text;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace AgentRelay.Protocol;

/// <summary>Writes a HANDOFF.md document with quoted UTC timestamps and preserved unknown frontmatter keys.</summary>
public static class HandoffWriter
{
    public static string Write(HandoffDocument document)
    {
        var yamlNode = YamlJson.ToYaml(document.Frontmatter.ToJsonObject());
        var mapping = yamlNode as YamlMappingNode
            ?? throw new InvalidOperationException("Handoff frontmatter must serialize to a YAML mapping.");

        var serializer = new SerializerBuilder().Build();
        var yaml = serializer.Serialize(mapping).TrimEnd('\r', '\n');
        var body = document.Body.TrimStart('\r', '\n');
        return $"---\n{yaml}\n---\n{body}";
    }

    public static void WriteFile(string path, HandoffDocument document, bool overwrite = true)
    {
        if (!overwrite && File.Exists(path))
            throw new IOException($"Refusing to overwrite an existing handoff: {path}");
        File.WriteAllText(path, Write(document), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
