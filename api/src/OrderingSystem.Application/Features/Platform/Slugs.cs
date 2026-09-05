using System.Globalization;
using System.Text;

namespace OrderingSystem.Application.Features.Platform;

/// <summary>
/// Turning a restaurant's name into the piece of a link a customer sees.
/// </summary>
internal static class Slugs
{
    /// <summary>
    /// "Beirut Mezze House" to "beirut-mezze-house".
    ///
    /// <para>
    /// Accents are stripped rather than kept, so a name written "Café" produces "cafe" — an
    /// address bar showing "caf%C3%A9" is nobody's idea of a tidy link. Everything else that is
    /// not a letter or a digit becomes a hyphen, and runs of them collapse, so punctuation and
    /// spacing cannot produce "saj--corner-".
    /// </para>
    /// <para>
    /// A name in Arabic or any other non-Latin script comes out empty, which is why the caller
    /// has to handle that rather than ship a restaurant whose link is nothing. Transliterating
    /// would be guessing at a name somebody else has to live with.
    /// </para>
    /// </summary>
    public static string From(string name, int maxLength)
    {
        var withoutAccents = new StringBuilder(name.Length);

        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                withoutAccents.Append(ch);
            }
        }

        var slug = new StringBuilder(withoutAccents.Length);

        foreach (var ch in withoutAccents.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                slug.Append(ch);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var trimmed = slug.ToString().TrimEnd('-');

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd('-');
    }
}
