using System.Text.RegularExpressions;

namespace SqlMcp.Server.Metadata;

// Reading Markdown tables: the Allowed views list and each view's Columns list.
public sealed partial class MetadataDocument
{
    private IReadOnlyList<string>? ReadAllowedViews()
    {
        var section = Sections.FirstOrDefault(s => s.Level == 2 && Eq(s.Title, AllowedViewsHeading));
        return section is null ? null : FirstTableColumn(section.Markdown);
    }

    /// <summary>
    /// The first cell of every body row of the tables in the text, without backticks. Header and separator
    /// rows are skipped, and so are tables inside fenced code blocks (an example is not a whitelist).
    /// </summary>
    private static List<string> FirstTableColumn(string markdown)
    {
        var cells = new List<string>();
        var inFence = false;
        var inTable = false;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                inTable = false;
                continue;
            }

            if (inFence || !line.StartsWith('|'))
            {
                inTable = false;
                continue;
            }

            if (!inTable)
            {
                inTable = true; // header row
                continue;
            }

            if (SeparatorRow().IsMatch(line))
            {
                continue;
            }

            var first = line.Trim('|').Split('|')[0].Replace("`", string.Empty).Trim();
            if (first.Length > 0)
            {
                cells.Add(first);
            }
        }

        return cells;
    }

    [GeneratedRegex(@"^\|?\s*:?-{3,}")]
    private static partial Regex SeparatorRow();
}
