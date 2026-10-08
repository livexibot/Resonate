namespace Resonate.Themes.Tests;

public sealed class PlayerLayoutTests
{
    private static ThemeDefinition Hovering(ThemeDefinition look) => look with { PlayerLayout = PlayerLayout.Hovering };

    [Fact]
    public void A_hovering_look_survives_being_copied_as_text()
    {
        var look = Hovering(ThemePresets.Midnight);
        var text = ThemeJson.Export(look);

        Assert.Contains("\"playerLayout\": \"Hovering\"", text, StringComparison.Ordinal);
        Assert.Equal(look, ThemeJson.Import(text));
    }

    [Fact]
    public void An_unknown_layout_number_becomes_docked() =>
        Assert.Equal(PlayerLayout.Docked, (ThemePresets.Midnight with { PlayerLayout = (PlayerLayout)99 }).Normalize().PlayerLayout);

    [Fact]
    public void An_unknown_layout_name_is_refused() =>
        Assert.Null(ThemeJson.Import("{\"resonateLook\": 1, \"look\": {\"accent\": \"#FFFFFF\", \"playerLayout\": \"Sideways\"}}"));

    [Fact]
    public void Hovering_is_a_change_of_structure()
    {
        Assert.False(ThemePresets.Midnight.HasSameStructure(Hovering(ThemePresets.Midnight)));
        Assert.False((ThemePresets.Midnight with { PlayerLayout = PlayerLayout.Floating }).HasSameStructure(Hovering(ThemePresets.Midnight)));
    }

    [Fact]
    public void Liquid_glass_shows_the_hovering_player() =>
        Assert.Equal(PlayerLayout.Hovering, ThemePresets.Glass.PlayerLayout);

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void A_hovering_player_stays_readable_over_anything_scrolling_under_it(string id)
    {
        var palette = ThemePalette.From(Hovering(ThemePresets.Find(id)!));
        var page = palette.Surface.Over(palette.Background);

        Assert.True(palette.Player.Opacity >= ThemePalette.HoveringPlayerOpacity - 0.005, $"fill is {palette.Player.Opacity:0.00} opaque");
        foreach (var under in new[] { ThemeColor.Black, ThemeColor.White, page, palette.TextPrimary, palette.Accent })
        {
            var seen = palette.Player.Over(under);
            Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, seen) >= 4.5, $"main text over {under}");
        }
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Docked_and_floating_players_keep_the_look_s_own_fill(string id)
    {
        var preset = ThemePresets.Find(id)!;
        foreach (var layout in new[] { PlayerLayout.Docked, PlayerLayout.Floating, PlayerLayout.Top, PlayerLayout.FloatingTop })
        {
            var look = preset with { PlayerLayout = layout };
            Assert.Equal(look.Player.Opaque.WithAlpha(look.PanelOpacity), ThemePalette.From(look).Player);
        }
    }

    [Fact]
    public void A_solid_hovering_player_stays_its_own_colour()
    {
        var look = Hovering(ThemePresets.Daylight);
        Assert.Equal(look.Player, ThemePalette.From(look).Player);
    }

    [Theory]
    [InlineData(5120, PlayerWidthClass.Full)]
    [InlineData(912, PlayerWidthClass.Full)]
    [InlineData(911.6, PlayerWidthClass.Full)]
    [InlineData(911, PlayerWidthClass.Compact)]
    [InlineData(612, PlayerWidthClass.Compact)]
    [InlineData(600, PlayerWidthClass.Compact)]
    [InlineData(599, PlayerWidthClass.Mini)]
    [InlineData(380, PlayerWidthClass.Mini)]
    public void The_bar_s_width_picks_how_much_it_shows(double width, PlayerWidthClass expected) =>
        Assert.Equal(expected, PlayerPlacement.WidthClassFor(width));

    [Fact]
    public void A_hovering_player_at_its_widest_shows_everything() =>
        Assert.Equal(PlayerWidthClass.Full, PlayerPlacement.WidthClassFor(PlayerPlacement.HoveringMaxWidth));

    [Fact]
    public void Only_the_mini_bar_is_shorter()
    {
        Assert.Equal(PlayerPlacement.BarHeight, PlayerPlacement.HeightFor(PlayerWidthClass.Full));
        Assert.Equal(PlayerPlacement.BarHeight, PlayerPlacement.HeightFor(PlayerWidthClass.Compact));
        Assert.True(PlayerPlacement.HeightFor(PlayerWidthClass.Mini) < PlayerPlacement.BarHeight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(24)]
    public void A_docked_player_reaches_the_window_s_edges(double gap)
    {
        var slot = PlayerPlacement.Slot(PlayerLayout.Docked, gap, sidebarFullHeight: false);
        var player = PlayerPlacement.Margin(PlayerLayout.Docked, gap);

        Assert.Equal(PlayerPlacement.BottomRow, slot.Row);
        Assert.False(slot.StartsAtContent);
        Assert.True(slot.SpansFollowingColumns);

        // The shell's padding is the gap on the left, right and bottom.
        Assert.Equal(-gap, slot.Margin.Left + player.Left);
        Assert.Equal(-gap, slot.Margin.Right + player.Right);
        Assert.Equal(-gap, slot.Margin.Bottom + player.Bottom);

        // As far below the panels as they are apart, as before.
        Assert.Equal(gap, slot.Margin.Top + player.Top);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void A_floating_player_lines_up_with_the_panels(double gap)
    {
        var slot = PlayerPlacement.Slot(PlayerLayout.Floating, gap, sidebarFullHeight: false);
        var player = PlayerPlacement.Margin(PlayerLayout.Floating, gap);

        Assert.Equal(0, slot.Margin.Left + player.Left);
        Assert.Equal(0, slot.Margin.Right + player.Right);
        Assert.Equal(0, slot.Margin.Bottom + player.Bottom);
    }

    [Fact]
    public void A_floating_player_keeps_off_the_edge_even_without_gaps() =>
        Assert.Equal(8, PlayerPlacement.Margin(PlayerLayout.Floating, 0).Left);

    [Theory]
    [InlineData(0, false)]
    [InlineData(12, false)]
    [InlineData(24, true)]
    public void A_hovering_player_stays_inside_the_page_s_column(double gap, bool sidebarFullHeight)
    {
        var slot = PlayerPlacement.Slot(PlayerLayout.Hovering, gap, sidebarFullHeight);
        var player = PlayerPlacement.Margin(PlayerLayout.Hovering, gap);

        Assert.Equal(PlayerPlacement.PanelsRow, slot.Row);
        Assert.True(slot.StartsAtContent);
        Assert.False(slot.SpansFollowingColumns);
        Assert.True(slot.AlignBottom);
        Assert.Equal(EdgeInsets.Zero, slot.Margin);
        Assert.True(player.Left >= 12 && player.Right >= 12 && player.Bottom >= 12);
        Assert.True(player.Left >= gap);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(912, 360, true)]
    [InlineData(384, 360, true)]
    [InlineData(383, 360, false)]
    [InlineData(272, 360, false)]
    [InlineData(560, 545, false)]
    public void A_hovering_player_needs_its_width_and_a_gap_on_each_side(double pageWidth, double playerWidth, bool fits) =>
        Assert.Equal(fits, PlayerPlacement.HoveringFits(pageWidth, playerWidth, panelGap: 10));

    [Theory]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(24, true)]
    public void A_hovering_player_too_wide_for_the_page_sits_under_the_panels(double gap, bool sidebarFullHeight)
    {
        var slot = PlayerPlacement.Slot(PlayerLayout.Hovering, gap, sidebarFullHeight, hoveringFits: false);
        var floating = PlayerPlacement.Slot(PlayerLayout.Floating, gap, sidebarFullHeight);
        var player = PlayerPlacement.Margin(PlayerLayout.Hovering, gap);

        // In the row under the panels, where a floating player goes, so it covers nothing.
        Assert.Equal(PlayerPlacement.BottomRow, slot.Row);
        Assert.False(slot.AlignBottom);
        Assert.Equal(floating.StartsAtContent, slot.StartsAtContent);
        Assert.True(slot.SpansFollowingColumns);
        Assert.Equal(floating.Margin.Top, slot.Margin.Top);

        // Its own (hovering) margin keeps it clear of the edges as over the page.
        Assert.Equal(sidebarFullHeight ? 0 : -gap + player.Left, slot.Margin.Left + player.Left);
        Assert.Equal(-gap + player.Bottom, slot.Margin.Bottom + player.Bottom);
    }

    [Theory]
    [InlineData(PlayerLayout.Docked, 0)]
    [InlineData(PlayerLayout.Floating, 0)]
    public void With_the_sidebar_reaching_the_bottom_the_player_sits_under_the_page(PlayerLayout layout, double expectedLeft)
    {
        const double Gap = 10;
        var normal = PlayerPlacement.Slot(layout, Gap, sidebarFullHeight: false);
        var slot = PlayerPlacement.Slot(layout, Gap, sidebarFullHeight: true);
        var player = PlayerPlacement.Margin(layout, Gap);

        Assert.Equal(2, slot.SidebarRowSpan);
        Assert.Equal(1, normal.SidebarRowSpan);
        Assert.Equal(PlayerPlacement.PanelsRow, slot.SidebarRow);
        Assert.Equal(PlayerPlacement.BottomRow, slot.Row);
        Assert.True(slot.StartsAtContent);

        // Under the page and the queue: it starts where the page starts...
        Assert.True(slot.SpansFollowingColumns);
        Assert.Equal(expectedLeft, slot.Margin.Left + player.Left);

        // ...and the other edges are where they always were.
        Assert.Equal(normal.Margin.Right, slot.Margin.Right);
        Assert.Equal(normal.Margin.Bottom, slot.Margin.Bottom);
        Assert.Equal(normal.Margin.Top, slot.Margin.Top);
    }

    [Fact]
    public void Each_layout_has_its_own_outline()
    {
        Assert.Equal(new EdgeInsets(0, 1.5, 0, 0), PlayerPlacement.Outline(PlayerLayout.Docked, 1.5));
        Assert.Equal(EdgeInsets.All(1.5), PlayerPlacement.Outline(PlayerLayout.Floating, 1.5));
        Assert.Equal(EdgeInsets.All(0), PlayerPlacement.Outline(PlayerLayout.Floating, 0));

        // Over the page, a hairline at least, so it has an edge in looks without shadows.
        Assert.Equal(EdgeInsets.All(1), PlayerPlacement.Outline(PlayerLayout.Hovering, 0));
        Assert.Equal(EdgeInsets.All(2), PlayerPlacement.Outline(PlayerLayout.Hovering, 2));
    }

    [Theory]
    [InlineData(ButtonShape.Round, 16, 88, 44)]
    [InlineData(ButtonShape.Round, 16, 72, 36)]
    [InlineData(ButtonShape.Rounded, 16, 88, 24)]
    [InlineData(ButtonShape.Rounded, 32, 88, 44)]
    [InlineData(ButtonShape.Rounded, 32, 72, 36)]
    [InlineData(ButtonShape.Square, 0, 88, 0)]
    public void A_hovering_player_is_a_pill_with_round_buttons_and_never_an_oval(ButtonShape buttons, double cornerLarge, double height, double expected) =>
        Assert.Equal(expected, PlayerPlacement.Corner(PlayerLayout.Hovering, buttons, cornerLarge, height));

    [Fact]
    public void Docked_players_are_square_and_floating_ones_follow_the_panels()
    {
        Assert.Equal(0, PlayerPlacement.Corner(PlayerLayout.Docked, ButtonShape.Round, 16, 88));
        Assert.Equal(16, PlayerPlacement.Corner(PlayerLayout.Floating, ButtonShape.Round, 16, 88));
        Assert.Equal(4, PlayerPlacement.Corner(PlayerLayout.Floating, ButtonShape.Square, 0, 88));
    }

    [Fact]
    public void Only_a_hovering_player_has_a_width_limit()
    {
        Assert.Equal(PlayerPlacement.HoveringMaxWidth, PlayerPlacement.MaxWidth(PlayerLayout.Hovering));
        Assert.True(double.IsPositiveInfinity(PlayerPlacement.MaxWidth(PlayerLayout.Docked)));
        Assert.True(double.IsPositiveInfinity(PlayerPlacement.MaxWidth(PlayerLayout.Floating)));
    }

    [Fact]
    public void Pages_leave_room_only_under_a_hovering_player()
    {
        Assert.Equal(108, PlayerPlacement.PageInset(PlayerLayout.Hovering, 99.6));
        Assert.Equal(0, PlayerPlacement.PageInset(PlayerLayout.Hovering, 0));
        Assert.Equal(0, PlayerPlacement.PageInset(PlayerLayout.Docked, 96));
        Assert.Equal(0, PlayerPlacement.PageInset(PlayerLayout.Floating, 104));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(24)]
    public void A_player_on_top_reaches_the_window_s_edges_under_the_title_bar(double gap)
    {
        var slot = PlayerPlacement.Slot(PlayerLayout.Top, gap, sidebarFullHeight: false);
        var player = PlayerPlacement.Margin(PlayerLayout.Top, gap);

        Assert.Equal(PlayerPlacement.TopRow, slot.Row);
        Assert.False(slot.StartsAtContent);
        Assert.True(slot.SpansFollowingColumns);
        Assert.Equal(PlayerPlacement.PanelsRow, slot.SidebarRow);
        Assert.Equal(1, slot.SidebarRowSpan);

        // The shell has no padding at the top (the title bar is there); the gap on the sides.
        Assert.Equal(-gap, slot.Margin.Left + player.Left);
        Assert.Equal(-gap, slot.Margin.Right + player.Right);
        Assert.Equal(0, slot.Margin.Top + player.Top);

        // As far above the panels as they are apart.
        Assert.Equal(gap, slot.Margin.Bottom + player.Bottom);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    public void A_floating_player_on_top_lines_up_with_the_panels(double gap)
    {
        var slot = PlayerPlacement.Slot(PlayerLayout.FloatingTop, gap, sidebarFullHeight: false);
        var player = PlayerPlacement.Margin(PlayerLayout.FloatingTop, gap);

        Assert.Equal(PlayerPlacement.TopRow, slot.Row);
        Assert.Equal(0, slot.Margin.Left + player.Left);
        Assert.Equal(0, slot.Margin.Right + player.Right);
        Assert.Equal(gap, slot.Margin.Bottom + player.Bottom);
        Assert.Equal(8, PlayerPlacement.Margin(PlayerLayout.FloatingTop, 0).Left);
    }

    [Theory]
    [InlineData(PlayerLayout.Top, 0)]
    [InlineData(PlayerLayout.FloatingTop, 0)]
    public void With_the_sidebar_reaching_the_edge_a_player_on_top_sits_over_the_page(PlayerLayout layout, double expectedLeft)
    {
        const double Gap = 10;
        var normal = PlayerPlacement.Slot(layout, Gap, sidebarFullHeight: false);
        var slot = PlayerPlacement.Slot(layout, Gap, sidebarFullHeight: true);
        var player = PlayerPlacement.Margin(layout, Gap);

        // The sidebar starts in the top row and runs down beside the player.
        Assert.Equal(PlayerPlacement.TopRow, slot.SidebarRow);
        Assert.Equal(2, slot.SidebarRowSpan);
        Assert.Equal(PlayerPlacement.TopRow, slot.Row);
        Assert.True(slot.StartsAtContent);
        Assert.True(slot.SpansFollowingColumns);
        Assert.Equal(expectedLeft, slot.Margin.Left + player.Left);
        Assert.Equal(normal.Margin.Right, slot.Margin.Right);
        Assert.Equal(normal.Margin.Top, slot.Margin.Top);
        Assert.Equal(normal.Margin.Bottom, slot.Margin.Bottom);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(24, true)]
    public void A_player_in_the_corner_hovers_over_the_page_like_the_pill(double gap, bool sidebarFullHeight)
    {
        Assert.Equal(PlayerPlacement.Slot(PlayerLayout.Hovering, gap, sidebarFullHeight), PlayerPlacement.Slot(PlayerLayout.Corner, gap, sidebarFullHeight));
        Assert.Equal(PlayerPlacement.Margin(PlayerLayout.Hovering, gap), PlayerPlacement.Margin(PlayerLayout.Corner, gap));
        Assert.True(PlayerPlacement.HoversOverPage(PlayerLayout.Corner));
        Assert.Equal(PlayerPlacement.BottomRow, PlayerPlacement.Slot(PlayerLayout.Corner, gap, sidebarFullHeight, hoveringFits: false).Row);
    }

    [Fact]
    public void A_player_in_the_corner_is_the_mini_bar()
    {
        Assert.Equal(PlayerWidthClass.Mini, PlayerPlacement.WidthClassFor(PlayerPlacement.MaxWidth(PlayerLayout.Corner)));
        Assert.True(PlayerPlacement.CornerWidth >= PlayerPlacement.HoveringMinWidth);
        Assert.Equal(108, PlayerPlacement.PageInset(PlayerLayout.Corner, 99.6));
        Assert.Equal(0, PlayerPlacement.PageInset(PlayerLayout.Top, 96));
        Assert.Equal(0, PlayerPlacement.PageInset(PlayerLayout.FloatingTop, 104));
    }

    [Fact]
    public void Players_on_top_face_the_panels_with_their_outline_and_corners()
    {
        Assert.Equal(new EdgeInsets(0, 0, 0, 1.5), PlayerPlacement.Outline(PlayerLayout.Top, 1.5));
        Assert.Equal(EdgeInsets.All(1.5), PlayerPlacement.Outline(PlayerLayout.FloatingTop, 1.5));
        Assert.Equal(EdgeInsets.All(1), PlayerPlacement.Outline(PlayerLayout.Corner, 0));
        Assert.Equal(0, PlayerPlacement.Corner(PlayerLayout.Top, ButtonShape.Round, 16, 88));
        Assert.Equal(16, PlayerPlacement.Corner(PlayerLayout.FloatingTop, ButtonShape.Round, 16, 88));
        Assert.Equal(36, PlayerPlacement.Corner(PlayerLayout.Corner, ButtonShape.Round, 16, 72));
        Assert.False(PlayerPlacement.Floats(PlayerLayout.Top));
        Assert.True(PlayerPlacement.Floats(PlayerLayout.FloatingTop));
        Assert.True(PlayerPlacement.Floats(PlayerLayout.Corner));
    }

    [Fact]
    public void The_new_layouts_survive_being_copied_as_text()
    {
        foreach (var layout in new[] { PlayerLayout.Top, PlayerLayout.FloatingTop, PlayerLayout.Corner })
        {
            var look = ThemePresets.Midnight with { PlayerLayout = layout };
            Assert.Equal(look, ThemeJson.Import(ThemeJson.Export(look)));
            Assert.False(ThemePresets.Midnight.HasSameStructure(look));
        }
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void A_player_in_the_corner_stays_readable_over_anything_scrolling_under_it(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)! with { PlayerLayout = PlayerLayout.Corner });
        foreach (var under in new[] { ThemeColor.Black, ThemeColor.White, palette.TextPrimary, palette.Accent })
        {
            Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, palette.Player.Over(under)) >= 4.5, $"main text over {under}");
        }
    }
}
