using System.Text.Json.Nodes;

namespace TaskBaton.Protocol;

/// <summary>Exact session binding for the next turn. The id is never a rolling/alias value.</summary>
public sealed class SessionBinding
{
    public string Host { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string? Cwd { get; init; }

    public string? Profile { get; init; }

    public static SessionBinding FromJson(JsonObject obj) => new()
    {
        Host = JsonRead.String(obj, "host") ?? string.Empty,
        SessionId = JsonRead.String(obj, "session_id") ?? string.Empty,
        Cwd = JsonRead.String(obj, "cwd"),
        Profile = JsonRead.String(obj, "profile"),
    };

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject
        {
            ["host"] = Host,
            ["session_id"] = SessionId,
        };
        if (Cwd is not null)
            result["cwd"] = Cwd;
        if (Profile is not null)
            result["profile"] = Profile;
        return result;
    }
}
