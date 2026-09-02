using System.Globalization;
using System.Text;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// The reference a customer quotes and a kitchen calls out.
///
/// <para>
/// Three parts: which restaurant, which day, and which order of that day. The counter is what
/// gets shouted across a kitchen — "order forty-two" — and it resets daily so it stays short
/// enough to say. The rest is what makes the string mean something to support six months later.
/// </para>
/// </summary>
public static class OrderNumbers
{
    /// <summary>Matches the column, which is nvarchar(32).</summary>
    public const int MaxLength = 32;

    /// <summary>
    /// How much of the slug is kept. Ten characters distinguishes any two restaurants a person
    /// would plausibly confuse, while leaving the date and counter room inside the column.
    /// </summary>
    public const int SlugChars = 10;

    /// <summary>
    /// Builds the reference, e.g. <c>FRIESLAB-260902-042</c>.
    ///
    /// <para>
    /// Uniqueness comes from the sequence, which is per restaurant and per day, so a number can
    /// only repeat if two <em>different</em> restaurants share the first ten characters of their
    /// slug and reach the same count on the same date. The unique index on OrderNumber is the
    /// backstop if that ever happens; it is not a case worth adding a retry loop for.
    /// </para>
    /// </summary>
    public static string Format(string restaurantSlug, DateOnly businessDate, int sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restaurantSlug);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);

        var prefix = Shorten(restaurantSlug);
        var day = businessDate.ToString("yyMMdd", CultureInfo.InvariantCulture);

        // Three digits is a busy day at one restaurant; a fourth simply appears if it is ever
        // needed rather than the number wrapping or being truncated.
        var number = sequence.ToString("D3", CultureInfo.InvariantCulture);

        return $"{prefix}-{day}-{number}";
    }

    /// <summary>
    /// Letters and digits only, uppercased.
    ///
    /// Slugs carry hyphens, and a hyphen inside the first part would make the reference read as
    /// though it had four sections instead of three.
    /// </summary>
    private static string Shorten(string slug)
    {
        var builder = new StringBuilder(SlugChars);

        foreach (var character in slug)
        {
            if (builder.Length == SlugChars)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        // A slug of nothing but punctuation cannot happen — they are generated from names — but
        // an empty prefix would produce a reference starting with a hyphen, so it is named.
        return builder.Length > 0 ? builder.ToString() : "ORDER";
    }
}
