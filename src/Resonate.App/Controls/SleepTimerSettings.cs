using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// The Sleep timer plugin's settings: a time to pause after, counted from
/// when it is picked. The countdown lives as long as Resonate runs (one timer
/// for the app, so closing Settings keeps it going) and is never saved.
/// </summary>
internal static class SleepTimerSettings
{
    private static readonly int[] Minutes = [15, 30, 45, 60, 90, 120];

    private static DispatcherQueueTimer? _timer;
    private static DateTimeOffset _ends;

    public static FrameworkElement Create(AppServices services)
    {
        var choice = new ComboBox { MinWidth = 140 };
        AutomationProperties.SetName(choice, "Pause after");
        choice.Items.Add("Off");
        foreach (var minutes in Minutes)
        {
            choice.Items.Add(minutes < 60 ? $"{minutes} min" : $"{minutes / 60.0:0.#} h");
        }

        var left = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["ResonateCaptionTextStyle"],
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(left);
        row.Children.Add(choice);

        var tick = DispatcherQueue.GetForCurrentThread().CreateTimer();
        tick.Interval = TimeSpan.FromSeconds(1);
        tick.Tick += (_, _) => ShowLeft();
        void ShowLeft()
        {
            var remaining = _timer?.IsRunning == true ? _ends - DateTimeOffset.Now : TimeSpan.Zero;
            left.Text = remaining > TimeSpan.Zero ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}" : string.Empty;
            if (remaining <= TimeSpan.Zero && choice.SelectedIndex > 0)
            {
                choice.SelectedIndex = 0;
            }
        }

        choice.SelectedIndex = 0;
        choice.SelectionChanged += (_, _) =>
        {
            _timer?.Stop();
            if (choice.SelectedIndex > 0)
            {
                Start(services, TimeSpan.FromMinutes(Minutes[choice.SelectedIndex - 1]));
            }

            ShowLeft();
        };
        row.Loaded += (_, _) =>
        {
            tick.Start();
            ShowLeft();
        };
        row.Unloaded += (_, _) => tick.Stop();

        return new SettingRow { Header = "Pause after", Content = row };
    }

    private static void Start(AppServices services, TimeSpan after)
    {
        if (_timer is null)
        {
            _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += async (_, _) =>
            {
                if (services.BuiltIns.IsOn(BuiltInPlugins.SleepTimer))
                {
                    await services.Player.PauseAsync();
                }
            };
        }

        _ends = DateTimeOffset.Now + after;
        _timer.Interval = after;
        _timer.Start();
    }
}
