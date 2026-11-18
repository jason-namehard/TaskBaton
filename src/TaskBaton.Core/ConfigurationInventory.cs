using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace TaskBaton.Core;

public static class ConfigurationFiles
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Decode(byte[] bytes) => Utf8.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0));
    public static byte[] Encode(string text, byte[] original) => original.AsSpan().StartsWith(new byte[] { 239, 187, 191 })
        ? new byte[] { 239, 187, 191 }.Concat(Utf8.GetBytes(text)).ToArray() : Utf8.GetBytes(text);

    public static ConfigurationInventory Inspect(string path, string format)
    {
        var full = Path.GetFullPath(path);
        return new ConfigurationInventory(full, format, Parse(Decode(File.ReadAllBytes(full)), format));
    }

    public static McpEntry[] Parse(string text, string format)
    {
        try
        {
            if (format == "json")
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Configuration must be an object.");
                if (!document.RootElement.TryGetProperty("mcpServers", out var servers)) return [];
                return servers.EnumerateObject().Select(p => new McpEntry(p.Name,
                    p.Value.TryGetProperty("command", out var command) ? command.GetString() : null,
                    p.Value.TryGetProperty("args", out var args) ? args.EnumerateArray().Select(a => a.GetString() ?? throw new JsonException("Null argument.")).ToArray() : [],
                    p.Value.TryGetProperty("url", out var url) ? url.GetString() : null,
                    !p.Value.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean())).ToArray();
            }
            if (format == "toml")
            {
                var model = TomlSerializer.Deserialize<TomlTable>(text)!;
                if (!model.TryGetValue("mcp_servers", out var servers)) return [];
                return ((TomlTable)servers!).Select(p =>
                {
                    var entry = (TomlTable)p.Value!;
                    return new McpEntry(p.Key, entry.TryGetValue("command", out var command) ? (string?)command : null,
                        entry.TryGetValue("args", out var args) ? ((TomlArray)args!).Select(a => a is string value ? value : throw new InvalidCastException("MCP arguments must be strings.")).ToArray() : [],
                        entry.TryGetValue("url", out var url) ? (string?)url : null,
                        !entry.TryGetValue("enabled", out var enabled) || (bool)enabled!);
                }).ToArray();
            }
            throw new RelayException("configuration_format", "format 必须为 json 或 toml。");
        }
        catch (Exception error) when (error is JsonException or TomlException or InvalidOperationException or InvalidCastException)
        {
            throw new RelayException("configuration_invalid", "配置语法或 MCP 条目格式无效：" + error.Message);
        }
    }
}
