using System.Text;

namespace TaskBaton.Protocol;

internal static class MarkdownSections
{
    public static IReadOnlyDictionary<string, string> Parse(string body)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        string? current = null;
        var buffer = new StringBuilder();
        string? fence = null;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal))
                    fence = null;
                if (current is not null)
                    buffer.Append(line).Append('\n');
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = trimmed[..3];
                if (current is not null)
                    buffer.Append(line).Append('\n');
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush(sections, current, buffer);
                current = trimmed[3..].Trim();
                buffer.Clear();
                continue;
            }

            if (current is not null)
                buffer.Append(line).Append('\n');
        }

        Flush(sections, current, buffer);
        return sections;
    }

    private static void Flush(Dictionary<string, string> sections, string? current, StringBuilder buffer)
    {
        if (current is null)
            return;
        sections[current] = buffer.ToString().Trim();
    }
}
