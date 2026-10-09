using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Services;
using Resonate.Themes;
using Windows.Graphics;
using Windows.System;

namespace Resonate.App;

/// <summary>
/// App size (Settings, Look, Size): everything under the title bar is laid
/// out smaller and drawn larger by <see cref="Controls.ScaleBox"/>, so pages,
/// panels and the player grow together, and their narrow layouts take over
/// when the window has less room for them. Ctrl+Plus, Ctrl+Minus and Ctrl+0
/// change it as in a browser.
/// </summary>
public sealed partial class MainWindow
{
    private OverlappedPresenter? _presenter;

    // What the smallest window was worked out for, so a move to another screen works it out again.
    private uint _minimumDpi;
    private ulong _minimumDisplay;

    private void SetUpAppSize()
    {
        // The window is in its place by now, so this is the screen it is on.
        ApplyAppSize(grow: true);
        _services.Theme.SizeChanged += (_, _) =>
        {
            ApplyAppSize(grow: true);

            // Menu items keep the text size they were made with (see ThemeService.ApplyTextSize).
            BuildPlaylistSortMenu();
        };
    }

    /// <summary>One step larger (1), smaller (-1) or back to the usual size (0), with a word about where it is now.</summary>
    private void StepAppSize(int step)
    {
        var theme = _services.Theme;
        var size = step switch
        {
            > 0 => AppScale.Larger(theme.AppSize, AppScale.AppSizes),
            < 0 => AppScale.Smaller(theme.AppSize, AppScale.AppSizes),
            _ => AppScale.Normal,
        };
        theme.AppSize = size;
        var back = AppKeys.For(AppCommand.AppSizeReset, _services.Settings.KeyShortcuts);
        ShowMessage(
            size == AppScale.Normal || back.Count == 0 ? $"App size: {AppScale.Label(size)}." : $"App size: {AppScale.Label(size)}. {AppKeys.Display(back)} goes back to 100%.",
            InfoBarSeverity.Informational);
    }

    /// <summary>
    /// Draws the content at the user's App size, decodes covers for the pixels
    /// they now cover, and keeps the window large enough for the page beside
    /// the sidebar (with <paramref name="grow"/>, a smaller window grows to it).
    /// </summary>
    private void ApplyAppSize(bool grow = false)
    {
        var scale = _services.Theme.Scale;
        ContentScale.Factor = scale;
        CoverImages.Scale = scale;
        UpdateMinimumSize(grow);
    }

    /// <summary>
    /// The smallest window gives the page the room of a <see cref="MinimumWidth"/>
    /// by <see cref="MinimumHeight"/> window at the usual App size, within the
    /// screen it is on.
    /// </summary>
    private void UpdateMinimumSize(bool grow)
    {
        if (_presenter is null)
        {
            return;
        }

        // Covers are decoded for this screen's pixels.
        CoverImages.DisplayScale = GetDpiForWindow(Hwnd) / 96.0;
        if (WindowShapesOn)
        {
            // The Window shapes plugin lets the window shrink to a strip (MainWindow.WindowShapes.cs).
            SetShapeMinimumSize(WindowShapes.MinimumWidth, WindowShapes.MinimumHeight);
            return;
        }

        _minimumDpi = GetDpiForWindow(Hwnd);
        var dpi = _minimumDpi / 96.0;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        _minimumDisplay = display.DisplayId.Value;
        var area = display.WorkArea;
        var appSize = _services.Theme.AppSize;
        var width = AppScale.MinimumWindow((int)Math.Round(MinimumWidth * dpi), appSize, area.Width);
        var height = AppScale.MinimumWindow((int)Math.Round(MinimumHeight * dpi), appSize, area.Height);
        _presenter.PreferredMinimumWidth = width;
        _presenter.PreferredMinimumHeight = height;

        if (!grow || _presenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        var size = AppWindow.Size;
        if (size.Width >= width && size.Height >= height)
        {
            return;
        }

        // Grown where it is, but kept on its screen.
        var w = Math.Max(size.Width, width);
        var h = Math.Max(size.Height, height);
        var position = AppWindow.Position;
        var x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.X + area.Width - w));
        var y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - h));
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    /// <summary>After the window moved: on another screen (or the same at another scale) its smallest size is worked out again.</summary>
    private void NoteScreen(AppWindowChangedEventArgs args)
    {
        if (_presenter is null || !(args.DidPositionChange || args.DidSizeChange))
        {
            return;
        }

        // Larger App sizes are kept within the screen, so they also care which screen it is.
        if (GetDpiForWindow(Hwnd) != _minimumDpi
            || (_services.Theme.AppSize != AppScale.Normal
                && DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).DisplayId.Value != _minimumDisplay))
        {
            UpdateMinimumSize(grow: false);
        }
    }

    /// <summary>For CI's tour: the scaled shell fills the space under the title bar exactly, or what it fills instead.</summary>
    internal string? CheckContentFills()
    {
        var outer = ContentScale.ActualSize;
        var inner = ShellGrid.ActualSize * (float)ContentScale.Factor;
        return Math.Abs(outer.X - inner.X) > 1 || Math.Abs(outer.Y - inner.Y) > 1
            ? $"At App size {AppScale.Label(_services.Theme.AppSize)} the page fills {inner.X:0}x{inner.Y:0} of {outer.X:0}x{outer.Y:0}."
            : null;
    }
}
