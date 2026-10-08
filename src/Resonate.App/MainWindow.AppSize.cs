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

    private void SetUpAppSize()
    {
        ApplyAppSize();
        _services.Theme.SizeChanged += (_, _) => ApplyAppSize(grow: true);

        AddAppSizeKey((VirtualKey)0xBB, 1); // the plus key beside Backspace (VK_OEM_PLUS)
        AddAppSizeKey(VirtualKey.Add, 1);
        AddAppSizeKey((VirtualKey)0xBD, -1); // the minus key (VK_OEM_MINUS)
        AddAppSizeKey(VirtualKey.Subtract, -1);
        AddAppSizeKey(VirtualKey.Number0, 0);
        AddAppSizeKey(VirtualKey.NumberPad0, 0);
    }

    private void AddAppSizeKey(VirtualKey key, int step)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            StepAppSize(step);
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
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
        ShowMessage(
            size == AppScale.Normal ? "App size: 100%." : $"App size: {AppScale.Label(size)}. Ctrl+0 goes back to 100%.",
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
        ContentScale.Scale = scale;
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

        var dpi = GetDpiForWindow(Hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
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
}
