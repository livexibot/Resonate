using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Demo;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
using Resonate.Themes;

namespace Resonate.App.Services;

/// <summary>
/// For "--perf &lt;folder&gt;": measures what the owner feels, with demo data the
/// size of a heavy listener's library, then quits. It times every page, scrolls
/// a long list, switches looks with each animation, measures how much the app
/// works while nobody touches it, and visits every page several times to find
/// memory that is never given back. Results go to perf.json (read by CI's
/// budgets) and perf.md (shown in the CI summary). Any error is written to
/// <see cref="ErrorFile"/>, which fails the CI run.
/// </summary>
internal sealed partial class PerformanceTour
{
    /// <summary>Liked Songs in the speed test: a heavy listener's library.</summary>
    public const int LikedSongs = 10_000;

    private const string ErrorFile = "perf-errors.txt";
    private const double Megabyte = 1024 * 1024;
    private const int LeakRounds = 4;
    private const string Glass = "glass";

    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan SettleLimit = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan IdleSpan = TimeSpan.FromSeconds(5);

    // Limits that fail CI. GitHub's machines draw without a graphics card, so
    // these are several times what they measure there; a real PC is faster.
    private const double PageHeldLimitMs = 250;
    private const double PageFirstFrameLimitMs = 500;
    private const double IdleCpuLimitPercent = 2;
    private const double PlayingCpuLimitPercent = 4;

    // Caches fill up over the first rounds; a page that is never freed adds
    // tens of megabytes every round.
    private const double LastRoundGrowthLimitMb = 10;

    private readonly MainWindow _window;
    private readonly FrameworkElement _root;
    private readonly string _folder;
    private readonly double _startupMs;
    private readonly List<(string Name, MemorySample Sample)> _memory = [];
    private readonly List<(string Name, double CpuPercent, double AllocatedKbPerSecond)> _idle = [];
    private readonly List<StepResult> _pages = [];
    private readonly List<StepResult> _looks = [];
    private readonly List<ScrollResult> _scrolls = [];
    private readonly List<string> _leftAlive = [];
    private readonly List<double> _privateMbAfterRound = [];
    private readonly List<string> _errors = [];
    private string _stage = "starting";
    private bool _finished;

    public PerformanceTour(MainWindow window, FrameworkElement root, string folder, double startupMs)
    {
        _window = window;
        _root = root;
        _folder = folder;
        _startupMs = startupMs;
    }

    /// <summary>Every page, by the key <see cref="MainWindow.Open"/> takes.</summary>
    private static IReadOnlyList<(string Name, string Key)> Pages { get; } =
    [
        ("Liked Songs (10,000)", MainWindow.LikedSongsKey),
        ("Playlist", "late-night"),
        ("Album", AlbumSource.Prefix + DemoCatalog.AlbumId(DemoCatalog.Track("focus", 0).Album!.Name!)),
        ("Artist", MainWindow.ArtistPrefix + DemoCatalog.ArtistId(DemoCatalog.AllArtists[0])),
        ("Daily mix", DailyMixSource.KeyFor(1)),
        ("Local Files", MainWindow.LocalFilesKey),
        ("DJ", MainWindow.DjKey),
        ("Search", MainWindow.SearchKey),
        ("Home", MainWindow.HomeKey),
    ];

    public async Task RunAsync()
    {
        Application.Current.UnhandledException += OnUnhandledException;
        try
        {
            Directory.CreateDirectory(_folder);
            var player = App.Services.Player;

            // Resonate opens on Home; let it and the background work finish.
            await Task.Delay(3000);
            Checkpoint("measuring memory and idle use");
            _memory.Add(("After start", await SampleMemoryAsync()));

            // Doing nothing should cost (almost) nothing, also while music plays.
            await WithinAsync(player.PauseAsync());
            await Task.Delay(1000);
            await MeasureIdleAsync("Paused");
            await WithinAsync(player.PlayAsync());
            await Task.Delay(1000);
            await MeasureIdleAsync("Playing");
            var window = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            ShowWindow(window, ShowMinimized);
            await Task.Delay(1000);
            await MeasureIdleAsync("Playing, minimised");
            ShowWindow(window, ShowRestored);
            await Task.Delay(1000);

            // Liquid Glass draws the song's blurred cover, slowly moving, behind everything.
            var theme = App.Services.Theme;
            theme.Select(Glass, transition: ThemeTransitionKind.None);
            await Task.Delay(2000);
            await MeasureIdleAsync("Playing, Liquid Glass (moving cover)");
            ShowWindow(window, ShowMinimized);
            await Task.Delay(1000);
            await MeasureIdleAsync("Playing, Liquid Glass, minimised");
            ShowWindow(window, ShowRestored);
            await Task.Delay(1000);
            await WithinAsync(player.PauseAsync());
            await Task.Delay(1000);
            await MeasureIdleAsync("Paused, Liquid Glass");
            theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
            await Task.Delay(1000);

            // The clock in the player bar would keep the layout busy; pages are timed paused.
            await WithinAsync(player.PauseAsync());
            Checkpoint("opening pages");

            foreach (var (name, key) in Pages)
            {
                _pages.Add(await OpenAsync(name, key));
            }

            _pages.Add(await MeasureAsync("Queue (open)", _window.ToggleQueue));
            _window.ToggleQueue();
            await SettleAsync();

            // Settings opens in a pane beside the page.
            _pages.Add(await MeasureAsync("Settings (open)", _window.OpenSettings));
            _window.CloseSettings();
            await SettleAsync();

            Checkpoint("scrolling");
            _window.Open(MainWindow.LikedSongsKey);
            await SettleAsync();
            // The page's longest list (the sidebar's lists come first in the window).
            var songs = _window.CurrentPage is DependencyObject page
                ? Descendants<ListView>(page).MaxBy(list => list.Items.Count)
                : null;
            if (songs is { Items.Count: > 1000 })
            {
                _scrolls.Add(await ScrollAsync("Liked Songs, steady (2 rows a frame)", songs, rowsPerFrame: 2, frames: 300));
                _scrolls.Add(await ScrollAsync("Liked Songs, fast (25 rows a frame)", songs, rowsPerFrame: 25, frames: 300));

                // Under the hovering player, whose shadow is drawn again as rows pass beneath it.
                theme.Edit(look => look with { PlayerLayout = PlayerLayout.Hovering });
                await SettleAsync();
                _scrolls.Add(await ScrollAsync("Liked Songs, under the hovering player (2 rows a frame)", songs, rowsPerFrame: 2, frames: 300));
                theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
                theme.Delete(ThemeLibrary.CustomId);
                await SettleAsync();
            }
            else
            {
                _errors.Add("The song list was not found, so scrolling was not measured.");
            }

            _memory.Add(("After scrolling 10,000 songs", await SampleMemoryAsync()));

            // Each switching animation once, each to the next preset, from a
            // playlist. Each plays to its end before the next one starts.
            Checkpoint("switching looks");
            _window.Open("focus");
            await SettleAsync();
            var presets = ThemePresets.All;
            var next = 1;
            foreach (var kind in Enum.GetValues<ThemeTransitionKind>().Where(k => k is not ThemeTransitionKind.Random))
            {
                var preset = presets[next++ % presets.Count];
                _looks.Add(await SwitchAsync($"{preset.Name} ({kind})", () => theme.Select(preset.Id, transition: kind)));
            }

            // Not a limit: an animation that could not be built at all is a bug.
            if (theme.TransitionFailure is { } failure)
            {
                _errors.Add($"A switching animation failed, so the look changed without it: {failure}");
            }

            theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
            await SettleAsync();

            Checkpoint("visiting every page again");
            await FindLeaksAsync();
            _finished = true;
            CheckLimits();
        }
        catch (Exception ex)
        {
            _errors.Add(ex.ToString());
        }
        finally
        {
            try
            {
                Write();
            }
            finally
            {
                Application.Current.Exit();
            }
        }
    }

    /// <summary>
    /// Visits every page <see cref="LeakRounds"/> more times. Afterwards no page
    /// left behind may still be alive, and memory should stay where it was.
    /// </summary>
    /// <remarks>
    /// Memory is read on Search, which shows no pictures. Read on Home, it also
    /// counted 17 to 28 MB that was given back as soon as another page opened
    /// (CI, 8 October 2026), so a round could seem to grow while nothing was
    /// kept. A leak stays whichever page shows.
    /// </remarks>
    private async Task FindLeaksAsync()
    {
        _window.Open(MainWindow.SearchKey);
        await SettleAsync();
        var before = await SampleMemoryAsync();
        _memory.Add(("Before visiting every page 4 more times", before));

        var visited = new List<Visit>();
        for (var round = 1; round <= LeakRounds; round++)
        {
            foreach (var (name, key) in Pages)
            {
                _window.Open(key);
                if (_window.CurrentPage is FrameworkElement page)
                {
                    visited.Add(Visit.Of(name, round, page));
                }

                await SettleAsync(TimeSpan.FromSeconds(3));
            }

            _window.ToggleQueue();
            await SettleAsync(TimeSpan.FromSeconds(2));
            _window.ToggleQueue();

            // Closing the Settings pane lets go of its page, like leaving a page.
            _window.OpenSettings();
            if (_window.SettingsPage is { } settings)
            {
                visited.Add(Visit.Of("Settings", round, settings));
            }

            await SettleAsync(TimeSpan.FromSeconds(3));
            _window.CloseSettings();

            // Memory that keeps climbing round after round is a leak; caches level off.
            // The first sample after a round can still hold memory that one more
            // collection gives back: the sample taken straight after round 4 was
            // 11.6 to 12 MB lower in every run, and that slack alone failed main
            // once with the same code that had passed (CI, 8 October 2026). The
            // lower of two samples keeps what a leak keeps and drops the slack.
            _window.Open(MainWindow.SearchKey);
            await SettleAsync(TimeSpan.FromSeconds(3));
            var first = await SampleMemoryAsync();
            var second = await SampleMemoryAsync();
            _privateMbAfterRound.Add(Math.Min(first.PrivateMb, second.PrivateMb));
        }

        var current = _window.CurrentPage;
        _memory.Add(("After visiting every page 4 more times", await SampleMemoryAsync()));

        // Only weak references are kept here, so the check itself holds no page.
        var alive = visited.Where(v => v.IsAlive(current)).ToList();

        // Still alive: whatever lets go of it says what holds it. Each step
        // fails CI like a page kept for good, naming the step.
        foreach (var (step, act) in Nudges())
        {
            if (alive.Count == 0)
            {
                break;
            }

            await act();
            await SettleAsync(TimeSpan.FromSeconds(2));
            await SampleMemoryAsync();
            var gone = alive.Where(v => !v.IsAlive(null)).ToList();
            if (gone.Count > 0)
            {
                _leftAlive.AddRange(Describe(gone).Select(d => $"{d} until {step}"));
                alive = alive.Except(gone).ToList();
            }
        }

        _leftAlive.AddRange(Describe(alive));
    }

    /// <summary>What a page still alive at the check is given next, in turn.</summary>
    private (string Step, Func<Task> Act)[] Nudges() =>
    [
        ("Home opened", () => OpenPage(MainWindow.HomeKey)),
        ("Search opened again", () => OpenPage(MainWindow.SearchKey)),
        ("Settings opened and closed again", ReopenSettingsAsync),
    ];

    private Task OpenPage(string key)
    {
        _window.Open(key);
        return Task.CompletedTask;
    }

    private async Task ReopenSettingsAsync()
    {
        _window.OpenSettings();
        await SettleAsync(TimeSpan.FromSeconds(2));
        _window.CloseSettings();
    }

    /// <summary>
    /// "Settings ×1 (round 4, never unloaded)": how many of a page, from which
    /// rounds, and whether WinUI said it left (its Unloaded event).
    /// </summary>
    private static IEnumerable<string> Describe(IEnumerable<Visit> visits) => visits
        .GroupBy(v => v.Name)
        .Select(g => $"{g.Key} ×{g.Count()} (round {string.Join(", ", g.Select(v => v.Round + (v.Unloaded.Value ? string.Empty : ", never unloaded")))})");

    /// <summary>Anything over its limit is an error, so CI fails and shows it.</summary>
    private void CheckLimits()
    {
        foreach (var page in _pages)
        {
            if (page.HeldMs > PageHeldLimitMs)
            {
                _errors.Add($"Opening {page.Name} held the interface for {page.HeldMs:N0} ms (limit {PageHeldLimitMs:N0} ms).");
            }

            if (page.FirstFrameMs > PageFirstFrameLimitMs)
            {
                _errors.Add($"{page.Name} took {page.FirstFrameMs:N0} ms to show (limit {PageFirstFrameLimitMs:N0} ms).");
            }
        }

        // While a song plays only the time and the progress bar move (a pixel
        // at a time), and Liquid Glass's cover holds still while nothing plays
        // or nobody can see it. Its moving cover is measured, not limited.
        foreach (var (name, cpu, _) in _idle)
        {
            double? limit = name switch
            {
                "Playing" => PlayingCpuLimitPercent,
                "Playing, Liquid Glass (moving cover)" => null,
                _ => IdleCpuLimitPercent,
            };

            if (cpu > limit)
            {
                _errors.Add($"Doing nothing ({name}) used {cpu:N1} % of a processor core (limit {limit:N0} %).");
            }
        }

        if (_leftAlive.Count > 0)
        {
            _errors.Add($"Pages stayed in memory after leaving them: {string.Join(", ", _leftAlive)}.");
        }

        if (_privateMbAfterRound.Count >= 2)
        {
            var growth = _privateMbAfterRound[^1] - _privateMbAfterRound[^2];
            if (growth > LastRoundGrowthLimitMb)
            {
                _errors.Add($"Memory still grew by {growth:N0} MB in the last round of visiting every page (limit {LastRoundGrowthLimitMb:N0} MB).");
            }
        }
    }

    /// <summary>Waits for a command to the pretend player, but never for long.</summary>
    private static async Task WithinAsync(Task command) => await Task.WhenAny(command, Task.Delay(TimeSpan.FromSeconds(5)));

    private Task<StepResult> OpenAsync(string name, string key) => MeasureAsync(name, () => _window.Open(key));

    /// <summary>
    /// Times one action: how long it held the interface thread, when the first
    /// frame showing it was drawn, when the layout stopped changing (data that
    /// arrives later included), and the longest gap between two frames meanwhile.
    /// </summary>
    private async Task<StepResult> MeasureAsync(string name, Action action)
    {
        using var frames = new FrameRecorder();
        using var layout = new LayoutWatcher(_root);
        var allocated = GC.GetTotalAllocatedBytes();
        var start = Stopwatch.GetTimestamp();
        frames.Start(start);
        layout.Start(start);

        action();
        var held = Stopwatch.GetElapsedTime(start);
        var firstFrame = await frames.WhenFrameAsync(2, SettleLimit);
        var settled = await layout.WhenQuietAsync(Quiet, SettleLimit);

        return new StepResult(
            name,
            held.TotalMilliseconds,
            firstFrame.TotalMilliseconds,
            settled.TotalMilliseconds,
            frames.LongestGapMilliseconds,
            (GC.GetTotalAllocatedBytes() - allocated) / Megabyte);
    }

    /// <summary>
    /// Times one switch of looks like <see cref="MeasureAsync"/>, and also
    /// when its animation first moved on screen and when it ended. It waits
    /// for the end, so one switch never cuts the next one short.
    /// </summary>
    private async Task<StepResult> SwitchAsync(string name, Action action)
    {
        var theme = App.Services.Theme;
        using var frames = new FrameRecorder();
        using var layout = new LayoutWatcher(_root);
        var allocated = GC.GetTotalAllocatedBytes();
        var start = Stopwatch.GetTimestamp();
        frames.Start(start);
        layout.Start(start);

        action();
        var held = Stopwatch.GetElapsedTime(start);
        var started = theme.TransitionStarted;
        var animation = theme.TransitionTask;
        var firstFrame = await frames.WhenFrameAsync(2, SettleLimit);
        await Task.WhenAny(started, Task.Delay(SettleLimit));
        var moved = started.IsCompletedSuccessfully ? Stopwatch.GetElapsedTime(start, started.Result) : SettleLimit;
        await Task.WhenAny(animation, Task.Delay(SettleLimit));
        var finished = Stopwatch.GetElapsedTime(start);
        var settled = await layout.WhenQuietAsync(Quiet, SettleLimit);

        return new StepResult(
            name,
            held.TotalMilliseconds,
            firstFrame.TotalMilliseconds,
            settled.TotalMilliseconds,
            frames.LongestGapMilliseconds,
            (GC.GetTotalAllocatedBytes() - allocated) / Megabyte,
            moved.TotalMilliseconds,
            finished.TotalMilliseconds);
    }

    private async Task SettleAsync(TimeSpan? limit = null)
    {
        using var layout = new LayoutWatcher(_root);
        layout.Start(Stopwatch.GetTimestamp());
        await layout.WhenQuietAsync(Quiet, limit ?? SettleLimit);
    }

    /// <summary>Scrolls a list by a number of rows every frame and records how long each frame took.</summary>
    private static async Task<ScrollResult> ScrollAsync(string name, ListViewBase list, int rowsPerFrame, int frames)
    {
        list.ScrollIntoView(list.Items[0]);
        await Task.Delay(500);

        var count = list.Items.Count;
        var gaps = new List<double>(frames);
        var done = new TaskCompletionSource();
        var index = 0;
        var frame = 0;
        long last = 0;
        var allocated = GC.GetTotalAllocatedBytes();

        void OnRendering(object? sender, object e)
        {
            var now = Stopwatch.GetTimestamp();
            if (last != 0)
            {
                gaps.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            }

            last = now;
            if (frame++ >= frames || index >= count - 1)
            {
                CompositionTarget.Rendering -= OnRendering;
                done.TrySetResult();
                return;
            }

            index = Math.Min(count - 1, index + rowsPerFrame);
            list.ScrollIntoView(list.Items[index], ScrollIntoViewAlignment.Leading);
        }

        CompositionTarget.Rendering += OnRendering;
        if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(1))) != done.Task)
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        return new ScrollResult(name, gaps, (GC.GetTotalAllocatedBytes() - allocated) / Megabyte);
    }

    /// <summary>Processor time (as a share of one core) and memory allocated while nobody touches the app.</summary>
    private async Task MeasureIdleAsync(string name)
    {
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes();
        var start = Stopwatch.GetTimestamp();
        await Task.Delay(IdleSpan);
        process.Refresh();
        var elapsed = Stopwatch.GetElapsedTime(start);
        _idle.Add((
            name,
            (process.TotalProcessorTime - cpu) / elapsed * 100,
            (GC.GetTotalAllocatedBytes() - allocated) / 1024.0 / elapsed.TotalSeconds));
    }

    /// <summary>Memory after collecting garbage, so only what is really kept counts.</summary>
    private static async Task<MemorySample> SampleMemoryAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            // Off the interface thread: finalizers of WinUI objects hand their
            // release to it, so waiting for them there would never end.
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            // WinUI lets go of what it held for collected objects on a later
            // frame, and an idle window draws none, so the page closed last
            // could stay alive through any number of collections (CI, 8
            // October 2026). Asking for frames makes the window draw them.
            using var frames = new FrameRecorder();
            frames.Start(Stopwatch.GetTimestamp());
            await frames.WhenFrameAsync(2, TimeSpan.FromSeconds(1));
            await Task.Delay(100);
        }

        using var process = Process.GetCurrentProcess();
        return new MemorySample(
            process.WorkingSet64 / Megabyte,
            process.PrivateMemorySize64 / Megabyte,
            GC.GetTotalMemory(forceFullCollection: false) / Megabyte,
            process.HandleCount,
            process.Threads.Count,
            GetGuiResources(process.Handle, GuiObjectsGdi),
            GetGuiResources(process.Handle, GuiObjectsUser));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var found in Descendants<T>(child))
            {
                yield return found;
            }
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _errors.Add($"{e.Message}{Environment.NewLine}{e.Exception}");
    }

    /// <summary>Saves what is measured so far, so a test that hangs still leaves its results.</summary>
    private void Checkpoint(string stage)
    {
        _stage = stage;
        try
        {
            Write();
        }
        catch (IOException)
        {
        }
    }

    private void Write()
    {
        Directory.CreateDirectory(_folder);
        if (_errors.Count > 0)
        {
            File.WriteAllText(Path.Combine(_folder, ErrorFile), string.Join(Environment.NewLine + Environment.NewLine, _errors));
        }

        File.WriteAllText(Path.Combine(_folder, "perf.json"), ToJson());
        File.WriteAllText(Path.Combine(_folder, "perf.md"), ToMarkdown());
    }

    private string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteBoolean("finished", _finished);
            json.WriteString("stage", _stage);
            json.WriteNumber("startupMs", Round(_startupMs));
            json.WriteNumber("likedSongs", LikedSongs);
            json.WriteBoolean("animations", App.Services.Theme.AnimationsEnabled);

            json.WriteStartArray("memory");
            foreach (var (name, sample) in _memory)
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteNumber("workingSetMb", Round(sample.WorkingSetMb));
                json.WriteNumber("privateMb", Round(sample.PrivateMb));
                json.WriteNumber("managedMb", Round(sample.ManagedMb));
                json.WriteNumber("handles", sample.Handles);
                json.WriteNumber("threads", sample.Threads);
                json.WriteNumber("gdiObjects", sample.GdiObjects);
                json.WriteNumber("userObjects", sample.UserObjects);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("idle");
            foreach (var (name, cpu, allocated) in _idle)
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteNumber("cpuPercent", Round(cpu));
                json.WriteNumber("allocatedKbPerSecond", Round(allocated));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            WriteSteps(json, "pages", _pages);
            WriteSteps(json, "looks", _looks);

            json.WriteStartArray("scrolling");
            foreach (var scroll in _scrolls)
            {
                json.WriteStartObject();
                json.WriteString("name", scroll.Name);
                json.WriteNumber("frames", scroll.Gaps.Count);
                json.WriteNumber("medianMs", Round(scroll.Percentile(0.5)));
                json.WriteNumber("p95Ms", Round(scroll.Percentile(0.95)));
                json.WriteNumber("longestMs", Round(scroll.Percentile(1)));
                json.WriteNumber("over33Ms", scroll.Gaps.Count(g => g > 33.4));
                json.WriteNumber("allocatedMb", Round(scroll.AllocatedMb));
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("privateMbAfterEachRound");
            foreach (var value in _privateMbAfterRound)
            {
                json.WriteNumberValue(Round(value));
            }

            json.WriteEndArray();

            json.WriteStartArray("pagesLeftAlive");
            foreach (var page in _leftAlive)
            {
                json.WriteStringValue(page);
            }

            json.WriteEndArray();
            json.WriteNumber("errors", _errors.Count);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSteps(Utf8JsonWriter json, string name, List<StepResult> steps)
    {
        json.WriteStartArray(name);
        foreach (var step in steps)
        {
            json.WriteStartObject();
            json.WriteString("name", step.Name);
            json.WriteNumber("heldMs", Round(step.HeldMs));
            json.WriteNumber("firstFrameMs", Round(step.FirstFrameMs));
            json.WriteNumber("settledMs", Round(step.SettledMs));
            json.WriteNumber("longestFrameMs", Round(step.LongestFrameMs));
            json.WriteNumber("allocatedMb", Round(step.AllocatedMb));
            if (step.FirstMotionMs is { } moved && step.FinishedMs is { } finished)
            {
                json.WriteNumber("firstMotionMs", Round(moved));
                json.WriteNumber("finishedMs", Round(finished));
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private string ToMarkdown()
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"### Speed and memory (demo data, {LikedSongs:N0} liked songs)");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Start-up to first frame: **{_startupMs:N0} ms**. Windows animations: {(App.Services.Theme.AnimationsEnabled ? "on" : "off")}.");
        md.AppendLine();
        if (!_finished)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"**Incomplete: the test stopped while {_stage}.**");
            md.AppendLine();
        }

        md.AppendLine("| Memory | Working set | Private | .NET heap | Handles | Threads | GDI / USER objects |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, s) in _memory)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {s.WorkingSetMb:N0} MB | {s.PrivateMb:N0} MB | {s.ManagedMb:N1} MB | {s.Handles} | {s.Threads} | {s.GdiObjects} / {s.UserObjects} |");
        }

        md.AppendLine();
        md.AppendLine("| Doing nothing for 5 s | Processor (one core) | Allocated |");
        md.AppendLine("|---|---:|---:|");
        foreach (var (name, cpu, allocated) in _idle)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {cpu:N1} % | {allocated:N0} KB/s |");
        }

        md.AppendLine();
        AppendSteps(md, "Page", _pages);
        md.AppendLine();
        AppendSwitches(md, _looks);
        md.AppendLine();

        md.AppendLine("| Scrolling | Frames | Median frame | 95th percentile | Longest | Frames over 33 ms | Allocated |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var s in _scrolls)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {s.Name} | {s.Gaps.Count} | {s.Percentile(0.5):N1} ms | {s.Percentile(0.95):N1} ms | {s.Percentile(1):N0} ms | {s.Gaps.Count(g => g > 33.4)} | {s.AllocatedMb:N1} MB |");
        }

        md.AppendLine();
        if (_privateMbAfterRound.Count > 0)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"Private memory after each round of every page: {string.Join(", ", _privateMbAfterRound.Select(m => m.ToString("N0", CultureInfo.InvariantCulture)))} MB.");
            md.AppendLine();
        }

        md.AppendLine(_leftAlive.Count == 0
            ? "Pages kept in memory after leaving them: **none**."
            : $"Pages kept in memory after leaving them: **{string.Join(", ", _leftAlive)}**.");

        if (_errors.Count > 0)
        {
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"**{_errors.Count} error(s)**, see {ErrorFile}.");
        }

        return md.ToString();
    }

    private static void AppendSteps(StringBuilder md, string heading, List<StepResult> steps)
    {
        md.AppendLine(CultureInfo.InvariantCulture, $"| {heading} | Held the interface | First frame | Settled | Longest frame | Allocated |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var s in steps)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {s.Name} | {s.HeldMs:N0} ms | {s.FirstFrameMs:N0} ms | {s.SettledMs:N0} ms | {s.LongestFrameMs:N0} ms | {s.AllocatedMb:N1} MB |");
        }
    }

    /// <summary>Switching looks: when the animation first moved and when it ended, both from the click (for information; no limits).</summary>
    private static void AppendSwitches(StringBuilder md, List<StepResult> steps)
    {
        md.AppendLine("| Switching looks | Held the interface | First frame | First motion | Animation ended | Settled | Longest frame | Allocated |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var s in steps)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {s.Name} | {s.HeldMs:N0} ms | {s.FirstFrameMs:N0} ms | {s.FirstMotionMs:N0} ms | {s.FinishedMs:N0} ms | {s.SettledMs:N0} ms | {s.LongestFrameMs:N0} ms | {s.AllocatedMb:N1} MB |");
        }
    }

    private static double Round(double value) => Math.Round(value, 1);

    private const int ShowMinimized = 6;
    private const int ShowRestored = 9;
    private const uint GuiObjectsGdi = 0;
    private const uint GuiObjectsUser = 1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(nint process, uint flags);

    private sealed record MemorySample(double WorkingSetMb, double PrivateMb, double ManagedMb, int Handles, int Threads, uint GdiObjects, uint UserObjects);

    /// <summary>A page visited in the leak rounds, held only weakly.</summary>
    private sealed record Visit(string Name, int Round, WeakReference Page, StrongBox<bool> Unloaded)
    {
        public static Visit Of(string name, int round, FrameworkElement page)
        {
            // The page holds the handler, and the handler only the box.
            var unloaded = new StrongBox<bool>();
            page.Unloaded += (_, _) => unloaded.Value = true;
            return new Visit(name, round, new WeakReference(page), unloaded);
        }

        public bool IsAlive(object? current) => Page.Target is { } page && !ReferenceEquals(page, current);
    }

    private sealed record StepResult(string Name, double HeldMs, double FirstFrameMs, double SettledMs, double LongestFrameMs, double AllocatedMb, double? FirstMotionMs = null, double? FinishedMs = null);

    private sealed record ScrollResult(string Name, List<double> Gaps, double AllocatedMb)
    {
        public double Percentile(double share)
        {
            if (Gaps.Count == 0)
            {
                return 0;
            }

            var sorted = Gaps.Order().ToList();
            return sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(share * sorted.Count) - 1)];
        }
    }

    /// <summary>Records when the interface thread starts each frame.</summary>
    private sealed class FrameRecorder : IDisposable
    {
        private readonly List<long> _frames = [];
        private long _start;
        private TaskCompletionSource? _waiting;
        private int _waitingFor;

        public double LongestGapMilliseconds
        {
            get
            {
                var longest = 0.0;
                var previous = _start;
                foreach (var frame in _frames)
                {
                    longest = Math.Max(longest, Stopwatch.GetElapsedTime(previous, frame).TotalMilliseconds);
                    previous = frame;
                }

                return longest;
            }
        }

        public void Start(long start)
        {
            _start = start;
            CompositionTarget.Rendering += OnRendering;
        }

        /// <summary>The time from the start to the <paramref name="number"/>th frame (or the limit, when no frame comes).</summary>
        public async Task<TimeSpan> WhenFrameAsync(int number, TimeSpan limit)
        {
            if (_frames.Count < number)
            {
                _waitingFor = number;
                _waiting = new TaskCompletionSource();
                await Task.WhenAny(_waiting.Task, Task.Delay(limit));
            }

            return _frames.Count >= number ? Stopwatch.GetElapsedTime(_start, _frames[number - 1]) : limit;
        }

        public void Dispose() => CompositionTarget.Rendering -= OnRendering;

        private void OnRendering(object? sender, object e)
        {
            _frames.Add(Stopwatch.GetTimestamp());
            if (_waiting is not null && _frames.Count >= _waitingFor)
            {
                _waiting.TrySetResult();
                _waiting = null;
            }
        }
    }

    /// <summary>Notices every layout pass, to tell when a page has stopped changing.</summary>
    private sealed class LayoutWatcher : IDisposable
    {
        private readonly FrameworkElement _root;
        private long _start;
        private long _last;

        public LayoutWatcher(FrameworkElement root) => _root = root;

        public void Start(long start)
        {
            _start = start;
            _last = start;
            _root.LayoutUpdated += OnLayoutUpdated;
        }

        /// <summary>The time from the start to the last layout pass before a quiet spell (or the limit).</summary>
        public async Task<TimeSpan> WhenQuietAsync(TimeSpan quiet, TimeSpan limit)
        {
            while (Stopwatch.GetElapsedTime(_last) < quiet && Stopwatch.GetElapsedTime(_start) < limit)
            {
                await Task.Delay(20);
            }

            return Stopwatch.GetElapsedTime(_start, _last);
        }

        public void Dispose() => _root.LayoutUpdated -= OnLayoutUpdated;

        private void OnLayoutUpdated(object? sender, object e) => _last = Stopwatch.GetTimestamp();
    }
}
