namespace Resonate.Themes.Skins;

/// <summary>The scrolling song line of the classic player.</summary>
public static class Marquee
{
    /// <summary>Character cells in the marquee (154 pixels at 5 each: 30 whole ones and the edge of one more).</summary>
    public const int Cells = 31;

    /// <summary>What separates the end of a scrolling line from its start again.</summary>
    public const string Gap = "  ***  ";

    /// <summary>How long the marquee waits between one-character steps.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(220);

    /// <summary>"Artist - Title (3:45)": the song as the marquee writes it.</summary>
    public static string Line(string? artist, string? title, TimeSpan duration) => throw new NotImplementedException();

    /// <summary>True when the line is too long to stand still.</summary>
    public static bool Scrolls(string line) => throw new NotImplementedException();

    /// <summary>The <see cref="Cells"/> characters shown after scrolling <paramref name="offset"/> steps.</summary>
    public static string Window(string line, int offset) => throw new NotImplementedException();
}
