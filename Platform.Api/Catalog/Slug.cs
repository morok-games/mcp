using System.Globalization;
using System.Text;

namespace Platform.Api.Catalog;

public static class Slug
{
    /// <summary>"6-Month Double Axel Club" -> "6-month-double-axel-club".</summary>
    public static string From(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var pendingDash = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // strip diacritics: "Toruń" -> "torun"

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && sb.Length > 0)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return sb.ToString();
    }
}
