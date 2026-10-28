namespace SqlMcp.Server.Catalog;

// Splitting a multi-part name into its parts, without brackets or quotes.
public readonly partial record struct ObjectName
{
    /// <summary>"a", "a.b", "[a].[b]" or "\"a\".\"b\"" into parts; null when the name is malformed.</summary>
    private static List<string>? Split(string input)
    {
        var parts = new List<string>();
        var s = input.Trim();
        var i = 0;

        while (true)
        {
            string part;
            if (i < s.Length && s[i] is '[' or '"')
            {
                var close = s[i] == '[' ? ']' : '"';
                var end = s.IndexOf(close, i + 1);
                if (end < 0)
                {
                    return null;
                }

                part = s[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                var end = s.IndexOf('.', i);
                if (end < 0)
                {
                    end = s.Length;
                }

                part = s[i..end].Trim();
                i = end;
            }

            if (part.Length == 0)
            {
                return null;
            }

            parts.Add(part);

            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i == s.Length)
            {
                return parts;
            }

            if (s[i] != '.')
            {
                return null;
            }

            i++;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
