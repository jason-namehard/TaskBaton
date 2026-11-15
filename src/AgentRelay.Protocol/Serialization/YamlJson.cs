using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AgentRelay.Protocol;

/// <summary>
/// Converts YAML representation nodes to JSON-shaped nodes without applying YAML timestamp coercion.
/// Timestamps therefore stay strings and can be checked for the required UTC <c>Z</c> form.
/// </summary>
internal static partial class YamlJson
{
    private static readonly Regex IntegerPattern = new(@"^[-+]?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex DecimalPattern = new(@"^[-+]?(?:\d+\.\d*|\d*\.\d+)(?:[eE][-+]?\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex TimestampPrefix = new(@"^\d{4}-\d{2}-\d{2}T", RegexOptions.CultureInvariant);

    public static JsonNode? ToJson(YamlNode? node) => node switch
    {
        null => null,
        YamlMappingNode mapping => MappingToJson(mapping),
        YamlSequenceNode sequence => SequenceToJson(sequence),
        YamlScalarNode scalar => ScalarToJson(scalar),
        _ => JsonValue.Create(node.ToString()),
    };

    public static YamlNode ToYaml(JsonNode? node) => node switch
    {
        null => new YamlScalarNode("null") { Style = ScalarStyle.Plain },
        JsonObject obj => ObjectToYaml(obj),
        JsonArray array => ArrayToYaml(array),
        JsonValue value => ScalarToYaml(value),
        _ => new YamlScalarNode(node.ToJsonString()) { Style = ScalarStyle.DoubleQuoted },
    };

    private static JsonObject MappingToJson(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var pair in mapping.Children)
        {
            var key = pair.Key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : pair.Key.ToString();
            if (key.Length == 0)
                continue;
            result[key] = ToJson(pair.Value);
        }
        return result;
    }

    private static JsonArray SequenceToJson(YamlSequenceNode sequence)
    {
        var result = new JsonArray();
        foreach (var child in sequence.Children)
            result.Add(ToJson(child));
        return result;
    }

    private static JsonNode? ScalarToJson(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
            return null;

        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(value);

        if (value.Length == 0 || value is "~" or "null" or "Null" or "NULL")
            return null;
        if (value is "true" or "True" or "TRUE")
            return JsonValue.Create(true);
        if (value is "false" or "False" or "FALSE")
            return JsonValue.Create(false);
        if (IntegerPattern.IsMatch(value)
            && !(value.Length > 1 && (value[0] == '0' || (value[0] == '-' && value.Length > 2 && value[1] == '0')))
            && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);
        if (DecimalPattern.IsMatch(value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);

        return JsonValue.Create(value);
    }

    private static YamlMappingNode ObjectToYaml(JsonObject obj)
    {
        var mapping = new YamlMappingNode();
        foreach (var pair in obj)
            mapping.Add(pair.Key, ToYaml(pair.Value));
        return mapping;
    }

    private static YamlSequenceNode ArrayToYaml(JsonArray array)
    {
        var sequence = new YamlSequenceNode();
        foreach (var item in array)
            sequence.Add(ToYaml(item));
        return sequence;
    }

    private static YamlScalarNode ScalarToYaml(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text))
        {
            var style = NeedsQuotes(text) ? ScalarStyle.DoubleQuoted : ScalarStyle.Any;
            return new YamlScalarNode(text) { Style = style };
        }
        if (value.TryGetValue<bool>(out var boolean))
            return new YamlScalarNode(boolean ? "true" : "false") { Style = ScalarStyle.Plain };
        if (value.TryGetValue<long>(out var wide))
            return new YamlScalarNode(wide.ToString(CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };
        if (value.TryGetValue<int>(out var integer))
            return new YamlScalarNode(integer.ToString(CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };
        if (value.TryGetValue<double>(out var number))
            return new YamlScalarNode(number.ToString("R", CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };
        return new YamlScalarNode(value.ToJsonString()) { Style = ScalarStyle.Plain };
    }

    private static bool NeedsQuotes(string text)
    {
        if (text.Length == 0)
            return true;
        if (TimestampPrefix.IsMatch(text))
            return true;
        if (text is "true" or "false" or "null" or "~" or "True" or "False" or "Null" or "TRUE" or "FALSE" or "NULL")
            return true;
        if (IntegerPattern.IsMatch(text) || DecimalPattern.IsMatch(text))
            return true;
        if (text[0] is '-' or '?' or ':' or '!' or '&' or '*' or '#' or '{' or '[' or ']' or ',' or '|' or '>' or '@' or '`' or '"' or '\'' or '%')
            return true;
        return text.Contains(" #", StringComparison.Ordinal)
            || text.Contains(": ", StringComparison.Ordinal)
            || text.Contains('\n')
            || text.Contains('\r');
    }
}
