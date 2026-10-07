# Resonate: the brief for Claude

Resonate is a fast, good-looking Windows interface for Spotify that keeps
Spotify's own lossless playback.

This file is the brief: what the owner wants, how it should work, how work
is done in this repository, and what has already been decided. Claude reads
it at the start of every session. Read all of it before writing code, and
keep it up to date as decisions change. `README.md` is the public page for
visitors; keep it short and in step with this file.

## Status and handoff (read first)

- The first milestone is built and merged (pull request #1, 7 October
  2026; see "First milestone"). Releases are published with x64 and arm64
  installers (`Resonate-win-x64-Setup.exe`); the latest is v0.3.0
  (7 October 2026: the feature update and themes). CI proves on every pull
  request that an installed copy can install and update. The Spotify logic is tested on Linux; the
  WinUI 3 app is only compiled, timed and photographed on GitHub's Windows
  machines (CI), because cloud sessions run on Linux.
- The feature update (pull request #9, 7 October 2026) adds Home with
  listening stats and daily mixes, Local Files, DJ, sorting and filtering
  of every list, likes, album and artist pages, truly random shuffle,
  repeat, the queue and the equalizer. See "Second milestone" for what it
  does and what only the owner's PC can confirm.
- Nobody has run Resonate against a real Spotify account yet. The first run
  on the owner's PC must check what CI cannot: sign-in, that Spotify's media
  session (SMTC) reports position and allows seeking, that the per-app mixer
  volume finds Spotify, and that `--minimized` keeps Spotify hidden.
- The two oldest commits are authored "Claude". Fixing that needs a force
  push, which the permission system blocked. Ask the owner before trying.
- Pull request #1 could not be squash-merged (GitHub answered with an empty
  HTTP 500), so its merge commit carries the owner's account email
  (`livexibot@gmail.com`). Squash merging has worked since pull request #7
  and credits the private no-reply address. If it fails again, find out why
  before merging another way.
- The permission system has refused force pushes, deleting branches,
  rewriting history, making release signing optional, and a workflow that
  publishes releases on its own. When a step is refused, explain it to the
  owner in plain words and ask; do not work around it.
- Cloud sessions are given a `claude/...` branch to work on and may only push
  there. Use it for the pull request instead of a `feat/...` name. GitHub
  deleted that branch when pull request #9 was squash-merged, so a follow-up
  change starts the same branch name again from `main` with a plain push.
- Open questions for the owner are listed at the end of this file. The two
  that block a public release are Spotify's Developer Policy and the logo.

## Verified facts (checked 2026-10-07)

Each item was checked against official documentation, or against several
projects that hit it, unless it says "measure" (only the owner's PC can
tell). developer.spotify.com and learn.microsoft.com are blocked from cloud
sessions; Microsoft's docs can be read from the MicrosoftDocs GitHub
repositories, and Spotify's through search results and other projects.

Building and testing:
- Linux cannot run the WinUI XAML compiler (a .NET Framework program). Keep
  Spotify logic in `Resonate.Spotify` (any OS, tested with `dotnet test`)
  and Windows calls in `Resonate.Windows` (compiles on Linux with
  `EnableWindowsTargeting`). The app compiles only on `windows-latest`.
- CI has no Spotify account: tests use fakes of the Web API and of the
  media session, and the app has a `--demo` mode with made-up music that CI
  uses for screenshots (`--screenshots <folder>`), start-up timing
  (`--startup-benchmark <file>`) and the speed and memory test
  (`--perf <folder>`, `Services/PerformanceTour.cs`).
- The speed and memory test (pull request #11) runs with 10,000 liked
  songs and Windows animations on. It times every page, scrolling, each
  look switch, idle processor use (paused, playing, minimised, Liquid
  Glass) and memory over four rounds of every page, prints `perf.md` in
  the job log, and fails CI on: a page holding the interface over 250 ms
  or first frame over 500 ms, idle over 2 % of a core (4 % while
  playing), a page still alive after leaving it, memory growing over
  10 MB in the last round, a warm start over 1 s, or a build warning.
  GitHub's machines draw without a graphics card, so judge drawing cost
  on a real PC.
- Anything that animates for ever (a composition animation with no end,
  a glide over a whole song) makes the window redraw at the screen's
  refresh rate; pause it while paused or minimised (`MainWindow.IsShown`
  and `ShownChanged`). The progress bar is moved by the player's clock
  about once per screen pixel for this reason. A handler on a
  `DispatcherQueueTimer` that captures a page keeps the page in memory
  for good: subscribe while shown, unsubscribe when leaving. Never call
  `GC.WaitForPendingFinalizers` on the interface thread (it deadlocks).
- Cloud sessions cannot download CI artifacts or logs (their storage host is
  blocked). CI therefore also stores each pull request's screenshots as a
  commit under the hidden ref `refs/screenshots/pr-<number>`. Fetch them
  with `git fetch origin refs/screenshots/pr-<n>` and `git show
  FETCH_HEAD:<file>`, look at them, and link them in the pull request as
  `https://github.com/livexibot/Resonate/blob/<commit>/<file>?raw=true`.
  The GitHub tools read job logs; `get_job_logs` works, raw downloads do not.
- To catch C# mistakes in the app before CI, compile its code-behind on
  Linux against the Windows App SDK with stand-ins for the XAML-generated
  members (fields per `x:Name`, an empty `InitializeComponent`). The XAML
  compiler itself only runs on Windows.
- The .NET SDK download host is blocked in cloud sessions; install it with
  `apt-get install dotnet-sdk-10.0` (Ubuntu's package).
- Give the owner a direct download link for anything to try (a release's
  installer), not "download the artifact".

Spotify Web API (these changed a lot; re-check before relying on them):
- Since 27 November 2024, new apps cannot use recommendations, related
  artists, audio features or analysis, featured or category playlists,
  preview URLs, or Spotify-owned editorial playlists.
- February 2026 changes (existing development-mode apps moved on
  9 March 2026): the developer app's owner needs Spotify Premium; at most
  five users per app; playlist songs moved from `/playlists/{id}/tracks` to
  `/playlists/{id}/items`, with `tracks` renamed `items` and each entry's
  `track` renamed `item`; Spotify only lists the songs of playlists the user
  owns or collaborates on (others can still be played as a whole); search
  returns at most 10 results per type; browse, artist top tracks, other
  users' profiles and batch lookups (several IDs at once) were removed;
  `popularity`, `followers` and the `product` field of `/me` were removed;
  saving and following moved to `/me/library`. The client reads both the old
  and new field names.
- Refresh tokens now expire six months after sign-in; Resonate then asks to
  sign in again.
- Since July 2026 development-mode quota is shared by all of an account's
  client IDs, and running out returns 429 with reason `QUOTA_EXCEEDED`.
- Extended quota needs a registered business and 250,000 monthly users, so
  a public release means each user creates their own developer app and
  pastes its client ID into Resonate (the sign-in page walks them through).
- Redirect URIs: loopback IP literals only over HTTP
  (`http://127.0.0.1:43821/callback`), never `localhost`. PKCE, no secret.
- The queue can be read and added to, not reordered or cleared. There is no
  lyrics endpoint.
- Lossless must be switched on in the Spotify app and the Web API cannot
  confirm it (owner's setup list).
- Developer Policy: "Do not build products or services that mimic, or
  replicate or attempt to replace a core user experience of Spotify ...
  without our prior written permission." Resonate is a replacement
  interface, so this needs the owner's decision before anything is made
  public (see open questions).
- Design guidelines: Spotify content (names, covers, playback) must be
  attributed to Spotify with its logo and link back to Spotify. "Spotify"
  must not be in the app's name. Resonate currently shows a text credit and
  "Open in Spotify"; the official logo is not added yet (open question).

Windows:
- Resonate keeps the Spotify app in the background (owner's request,
  7 October 2026): it starts Spotify with a hidden window, and hides any
  Spotify window that has a taskbar button unless it is the foreground
  window (so the user can still open Spotify; it hides again when they
  switch away). While hidden, only Spotify's renderer and GPU processes
  (`--type=renderer`, `--type=gpu-process`) go into efficiency mode
  (EcoQoS, idle priority, working set trimmed); the main process and the
  audio service are never touched. Both are switches in Settings.
  Measure on the owner's PC: playback, media keys and the media session
  keep working with the window hidden and in efficiency mode.
- Settings offer "Spotify Web API only": every control goes through the
  Web API and the player polls `/me/player` (every 2 s while playing, 6 s
  paused, and 0.7 s after each command) instead of using the media session
  and the mixer. Mind the development-mode quota.
- The system media controls (SMTC) can play, pause, skip, seek (when the app
  allows it) and report the song, cover and timeline. They have no volume.
  Resonate uses Spotify's per-app volume in the Windows mixer (Core Audio),
  and the Web API's volume when Spotify has no audio session yet. Measure:
  whether Spotify's session allows seeking and reports position (Resonate
  falls back to the Web API either way).
- Spotify's own "start minimised" setting runs `Spotify.exe --autostart
  --minimized` (community reports, not documented). The Microsoft Store
  version (`SpotifyAB.SpotifyMusic_zpdnekdrzrea0`) is started through its
  package; Resonate then minimises its window without taking focus.
  Measure: whether Spotify stays hidden.
- Velopack installs into a plain folder (`%LocalAppData%\Resonate`), so the
  app is unpackaged (`WindowsPackageType=None`) and self-contained
  (`WindowsAppSDKSelfContained`).
- Native AOT for WinUI 3 is supported since Windows App SDK 1.6. The current
  Windows App SDK is 2.x (2.5.1 used). Use `x:Bind` (no `{Binding}`), mark
  classes that cross into WinRT `partial`, and avoid reflection.
- Under Native AOT, `as` or `is` on an element that XAML created (a template
  part, a `VisualTreeHelper` child, `ContainerFromItem`) fails silently when
  its type is one the app never creates with `new` or names with `x:Name`.
  C#/WinRT cannot find the type by its class name after trimming, so it
  wraps the object as the declared type (`GetRuntimeClassForTypeCreation`).
  CI's screenshots caught this for a `Path` template part and for
  `FindDescendant<ScrollViewer>`. Build such elements in code, or reach them
  through `x:Name`. A hard cast of a resource,
  `(FontFamily)Application.Current.Resources[...]`, ended the published app
  outright; put such variants in XAML and switch their `Visibility`. List
  rows have a transparent background so clicks land on the row's own
  elements (`ListEvents` in `Helpers.cs`).
- The screenshot tour writes any error to `tour-errors.txt`, and CI fails
  when that file exists or the last (sign-in) screenshot is missing.
- Themes swap colours by changing the `Color` of shared brushes in
  `Themes/Tokens.xaml`, which updates everything at once, even through
  `StaticResource`; the same trick lets colours slide from one look to the
  next frame by frame. Corners, outlines, spacing and fonts live in the
  theme dictionary of `Tokens.xaml` and are used with `ThemeResource`:
  `ThemeService` replaces their values, then switches the window's
  `RequestedTheme` away and back so every `ThemeResource` is read again
  (a known workaround; CI's screenshots switch all six presets at run time
  to check it). New XAML must use `ThemeResource` for those tokens.
- `RenderTargetBitmap` (used by CI's screenshots and by the theme
  transitions' snapshots) does not draw visuals added with
  `SetElementChildVisual` (the soft shadows) or Mica and acrylic. It does
  draw an element's own composition properties (clips, translation, scale),
  including running animations. Anything scaled past the edges makes the
  whole picture larger, which is why `BackdropLayer` clips itself. The hard
  shadow is plain XAML so it shows. Judge the transitions and the soft
  shadows on a real PC.

Spotify features Resonate builds on (researched 2026-10-07; "measure" means
only the owner's PC can tell):
- DJ is the playlist `spotify:playlist:37i9dQZF1EYkqdzj48dyYq`. The Web API
  quietly declines to start it (and a PUT play with it stops the music), so
  never send it as a context. Resonate opens that URI in the Spotify app and
  then follows `/me/player`. While DJ plays, shuffle and repeat are
  disallowed and the item can be empty while the DJ talks. Measure: that
  opening the URI starts DJ.
- Local files: the Web API refuses `spotify:local:` URIs (400 "Invalid track
  uri") in `uris`, offsets by URI and the queue; there is no context for
  the Local Files collection. Local entries inside the user's own playlists
  do play when the playlist starts as `context_uri` with
  `offset {position}`. Spotify's own Local Files list and its folder
  settings are client-internal (not in prefs). So Resonate keeps its own
  Local Files list from folders the user picks and plays those files itself.
- Equalizer: Spotify's per-account settings file is
  `%APPDATA%\Spotify\Users\<name>-user\prefs` (installer) or
  `%LOCALAPPDATA%\Packages\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\LocalState\Spotify\Users\<name>-user\prefs`
  (Store); the newest one is the account in use. The keys are
  `audio.equalizer_v2` (true or false) and
  `audio.equalizer.{low_shelf,low_peak,low_mid_peak,high_mid_peak,high_peak,high_shelf}_gain_v2`,
  whole numbers where 2147483647 is +12 dB. The bands are a 60 Hz low shelf,
  peaks at 150 Hz, 400 Hz, 1 kHz and 2.4 kHz, and a 15 kHz high shelf.
  Spotify reads the file when it starts and rewrites it when it quits, so
  Resonate writes only while Spotify is closed. Never open the global
  `%APPDATA%\Spotify\prefs` (it holds sign-in data) and never log either
  file. `audio.play_bitrate_enumeration=5` means Lossless (4 is Very high);
  Spotify leaves the key out while at its default. Measure all of these.
- Windows has no per-app equalizer, and processing Spotify's audio is
  forbidden here, so the Spotify app's own equalizer is the only one for
  Spotify songs.
- `/me/player/recently-played` returns at most the last 50 plays, so the
  listening stats are only as complete as Resonate's polling (every
  30 minutes, and on each Home visit).
- Spotify's own stats are `/me/top/artists` and `/me/top/tracks` (scope
  `user-top-read`) with `time_range` `short_term` (about 4 weeks),
  `medium_term` (about 6 months) or `long_term` (about a year). They are
  ranks only, with no play counts or minutes. Home's "Your top on Spotify"
  asks for all three once a day with the mixes (six requests).
- `uris` on PUT play has no documented limit, but about 800 returns 413 and
  long lists are reported to stall or lose their order. Resonate sends at
  most 100 songs at a time and sends the next 100 as the last one starts.
  A `uris` list started while Spotify's own shuffle is on starts at a random
  song, so Resonate switches Spotify's shuffle off first and checks it.
- Local files play through Windows' AudioGraph (built-in equalizer effects
  need no registration in an unpackaged app). Its equalizer has four
  peaking bands per effect, gain 0.126 to 7.94 (about ±18 dB) and works
  between 22 and 48 kHz. Measure: a device set to 96 or 192 kHz.
- The system media controls for Resonate's own playback come from
  `SystemMediaTransportControlsInterop.GetForWindow(hwnd)`;
  `GetForCurrentView` fails in WinUI 3.
- Local Files' index is `local-files.json` and its cover thumbnails
  `local-covers\` in the cache folder, keyed by path, size and last-write
  time. The first folders are Music and the real Downloads folder
  (`SHGetKnownFolderPath`). Scanning starts after the first frame (never in
  benchmark, update-check or demo runs), rescans only new or changed files,
  and watches the folders. `%APPDATA%\Spotify`, `%LOCALAPPDATA%\Spotify` and
  any `SpotifyAB.SpotifyMusic_*` folder are never scanned, watched or
  played; the scanner and the engine both check. "Date added" is the
  earlier of when Resonate first saw a file and its creation time.
- The local files engine (`AudioGraphEngine`) creates its graph on the
  first local song, pinned to 48 kHz when the device runs higher, stops
  the file node on pause and the graph after 60 s paused, and opens the
  next song 12 s before the end. Ogg and Opus need Microsoft's Web Media
  Extensions. Local playback goes through the Windows mixer: lossless
  decoding, resampled to the device rate, not bit-perfect. Measure: formats,
  gaps between songs, clicks, and device changes.

GitHub automation:
- Releases and pull requests made with the default `GITHUB_TOKEN` do not
  start other workflows, so `release-please.yml` calls `release.yml`
  directly when a release is created. Pull requests it opens get CI runs
  that wait for approval by someone with write access (newer GitHub
  behaviour); a fine-grained token secret avoids that if it becomes a
  bother.
- release-please (action v5) is configured with `bump-minor-pre-major`,
  `initial-version` 0.1.0, and the version in `version.txt` and
  `Directory.Build.props` (`x-release-please-version` marker).
- The in-app updater reads GitHub releases without a token, which only
  works once the repository is public. The owner chose to make it public
  for this (7 October 2026). Never embed a token in the app instead.
- CI packages the app with Velopack exactly like a release, installs it
  silently, starts the installed copy, and checks that it downloads a newer
  local version (`--update-check <feed folder> <result file>`), so a broken
  installer or updater shows up in a pull request, not after a release.
- release-please needs the repository setting "Allow GitHub Actions to
  create and approve pull requests"; without it the Release workflow fails
  with "GitHub Actions is not permitted to create or approve pull requests".
- Branch protection that requires approvals blocks Claude merging its own
  pull requests. Require passing checks only. Squash-merge authorship is not
  documented: check the author after the first merge.

Targets to measure from the first build: cold launch to a usable window
under one second (CI prints it for demo data; the first build measured
748 ms cold and 289 to 435 ms warm on GitHub's Windows machine; 449 ms
cold and under 200 ms warm by pull request #11), page changes within one
frame at the monitor's refresh rate (165 Hz, about 6 ms, on the owner's
5120x2160 display), and play or pause audible within about 100 ms.

## What the owner wants

The owner uses **Windows**. What they dislike about Spotify's own app, and
what Resonate must fix:

- It sometimes takes about 10 seconds to open and load. Resonate should be
  usable in well under a second.
- Clicking through menus and pages is slow and does not run at the
  monitor's refresh rate. Resonate must render at the display's full refresh
  rate (high refresh monitors included) and respond within a frame.
- The interface looks dated. Resonate should look modern and clean.
- It wants smooth animations everywhere: page transitions, hovers, menus,
  the player bar, and list scrolling, all at full frame rate and never
  delaying a click.

Beyond that:

- **Lossless audio, always.** Every song plays in Spotify Lossless. This is
  not negotiable.
- **A much better interface than Spotify's app.** Faster to launch, smooth
  scrolling at high frame rates, instant response to clicks, and less clutter.
- **Themes.** Presets plus full customisation: colours, light, dark and
  true-black modes, corners, shadows, fonts, and saved looks the owner can
  name and switch between.
- **Their own app.** A custom app they can keep adding features to over time.
- **Automatic updates.** Once installed, new versions published from this
  repository install themselves (download in the background, then one click
  to restart).

The size or memory use of Spotify's own app does not matter. Only what the
owner sees and touches has to be fast.

## How it works

Spotify streams lossless audio only to its own official apps, so Resonate
never plays Spotify's audio itself. Instead it is split in two:

1. **The official Spotify desktop app is the audio engine.** It runs in the
   background, minimised or in the tray, signed in to the owner's Premium
   account. Its window is never needed.
2. **Resonate is the whole interface.** A native app (no browser engine) for
   browsing, searching, the library, the queue, the player bar and themes.
   It tells the Spotify app what to play.

Resonate talks to Spotify in two ways:

- **Locally, for instant controls.** The operating system's media controls
  reach the Spotify app on the same computer with no internet round trip,
  so play, pause, next, previous, seek and volume feel as instant as
  Spotify's own buttons.
  - Windows: the system media transport controls (SMTC) session that
    Spotify registers.
  - macOS: Spotify's AppleScript interface (`playpause`, `next track`,
    `player position`, `sound volume`, and `play track "<uri>" in context
    "<uri>"`, which can also start a song locally).
  - Linux: MPRIS over D-Bus (`PlayPause`, `Next`, `Previous`, `SetPosition`,
    `Volume`, `OpenUri`).
  Verify exactly what each platform supports before relying on it. For
  example, Windows media controls can play, pause and skip, but cannot
  choose a song.
- **Through the Spotify Web API, for everything else.** Library, playlists,
  search, artist and album pages, the queue, and starting a song or playlist
  on the local Spotify app's device (found with the devices endpoint). Sign
  in with OAuth (PKCE) using the owner's own Spotify developer app; never
  ask for or store the Spotify password.

Now-playing information (song, artwork, position, play state) should come
from the local channel where possible, so it updates instantly, with the
Web API filling in the rest.

The one thing Resonate plays itself is the user's own music files (Local
Files), because Spotify will not let another app start them. A small
player in `Resonate.Windows/LocalAudio` plays files from the folders the
user chose, with its own queue, shuffle, repeat, media controls and the
same six-band equalizer. Starting one player pauses the other
(`PlayerRouter`). This never touches Spotify's audio, so lossless Spotify
playback is unchanged.

## Requirements and limits

- Spotify Premium (needed for lossless and for controlling playback).
- The official Spotify desktop app installed and signed in to the same
  account. Resonate should start it hidden if it is not running, and say
  clearly when it cannot be found.
- A Spotify developer app (client ID) for the Web API, owned by a Premium
  account. In development mode Spotify allows at most five users, which is
  fine for personal use.
- Spotify only lists the songs of playlists the user owns or collaborates
  on. Other playlists in the library show a note and can still be played
  as a whole.
- Starting a new song or playlist may go through Spotify's servers (always
  on Windows). That is about as fast as the official app, which also has to
  fetch the song first.
- Resonate cannot see Spotify's audio, so a visualiser is out of scope.
  The equaliser in Settings is Spotify's own: Resonate writes it into
  Spotify's settings file while Spotify is closed, so a change reaches
  Spotify songs when Spotify next starts (Resonate starts it, or the user
  presses "Restart Spotify now"). Local files get the same bands at once.
- Spotify's DJ can only be started by opening it in the Spotify app.

## Hard rules

- Never play, decode, record, download or save Spotify audio inside
  Resonate, and do not use librespot. Spotify's app does all playback.
- Never bypass or work around Spotify's DRM or copy protection.
- The local files player plays only files from folders the user chose,
  never anything from Spotify's own folders.
- No ad blocking, no unlocking Premium features for free accounts.
- No embedded browser engine (no Electron, no webview for the interface).
- No telemetry and no hosted backend. Everything runs on the owner's
  computer, talking only to Spotify and to GitHub for updates.
- Never log access tokens, refresh tokens or authorisation responses. Keep
  tokens in the operating system's credential store, not in plain files.
- The interface is optimistic: a control shows its result the moment it is
  used, and a late answer from Spotify must not undo what the user just did.
- Network and Spotify work never blocks the interface thread.
- Resonate is not affiliated with Spotify. Never make the app look like an
  official Spotify product, never put "Spotify" in its name or icon, and say
  "for Spotify". Spotify's design guidelines require their logo as the
  credit next to Spotify content; whether to add it is an open question
  for the owner (until then Resonate credits Spotify in words).

## Suggested technology

- **C# with WinUI 3 (Windows App SDK), published with Native AOT.** The
  owner is on Windows and wants smooth animations, a modern look and full
  refresh rate. WinUI 3 draws with DirectX through the Windows compositor,
  so its animations and scrolling run at the monitor's refresh rate off the
  interface thread. It brings the modern Windows look (Mica, acrylic,
  rounded Fluent controls) and virtualised lists for big playlists, and it
  can reach Spotify's media session directly through WinRT
  (`GlobalSystemMediaTransportControlsSessionManager`). Native AOT keeps
  launch time low. Measure cold launch and frame times early, and keep them
  in a test or benchmark.
- Themes are resource dictionaries of colour, corner, shadow and font
  tokens that every control reads, so a preset or a custom colour restyles
  the whole app at once.
- Updates: Velopack (or an equivalent) for background download and
  one-click restart from GitHub releases.
- If macOS or Linux are ever wanted, that is a separate front end (or a
  move to a cross-platform toolkit such as Rust with egui, which the earlier
  version used); keep the Spotify logic in its own layer so it can be
  shared. Windows comes first.

## First milestone

Built in the first pull request; items 1 to 6 are in place. Still to check
on the owner's PC: everything that needs a real Spotify account (see
"Status and handoff").

1. Sign in with Spotify (PKCE) and store the token securely.
2. Find the local Spotify app: start it hidden if needed and identify its
   Connect device.
3. A player bar with play, pause, next, previous, seek and volume through
   the local channel, plus now playing with artwork.
4. Library sidebar (playlists, Liked Songs), a playlist page, and search.
   Double-clicking a song plays it in its playlist on the local Spotify app.
5. One theme preset with the colour system in place, so more presets and
   customisation can be added, and smooth transitions between pages.
6. The automation in "How work gets done" below: CI on every pull
   request, release-please, and a release workflow that builds the Windows
   installer (x64, and arm64 if cheap) with Velopack, plus the in-app
   updater that installs new releases from this repository. Never commit a
   private key or token.

## Second milestone

Built in pull request #9 (themes and the look are a separate pull request):

1. Home: stats for the last 24 hours and 7 days, six daily mixes built from
   Liked Songs (Spotify removed recommendations for new apps), "On repeat"
   and recently played. History is kept in `history.json`, mixes in
   `home.json` (cache folder).
2. Every song list (playlists, Liked Songs, albums, mixes, Local Files) can
   be sorted and filtered; the sort is remembered per list. Playlists in
   the sidebar can be sorted or dragged into any order. Songs can be liked,
   added to playlists, dragged into a new order in the user's own
   playlists, and opened by album or artist.
3. Truly random shuffle (Fisher–Yates with the system's cryptographic
   random numbers), repeat all and one, and a queue pane.
4. Local Files and DJ under Liked Songs.
5. The equalizer in Settings (Spotify's own, plus local files).
6. Back navigation (title bar, Alt+Left, the mouse's back button) and
   keyboard shortcuts (Ctrl+S shuffle, Ctrl+R repeat, Ctrl+Up/Down volume,
   Ctrl+N new playlist).

To check on the owner's PC: Home after signing in again (two new
permissions), that DJ starts, the equalizer reaching Spotify (and "Restart
Spotify now" bringing the song back), local files playing with the
equalizer, and shuffle staying random across 100-song windows.

After the second milestone, Home gained "Your top on Spotify" (the owner
asked whether stats could come from Spotify, 7 October 2026): Spotify's
top artists and songs over 4 weeks, 6 months or a year, top 5 opening to
top 10, kept in `home.json` with the mixes.

## How work gets done

The owner is not familiar with git or GitHub and wants everything handled
through automation. Claude does all git and GitHub work. The owner describes
what they want in plain words and gets told, in plain words, what changed.
Never ask the owner to run git commands. When something needs the owner
(a setting only they can change, a decision about the look), say exactly
what to click or answer.

The goal is a repository that a stranger can understand at a glance once it
is public: one main branch, one clean commit per change, readable pull
requests, a changelog, and releases with real notes.

### Branches and pull requests

- `main` is the only long-lived branch. It always builds and always holds
  the latest working app.
- Every change gets its own short-lived branch, named by type:
  `feat/<short-name>`, `fix/<short-name>`, `perf/...`, `docs/...`,
  `ci/...`, `chore/...`. One topic per branch.
- Every branch becomes a pull request into `main` with:
  - a title in Conventional Commits form, for example
    `feat: add theme presets` or `fix: keep the queue after skipping`;
  - a plain-language description of what changes for the person using the
    app, and why;
  - screenshots or a short recording for anything visible;
  - `Closes #<issue>` when it finishes an issue.
- CI (build, tests, formatting and lint) must pass. Claude fixes failures
  itself; never skip or disable a test to get green.
- Merge with **squash merge** only, so `main` gets one commit per pull
  request, titled like the pull request. Delete the branch after merging.
- Claude merges its own pull requests once CI is green. For a large change
  to the look or layout, show the owner the screenshots first and wait for
  a yes.
- Never force-push `main`, never rewrite its history, never commit secrets,
  and never leave old branches lying around.

### Commit as the owner

Every commit is made in the owner's name, so the history shows their
account. At the start of each session, before the first commit, run:

```
git config user.name "livexibot"
git config user.email "320484903+livexibot@users.noreply.github.com"
```

That is the owner's private GitHub address, so their real email never
appears in the public history. Keep the `Co-Authored-By` line for Claude
at the end of commit messages, so it stays clear that Claude helped write
the change.

### Commit and pull request types

`feat` (new feature), `fix` (bug fix), `perf` (faster), `refactor` (no
visible change), `docs`, `test`, `ci`, `chore`. A `!` after the type
(`feat!:`) marks a breaking change. `feat`, `fix` and `perf` appear in the
changelog, so write their titles for users.

### Issues

Track features and bugs as GitHub issues with labels such as `feature`,
`bug`, `design` and `performance`. When the owner asks for something that
cannot be done right away, open an issue for it so nothing is forgotten.

### Releases (automatic)

- Versions follow semantic versioning, starting at `0.1.0`. While the
  version is `0.x`, a `feat` raises the middle number and a `fix` the last.
  Stay on `0.x` until the owner says it is ready for `1.0`.
- release-please (a GitHub Action) watches `main` and keeps one open
  "release" pull request that raises the version and updates
  `CHANGELOG.md` from the merged pull request titles.
- Merging that release pull request creates the `vX.Y.Z` tag and the GitHub
  release. The release workflow then builds the Windows installer with
  Velopack and attaches it, and installed copies update themselves.
- Claude merges the release pull request when the owner asks ("publish a
  release"), or after a change the owner wants to try. Check the notes read
  well for users before merging.

### Repository layout

Keep it obvious what is what:

- `src/Resonate.App/` the WinUI 3 app: windows, pages, controls, the
  updater, demo mode, and the theme engine (`Themes/Tokens.xaml` holds every
  token, `ThemeService.cs` applies looks, `ThemeTransitions.cs` animates
  switching, `Controls/ThemeStudio` is the Look section of Settings).
- `src/Resonate.Spotify/` everything about Spotify that is not Windows:
  sign-in, the Web API client, the library, the player logic, listening
  history and daily mixes (`History/`), the equalizer and Spotify's
  settings file (`Audio/`), and Local Files' tag reader and index
  (`LocalFiles/`). Any OS.
- `src/Resonate.Themes/` the theme model, independent of WinUI: the six
  presets, what a look can set, the palette worked out from it (readable
  text guaranteed), saved looks, sharing a look as text, and picking colours
  from a cover. Any OS, tested.
- `src/Resonate.Windows/` the Windows side of the player: the media
  session, the mixer volume, starting and restarting Spotify, the local
  files player (`LocalAudio/`), the Credential Manager.
- `tests/` automated tests (`dotnet test`, run on Linux and Windows).
- `docs/` user-facing guides, once there is something to explain.
- `.github/workflows/` `ci.yml` (every pull request: format, tests, the
  Windows build with start-up time, screenshots, the install and update
  test, and the speed and memory test), `release-please.yml`
  (release pull request, then calls `release.yml`), `release.yml` (builds
  the x64 and arm64 installers with Velopack and attaches them).
- `release-please-config.json`, `.release-please-manifest.json`,
  `version.txt`: release settings and the current version.
- `README.md` for visitors, `CLAUDE.md` (this brief), `CHANGELOG.md`
  (generated), `LICENSE`.

### One-time setup the owner must do

Claude cannot change these repository settings. Walk the owner through them
when the work first needs them, then tick them off here.

- [x] Delete the leftover branches and tag from the reset.
- [ ] Settings, General, Pull Requests: allow squash merging only, and turn
  on "Automatically delete head branches".
- [x] Settings, Actions, General, Workflow permissions: "Read and write
  permissions" and "Allow GitHub Actions to create and approve pull
  requests" (release-please needs both). Done 7 October 2026.
- [ ] GitHub account Settings, Emails: tick "Keep my email addresses
  private", so commits made on the website use the no-reply address.
- [ ] Settings, Rules: protect `main` so changes arrive only through pull
  requests with passing CI, and block force pushes. Do this once CI exists.
- [ ] Create a Spotify developer app at developer.spotify.com (with the
  Premium account), add the redirect URI `http://127.0.0.1:43821/callback`,
  tick "Web API", and paste its client ID into Resonate's first screen.
- [ ] In the Spotify app: switch audio quality to Lossless, stay signed
  in, and let it start with Windows, minimised.
- [ ] Choose a license before making the repository public (MIT is a
  common, simple choice).
- [ ] Make the repository public (decided 7 October 2026, so installed
  copies can see new releases): Settings, General, Danger Zone, Change
  visibility. Do the email setting above first.
- [ ] Optional, later: Windows code signing, so the installer does not show
  a SmartScreen warning. This costs money (for example Azure Trusted
  Signing).

## Later ideas

- Plugins (the owner's next step after themes).
- Keyboard shortcuts for everything, and a command palette.
- Lyrics (LRCLIB would contact a host other than Spotify and GitHub: ask
  the owner first), a mini player, a Now Playing view, a sleep timer, and
  tray and taskbar-thumbnail controls.
- Queue editing (Spotify's queue can only be read and added to).

## Decisions and open questions

- Name: Resonate. Repository: `livexibot/Resonate`.
- Owner's platform: Windows.
- First priorities: launch speed, full refresh rate rendering, a modern
  clean look, smooth animations, and themes.
- Open (blocks going public): Spotify's Developer Policy forbids apps that
  "replicate or attempt to replace a core user experience of Spotify"
  without written permission. Options: keep Resonate personal (private
  repository, own developer app), ask Spotify for permission, or reshape it
  to add value Spotify's app lacks. The owner decides.
- Open: add Spotify's official logo as the credit next to Spotify content
  (the guidelines require it), or keep the text credit while Resonate stays
  personal. The original rule said never use the logo.
- Decided: the repository becomes public so the updater works without
  tokens (7 October 2026). The choice was offered with the Developer Policy
  question spelled out; the logo question below is still open.
- Decided: the owner's display is 5120x2160 at 165 Hz (Windows 11), so a
  frame is about 6 ms.
- Decided (7 October 2026): Local Files are played by Resonate itself,
  because Spotify refuses to start them for other apps; the owner asked
  for Local Files "just like in Spotify". Only the user's own files.
- Decided: the equaliser is Spotify's own, edited in Spotify's settings
  file only while Spotify is closed, with one backup
  (`prefs.resonate-backup`). A change made while Spotify runs waits
  (`EqualizerPendingForSpotify` in settings) until Resonate next starts
  Spotify or the user restarts it from Settings.
- Themes (asked 7 October 2026, "akin to Spicetify"): six presets that
  differ in shape and material, not just colour: Midnight (the default),
  Daylight, Liquid Glass (the song's blurred cover behind see-through
  panels), Pure Black, Synthwave and Paper. Under them, Customize edits
  everything a look sets: colours, light, dark or black, backdrop
  (colour, gradient, song cover, Mica, acrylic), corners, button shape,
  outlines, spacing, shadows, fonts, and the player (docked or floating,
  progress bar style, play button, cover). Editing a preset makes a custom
  copy; looks can be saved, renamed, and copied or pasted as text. Switching
  looks animates (morph, ripple from the click, split, blinds, wipe, a
  random one, or none; the owner asked for animated switching). Plugins
  come later.
- History: this repository was reset to a single commit. The earlier
  librespot-based client is not kept here; it was a fork of
  https://github.com/crmne/spotifast, which can be read for ideas such as
  its egui theming, but its playback approach is not used.
