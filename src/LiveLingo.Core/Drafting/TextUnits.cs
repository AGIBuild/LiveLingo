using System.Text;

namespace LiveLingo.Core.Drafting;

/// <summary>Splitting, routing and joining of draft text. The only place that knows sentence and layout rules.</summary>
public static class TextUnits
{
    /// <summary>
    /// Joins unit bodies. Layout whitespace is kept verbatim; two adjacent same-line units get a space only
    /// when the seam is between two ASCII characters (English sentences), never between CJK text.
    /// </summary>
    public static string Join(IEnumerable<(string Lead, string Body, string Trail)> parts)
    {
        var joined = new StringBuilder();
        var previousTrail = "";
        var previousBody = "";
        var first = true;
        foreach (var (lead, body, trail) in parts)
        {
            if (!first && lead.Length == 0 && previousTrail.Length == 0 && NeedsSpace(previousBody, body))
                joined.Append(' ');
            joined.Append(lead).Append(body).Append(trail);
            previousTrail = trail;
            previousBody = body;
            first = false;
        }
        return joined.ToString();
    }

    private static bool NeedsSpace(string left, string right) =>
        left.Length > 0 && right.Length > 0 && IsAsciiEnd(left[^1]) && IsAsciiStart(right[0]);

    private static bool IsAsciiEnd(char c) => c < 128 && (char.IsLetterOrDigit(c) || ".!?,;:)]\"'".Contains(c));

    private static bool IsAsciiStart(char c) => c < 128 && (char.IsLetterOrDigit(c) || "\"'([".Contains(c));
}
