namespace Resonate.Themes.Tests;

public sealed class PlayButtonShadowTests
{
    [Theory]
    [InlineData(ShadowStyle.Soft)]
    [InlineData(ShadowStyle.Strong)]
    public void A_filled_play_button_glows_in_the_accent(ShadowStyle shadow)
    {
        var palette = ThemePalette.From(ThemePresets.Midnight with { Shadow = shadow, PlayButton = PlayButtonStyle.Filled });
        var glow = palette.PlayButtonShadow;

        Assert.True(glow.IsVisible);
        Assert.Equal(palette.Accent, glow.Color.Opaque);
        Assert.True(glow.BlurRadius > 0);
        Assert.True(glow.OffsetY > 0, "a filled button's glow falls a little below it");
    }

    [Fact]
    public void The_glow_follows_the_cover_s_accent()
    {
        var cover = ThemeColor.FromRgb(0x2EB8FF);
        var palette = ThemePalette.From(ThemePresets.Glass, cover);
        Assert.Equal(cover, palette.PlayButtonShadow.Color.Opaque);
    }

    [Fact]
    public void A_light_look_glows_more_gently()
    {
        var light = ThemePalette.From(ThemePresets.Daylight).PlayButtonShadow;
        var dark = ThemePalette.From(ThemePresets.Midnight).PlayButtonShadow;
        Assert.True(light.Color.Opacity < dark.Color.Opacity);
    }

    [Fact]
    public void An_outline_button_glows_straight_behind_itself()
    {
        var glow = ThemePalette.From(ThemePresets.Midnight with { PlayButton = PlayButtonStyle.Outline }).PlayButtonShadow;
        Assert.True(glow.IsVisible);
        Assert.Equal(0, glow.OffsetY);
    }

    [Fact]
    public void Glowing_looks_get_neon()
    {
        var palette = ThemePalette.From(ThemePresets.Synthwave);
        Assert.Equal(palette.ItemShadow, palette.PlayButtonShadow);
        Assert.Equal(palette.Accent, palette.PlayButtonShadow.Color.Opaque);
        Assert.Equal(0, palette.PlayButtonShadow.OffsetY);
    }

    [Fact]
    public void Printed_looks_get_a_hard_offset_copy()
    {
        var palette = ThemePalette.From(ThemePresets.Paper);
        var shadow = palette.PlayButtonShadow;
        Assert.Equal(palette.ItemShadow, shadow);
        Assert.Equal(0, shadow.BlurRadius);
        Assert.True(shadow.OffsetX > 0 && shadow.OffsetY > 0);
    }

    [Fact]
    public void Looks_without_shadows_draw_nothing() =>
        Assert.False(ThemePalette.From(ThemePresets.PureBlack).PlayButtonShadow.IsVisible);

    [Theory]
    [InlineData(ShadowStyle.Soft)]
    [InlineData(ShadowStyle.Glow)]
    [InlineData(ShadowStyle.Hard)]
    public void A_plain_play_button_has_nothing_behind_it(ShadowStyle shadow) =>
        Assert.False(ThemePalette.From(ThemePresets.Midnight with { Shadow = shadow, PlayButton = PlayButtonStyle.Plain }).PlayButtonShadow.IsVisible);

    [Theory]
    [InlineData("midnight")]
    [InlineData("daylight")]
    [InlineData("glass")]
    public void The_everyday_presets_glow_in_their_accent(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        Assert.True(palette.PlayButtonShadow.IsVisible);
        Assert.Equal(palette.Accent, palette.PlayButtonShadow.Color.Opaque);
    }
}
