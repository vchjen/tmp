using System.Globalization;
using System.Text;

public static string CleanTicker(string input)
{
    if (input == null) return null!;

    // Normalize (helps with some compatibility chars)
    var s = input.Normalize(NormalizationForm.FormKC);

    var sb = new StringBuilder(s.Length);
    foreach (var ch in s)
    {
        // Drop replacement character � (U+FFFD)
        if (ch == '\uFFFD') continue;

        // Convert weird spaces to normal space
        if (ch is '\u00A0' or '\u2007' or '\u202F') { sb.Append(' '); continue; }

        var cat = CharUnicodeInfo.GetUnicodeCategory(ch);

        // Drop zero-width/invisible format + control chars
        if (cat == UnicodeCategory.Format || cat == UnicodeCategory.Control) continue;

        // Normalize other whitespace to space
        if (char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }

        sb.Append(ch);
    }

    return sb.ToString().Trim();
}
