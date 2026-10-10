namespace Resonate.Themes.Tests;

public sealed class AppKeysTests
{
    [Fact]
    public void Space_plays_and_pauses_until_the_user_picks_other_keys()
    {
        var table = AppKeys.Table(null);
        Assert.Equal(AppCommand.PlayPause, table[KeyCombo.Parse("Space")!.Value]);
        Assert.Equal(AppCommand.Search, table[KeyCombo.Parse("Ctrl+L")!.Value]);
        Assert.Equal(AppCommand.AppSizeUp, table[KeyCombo.Parse("Ctrl+Shift+Equal")!.Value]);
        Assert.Equal(AppCommand.Back, table[KeyCombo.Parse("Alt+Left")!.Value]);
        Assert.Equal(AppCommand.Forward, table[KeyCombo.Parse("Alt+Right")!.Value]);
    }

    [Fact]
    public void Every_command_is_listed_once_and_no_two_share_default_keys()
    {
        Assert.Equal(Enum.GetValues<AppCommand>().Length, AppKeys.Commands.Select(c => c.Command).Distinct().Count());
        var keys = Enum.GetValues<AppCommand>().SelectMany(AppKeys.Defaults).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Null(AppKeys.Problem(k)));
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", true, false, true, "S")]
    [InlineData("Space", false, false, false, "Space")]
    [InlineData("alt+left", false, true, false, "left")]
    public void Keys_are_read_and_written_as_text(string text, bool control, bool alt, bool shift, string key)
    {
        var combo = KeyCombo.Parse(text)!.Value;
        Assert.Equal(new KeyCombo(control, alt, shift, key), combo);
        Assert.Equal(combo, KeyCombo.Parse(combo.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Win+K")]
    [InlineData("Ctrl+")]
    public void Unreadable_keys_are_none(string text) => Assert.Null(KeyCombo.Parse(text));

    [Fact]
    public void Keys_given_to_a_command_are_taken_from_the_one_that_had_them()
    {
        var own = new Dictionary<string, string>();
        var taken = AppKeys.Assign(own, AppCommand.Like, KeyCombo.Parse("Ctrl+S"));

        Assert.Equal(AppCommand.Shuffle, taken);
        Assert.Empty(AppKeys.For(AppCommand.Shuffle, own));
        Assert.Equal(AppCommand.Like, AppKeys.Table(own)[KeyCombo.Parse("Ctrl+S")!.Value]);
        Assert.True(AppKeys.IsChanged(AppCommand.Shuffle, own));

        // Putting Shuffle back takes Ctrl+S from Like again.
        Assert.Equal(AppCommand.Like, AppKeys.Reset(own, AppCommand.Shuffle));
        Assert.False(AppKeys.IsChanged(AppCommand.Shuffle, own));
        Assert.Empty(AppKeys.For(AppCommand.Like, own));
    }

    [Fact]
    public void Taking_one_of_several_keys_leaves_the_others()
    {
        var own = new Dictionary<string, string>();
        AppKeys.Assign(own, AppCommand.Queue, KeyCombo.Parse("Ctrl+L"));
        Assert.Equal([KeyCombo.Parse("Ctrl+F")!.Value], AppKeys.For(AppCommand.Search, own));
    }

    [Fact]
    public void A_command_can_have_no_keys()
    {
        var own = new Dictionary<string, string>();
        AppKeys.Assign(own, AppCommand.PlayPause, null);
        Assert.Empty(AppKeys.For(AppCommand.PlayPause, own));
        Assert.False(AppKeys.Table(own).ContainsKey(KeyCombo.Parse("Space")!.Value));
        Assert.Equal("None", AppKeys.Display(AppKeys.For(AppCommand.PlayPause, own)));
    }

    [Theory]
    [InlineData("Space", true)]
    [InlineData("Shift+Right", true)]
    [InlineData("Ctrl+Left", true)]
    [InlineData("Ctrl+V", true)]
    [InlineData("Ctrl+F", false)]
    [InlineData("Ctrl+S", false)]
    [InlineData("Alt+Left", false)]
    public void Keys_used_for_typing_stay_with_a_text_field(string text, bool belongs) =>
        Assert.Equal(belongs, AppKeys.BelongsToText(KeyCombo.Parse(text)!.Value));

    [Theory]
    [InlineData("Tab")]
    [InlineData("Enter")]
    [InlineData("Escape")]
    [InlineData("Alt+F4")]
    [InlineData("Alt+Space")]
    public void Keys_windows_and_lists_need_cannot_be_taken(string text) =>
        Assert.NotNull(AppKeys.Problem(KeyCombo.Parse(text)!.Value));

    [Theory]
    [InlineData("Ctrl+Equal", "Ctrl+Plus")]
    [InlineData("Ctrl+NumberPad0", "Ctrl+Num 0")]
    [InlineData("Shift+Right", "Shift+Right")]
    public void Keys_read_as_people_name_them(string text, string shown) =>
        Assert.Equal(shown, AppKeys.Display(KeyCombo.Parse(text)!.Value));
}
