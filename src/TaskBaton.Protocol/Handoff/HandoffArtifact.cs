using System.Text.Json.Nodes;

namespace TaskBaton.Protocol;

/// <summary>An input, request, result, or evidence file registered by a handoff or receipt.</summary>
public sealed class HandoffArtifact
{
    public string Path { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string? Sha256 { get; init; }

    public string? ExpiresAt { get; init; }

    public static HandoffArtifact FromJson(JsonObject obj) => new()
    {
        Path = JsonRead.String(obj, "path") ?? string.Empty,
        Role = JsonRead.String(obj, "role") ?? string.Empty,
        Sha256 = JsonRead.String(obj, "sha256"),
        ExpiresAt = JsonRead.String(obj, "expires_at"),
    };

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject
        {
            ["path"] = Path,
            ["role"] = Role,
        };
        if (Sha256 is not null)
            result["sha256"] = Sha256;
        if (ExpiresAt is not null)
            result["expires_at"] = ExpiresAt;
        return result;
    }
}
