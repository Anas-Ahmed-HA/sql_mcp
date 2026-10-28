using System.Text.RegularExpressions;

namespace SqlMcp.Server.Metadata;

// Splitting Markdown into heading-delimited sections.
public sealed partial class MetadataDocument
{
    public static MetadataDocument Parse(string connectionName, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        // ATX headings only, and never inside fenced code blocks (a "# comment" in a SQL sample isn't a heading).
        var headings = new List<(int Line, int Level, string Title)>();
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence && HeadingPattern().Match(lines[i]) is { Success: true } match)
            {
                headings.Add((i, match.Groups[1].Length, match.Groups[2].Value.Trim()));
            }
        }

        var sections = new List<MetadataSection>();
        for (var h = 0; h < headings.Count; h++)
        {
            var (start, level, title) = headings[h];
            var end = lines.Length;
            for (var next = h + 1; next < headings.Count; next++)
            {
                if (headings[next].Level <= level)
                {
                    end = headings[next].Line;
                    break;
                }
            }

            sections.Add(new MetadataSection(title, level, string.Join('\n', lines[start..end]).TrimEnd()));
        }

        return new MetadataDocument(connectionName, markdown, sections);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();
}
