using System.Globalization;

namespace Resonate.Spotify.History;

/// <summary>Small pieces of text Home makes from names: the listener's first name, an artist's initials.</summary>
public static class HomeText
{
    /// <summary>
    /// The first word of a Spotify display name, for the greeting ("Good
    /// evening, Sam"); null for no name, or one that is only digits or
    /// symbols (an account number reads badly in a greeting).
    /// </summary>
    public static string? FirstName(string? displayName)
    {
        var first = Words(displayName).FirstOrDefault();
        return first is not null && first.Any(char.IsLetter) ? first : null;
    }

    /// <summary>
    /// Up to two letters for a picture that has not loaded: the first of the
    /// first word and of the last ("Mira Sol" is "MS", "Lumen" is "L"). Marks
    /// such as brackets are skipped; empty when the name has no letters or digits.
    /// </summary>
    public static string Initials(string? name)
    {
        var letters = Words(name).Select(FirstLetter).OfType<string>().ToList();
        var initials = letters.Count switch
        {
            0 => string.Empty,
            1 => letters[0],
            _ => letters[0] + letters[^1],
        };

        return initials.ToUpper(CultureInfo.CurrentCulture);
    }

    private static string[] Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The word's first letter or digit, whole even when it takes two characters.</summary>
    private static string? FirstLetter(string word)
    {
        var elements = StringInfo.GetTextElementEnumerator(word);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (char.IsLetterOrDigit(element, 0))
            {
                return element;
            }
        }

        return null;
    }
}
