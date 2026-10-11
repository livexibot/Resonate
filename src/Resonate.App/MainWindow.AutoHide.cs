using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.Themes;

namespace Resonate.App;

/// <summary>
/// Auto-hide (Settings, Layout; the owner's request, 11 October 2026): the
/// sidebar and the player can each slide out of sight, leaving the page the
/// whole window. Resting the pointer on the window's edge where one went
/// (the left edge for the sidebar, the player's own edge for the player), or
/// clicking there, slides it back over the page, and it slides away again a
/// moment after the pointer leaves it and its edge, unless a menu or dialog
/// is open or a text field in it has the keyboard. The page keeps its size
/// while they come and go. A player beside the page on the left shares the
/// left edge with a hiding sidebar, and both come out together; the player
/// in the sidebar goes with the sidebar.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The strip along the window's edge that brings it back, in the content's units.</summary>
    private const double EdgeThickness = 10;

    /// <summary>How long the pointer rests on an edge before what hides there comes out (a pass on the way elsewhere opens nothing).</summary>
    private static readonly TimeSpan EdgeDwell = TimeSpan.FromMilliseconds(120);

    /// <summary>How long after the pointer leaves it what came out slides away again.</summary>
    private static readonly TimeSpan HideAfter = TimeSpan.FromMilliseconds(650);

    private static readonly TimeSpan SlideIn = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan SlideOut = TimeSpan.FromMilliseconds(240);

    private readonly Drawer _sidebarDrawer = new();
    private readonly Drawer _playerDrawer = new();

    // Where each sliding element was last sent, so a layout pass that changes nothing starts no animation.
    private readonly Dictionary<UIElement, (Vector3 To, float Opacity)> _slid = [];
    private bool _autoHideSetUp;

    /// <summary>The sidebar hides: the page runs under it from the window's left (see <see cref="PageColumn"/>).</summary>
    private bool SidebarAutoHides { get; set; }

    /// <summary>The player hides, and lies over the page when it comes out.</summary>
    private bool PlayerAutoHides { get; set; }

    /// <summary>The page's first column: the sidebar's own while the sidebar hides.</summary>
    private int PageColumn => SidebarAutoHides ? 0 : 1;

    /// <summary>The room the sidebar takes beside the page (its width and the gap after it); none while it hides.</summary>
    private double SidebarRoom => SidebarAutoHides ? 0 : SidebarColumn.Width.Value + ShellGrid.ColumnSpacing;

    /// <summary>Both hide and the player sits beside the page on the left: the left edge brings out both.</summary>
    private bool SharesLeftEdge
    {
        get
        {
            var layout = _placement?.Layout ?? _services.Theme.Current.PlayerLayout;
            return SidebarAutoHides && PlayerAutoHides && PlayerPlacement.IsSide(layout) && PlayerPlacement.IsLeftSide(layout);
        }
    }

    /// <summary>Settings changed, the look's player moved, or a window shape came or went: what hides now.</summary>
    internal void ApplyAutoHide()
    {
        SetUpAutoHide();
        var settings = _services.Settings;
        var shown = ShellGrid.Visibility == Visibility.Visible && !ShapeHidesPanels;
        var layout = _services.Theme.Current.PlayerLayout;
        var inSidebar = PlayerPlacement.IsInSidebar(layout) && _sidebarCompact != true && _classicPlayer is null;
        var sidebar = settings.AutoHideSidebar && shown;
        var player = settings.AutoHidePlayer && shown && !inSidebar;

        if (sidebar != SidebarAutoHides)
        {
            SidebarAutoHides = sidebar;
            _sidebarDrawer.Out = false;
            Grid.SetColumn(ContentPanel, PageColumn);
            Grid.SetColumn(ContentElevation, PageColumn);
            Grid.SetColumnSpan(ContentPanel, PageColumnSpan(_paneOverPage));
            Grid.SetColumnSpan(ContentElevation, PageColumnSpan(_paneOverPage));

            // Over the page, the player and the grips while it is out.
            Canvas.SetZIndex(SidebarElevation, sidebar ? 3 : 0);
            Canvas.SetZIndex(Sidebar, sidebar ? 3 : 0);
            SidebarSplitter.Visibility = sidebar ? Visibility.Collapsed : Visibility.Visible;

            // Almost opaque over the page, so a see-through look's sidebar stays readable there.
            Sidebar.Background = _services.Theme.GetBrush(sidebar ? "ResonateSidebarOverPageBrush" : "ResonateSidebarBrush");
            LayOutPanes();
            _placement = null;
            ApplyPlayerPlacement();
        }

        if (player != PlayerAutoHides)
        {
            PlayerAutoHides = player;
            _playerDrawer.Out = false;

            // Almost opaque over the page.
            PlayerBar.LieOverPage(player);
            StyleSideCard();
            _placement = null;
            ApplyPlayerPlacement();
        }

        SlideSidebar();
        SlidePlayer();
    }

    /// <summary>The page's columns: its own, the sidebar's while it hides, and the pane's while the pane lies over it.</summary>
    private int PageColumnSpan(bool paneOver) => (paneOver ? 2 : 1) + (SidebarAutoHides ? 1 : 0);

    private void SetUpAutoHide()
    {
        if (_autoHideSetUp)
        {
            return;
        }

        _autoHideSetUp = true;
        _sidebarDrawer.Holder = Sidebar;
        _playerDrawer.Holder = PlayerSlot;
        _sidebarDrawer.Edge = MakeEdge(_sidebarDrawer);
        _playerDrawer.Edge = MakeEdge(_playerDrawer);
        _sidebarDrawer.Show = show =>
        {
            _sidebarDrawer.Out = show;
            SlideSidebar();
            if (SharesLeftEdge && (show || !_playerDrawer.Inside))
            {
                // The left edge brings out both; the player goes with the sidebar unless the pointer is on it.
                _playerDrawer.Out = show;
                SlidePlayer();
            }
        };
        _playerDrawer.Show = show =>
        {
            _playerDrawer.Out = show;
            SlidePlayer();
        };

        Watch(_sidebarDrawer);
        Watch(_playerDrawer);
        Sidebar.SizeChanged += (_, _) => SlideSidebar();
        PlayerSlot.SizeChanged += (_, _) => SlidePlayer();

        // Signing in hides the panels (a window shape calls ApplyPlayerPlacement, which comes here).
        ShellGrid.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => ApplyAutoHide());
    }

    /// <summary>
    /// Moves and fades <paramref name="element"/> on its compositor visual
    /// (XAML's own transitions refuse an element with a RenderTransform, which
    /// the player has for the user's offset): out with a soft landing, away a
    /// little quicker; at once with Windows' animations off or the window hidden.
    /// </summary>
    private void Slide(UIElement element, Vector3 to, float opacity)
    {
        // Never touched while auto-hide has not been used.
        if (_slid.TryGetValue(element, out var last) ? last == (to, opacity) : to == Vector3.Zero && opacity == 1)
        {
            return;
        }

        _slid[element] = (to, opacity);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!_services.Theme.AnimationsEnabled || !IsShown)
        {
            visual.StopAnimation("Translation");
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Properties.InsertVector3("Translation", to);
            visual.Opacity = opacity;
            return;
        }

        var compositor = visual.Compositor;
        var coming = opacity > 0;
        var ease = coming
            ? compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));
        var time = coming ? SlideIn : SlideOut;
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1f, to, ease);
        move.Duration = time;
        visual.StartAnimation("Translation", move);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity, ease);
        fade.Duration = time;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

    /// <summary>
    /// A strip along an edge of the window, over everything, shown while its
    /// drawer hides or is out: a rest or a click brings the drawer out, and
    /// the pointer on it counts as on the drawer (the window's edge may lie
    /// outside the panel, in the shell's padding).
    /// </summary>
    private Border MakeEdge(Drawer drawer)
    {
        var edge = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Visibility = Visibility.Collapsed,
        };
        edge.PointerEntered += (_, _) =>
        {
            drawer.EdgeInside = true;
            drawer.CancelHide();
            if (!drawer.Out)
            {
                drawer.Rest(DispatcherQueue, EdgeDwell);
            }
        };
        edge.PointerExited += (_, _) =>
        {
            drawer.EdgeInside = false;
            drawer.CancelRest();
            HideLater(drawer);
        };
        edge.Tapped += (_, e) =>
        {
            e.Handled = true;
            drawer.CancelRest();
            drawer.Show?.Invoke(true);
        };

        // Over the shell, under the sign-in page.
        ContentScale.Children.Insert(ContentScale.Children.IndexOf(ShellGrid) + 1, edge);
        return edge;
    }

    /// <summary>The pointer on what came out keeps it out; leaving it starts the slide away.</summary>
    private void Watch(Drawer drawer)
    {
        drawer.Holder!.PointerEntered += (_, _) =>
        {
            drawer.Inside = true;
            drawer.CancelHide();
        };
        drawer.Holder.PointerExited += (_, _) =>
        {
            drawer.Inside = false;
            HideLater(drawer);
        };
    }

    private void HideLater(Drawer drawer)
    {
        if (drawer.Out)
        {
            drawer.HideLater(DispatcherQueue, HideAfter, () => MayHide(drawer));
        }
    }

    /// <summary>
    /// Not while the pointer is on it or its edge (or on the player that came
    /// out with the sidebar), nor while a menu, a flyout or a dialog is open,
    /// nor while a text field in it has the keyboard; it is asked again later.
    /// </summary>
    private bool MayHide(Drawer drawer)
    {
        if (drawer.Inside || drawer.EdgeInside || (drawer == _sidebarDrawer && SharesLeftEdge && _playerDrawer.Inside))
        {
            return false;
        }

        if (Content?.XamlRoot is not { } root || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0)
        {
            return false;
        }

        var focused = FocusManager.GetFocusedElement(root) as DependencyObject;
        if (focused is not (TextBox or AutoSuggestBox))
        {
            return true;
        }

        for (var element = focused; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, drawer.Holder))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The sidebar out over the page, or away past the window's left edge.</summary>
    private void SlideSidebar()
    {
        if (_sidebarDrawer.Edge is not { } edge)
        {
            return;
        }

        var hidden = SidebarAutoHides && !_sidebarDrawer.Out;
        var away = new Vector3(-(float)(Math.Max(Sidebar.ActualWidth, SidebarColumn.Width.Value) + ShellGrid.Padding.Left + 32), 0, 0);
        foreach (var element in new UIElement[] { Sidebar, SidebarElevation })
        {
            Slide(element, hidden ? away : Vector3.Zero, hidden ? 0 : 1);
        }

        Sidebar.IsHitTestVisible = !hidden;
        edge.Width = EdgeThickness;
        edge.Height = double.NaN;
        edge.HorizontalAlignment = HorizontalAlignment.Left;
        edge.VerticalAlignment = VerticalAlignment.Stretch;
        edge.Visibility = SidebarAutoHides ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The player out over the page, or away past its edge of the window.</summary>
    private void SlidePlayer()
    {
        if (_playerDrawer.Edge is not { } edge)
        {
            return;
        }

        var layout = _placement?.Layout ?? _services.Theme.Current.PlayerLayout;
        var hidden = PlayerAutoHides && !_playerDrawer.Out;
        var pad = ShellGrid.Padding;
        var margin = PlayerSlot.Margin;
        Vector3 away;
        if (PlayerPlacement.IsSide(layout))
        {
            var left = PlayerPlacement.IsLeftSide(layout);
            var reach = (float)(PlayerSlot.ActualWidth + margin.Left + margin.Right + (left ? pad.Left + SidebarRoom : pad.Right) + 32);
            away = new Vector3(left ? -reach : reach, 0, 0);
            edge.Width = EdgeThickness;
            edge.Height = double.NaN;
            edge.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            edge.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            var top = PlayerPlacement.IsAtTop(layout) || PlayerPlacement.HoversAtTop(layout);
            var reach = (float)(PlayerSlot.ActualHeight + margin.Top + margin.Bottom + (top ? pad.Top : pad.Bottom) + 32);
            away = new Vector3(0, top ? -reach : reach, 0);
            edge.Width = double.NaN;
            edge.Height = EdgeThickness;
            edge.HorizontalAlignment = HorizontalAlignment.Stretch;
            edge.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }

        Slide(PlayerSlot, hidden ? away : Vector3.Zero, hidden ? 0 : 1);
        PlayerSlot.IsHitTestVisible = !hidden;

        // Over the sidebar too when both are out (it is over the panels and grips otherwise, see SetUpPlayerPlacement).
        Canvas.SetZIndex(PlayerSlot, PlayerAutoHides ? 4 : 1);
        edge.Visibility = PlayerAutoHides && !SharesLeftEdge ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Something that slides out from an edge and back: its panel, its edge strip and its timers.</summary>
    private sealed class Drawer
    {
        private DispatcherQueueTimer? _rest;
        private DispatcherQueueTimer? _hide;
        private Func<bool>? _mayHide;

        public UIElement? Holder { get; set; }

        public Border? Edge { get; set; }

        /// <summary>Out over the page now.</summary>
        public bool Out { get; set; }

        /// <summary>The pointer is on the panel.</summary>
        public bool Inside { get; set; }

        /// <summary>The pointer is on the edge strip.</summary>
        public bool EdgeInside { get; set; }

        /// <summary>Slides it out (true) or away (false).</summary>
        public Action<bool>? Show { get; set; }

        /// <summary>The pointer rests on the edge: out, unless it moves on first.</summary>
        public void Rest(DispatcherQueue queue, TimeSpan dwell)
        {
            _rest ??= queue.CreateTimer();
            _rest.Stop();
            _rest.Interval = dwell;
            _rest.IsRepeating = false;
            _rest.Tick -= OnRest;
            _rest.Tick += OnRest;
            _rest.Start();
        }

        public void CancelRest()
        {
            if (_rest is { } timer)
            {
                timer.Stop();
                timer.Tick -= OnRest;
            }
        }

        /// <summary>Away after <paramref name="after"/>, once <paramref name="may"/> allows it (asked again each time until then).</summary>
        public void HideLater(DispatcherQueue queue, TimeSpan after, Func<bool> may)
        {
            _hide ??= queue.CreateTimer();
            _hide.Stop();
            _mayHide = may;
            _hide.Interval = after;
            _hide.IsRepeating = true;
            _hide.Tick -= OnHide;
            _hide.Tick += OnHide;
            _hide.Start();
        }

        public void CancelHide()
        {
            if (_hide is { } timer)
            {
                timer.Stop();
                timer.Tick -= OnHide;
            }
        }

        private void OnRest(DispatcherQueueTimer sender, object args)
        {
            CancelRest();
            Show?.Invoke(true);
        }

        private void OnHide(DispatcherQueueTimer sender, object args)
        {
            if (!Out)
            {
                CancelHide();
                return;
            }

            if (_mayHide?.Invoke() == false)
            {
                return;
            }

            CancelHide();
            Show?.Invoke(false);
        }
    }
}
