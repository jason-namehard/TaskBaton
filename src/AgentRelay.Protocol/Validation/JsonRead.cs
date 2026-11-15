using System.Text.Json.Nodes;

namespace AgentRelay.Protocol;

internal static class JsonRead
{
    public static string? String(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value)
            return null;
        if (value.TryGetValue<string>(out var text))
            return text;
        return null;
    }

    public static int? Int(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value)
            return null;
        if (value.TryGetValue<int>(out var number))
            return number;
        if (value.TryGetValue<long>(out var wide) && wide is >= int.MinValue and <= int.MaxValue)
            return (int)wide;
        return null;
    }

    public static bool? Bool(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value)
            return null;
        return value.TryGetValue<bool>(out var boolean) ? boolean : null;
    }

    public static JsonObject? Object(JsonObject obj, string name) => obj[name] as JsonObject;

    public static JsonArray? Array(JsonObject obj, string name) => obj[name] as JsonArray;
}
