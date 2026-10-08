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
  2026; see "First milestone"). Releases are published with one
  installer, for ordinary (x64) Windows PCs (`Resonate-win-x64-Setup.exe`;
  the owner dropped arm64 and the portable zip on 8 October 2026 to save
  build time); the latest is v0.7.0
  (8 October 2026: swiping the playing song to skip, Web API only closing
  the Spotify app, the Winamp mini player, App size and Text size, and
  ten built-in plugins with synced lyrics; v0.6.0 brought the classic
  player, Settings in a side pane, bundled fonts, the hovering player,
  eased look switching and a livelier Home, v0.5.0 the resizable
  sidebar, faster covers and the Web API only mode, v0.4.0 "Your top on
  Spotify" and plugins, v0.3.0 the feature update and themes).
  CI proves on every pull
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
- Plugins are optional and downloaded from the release only when turned on
  (see "Plugins" under verified facts and decisions). CI installs and runs
  them in the installed app; on the owner's PC, check that Windows security
  software lets the downloaded helper run.
- Ten built-in plugins (8 October 2026: Lyrics, Home stage, Away screen,
  Rediscover, Up next, Artist orbit, Smart playlists, Window shapes,
  Summon bar, Signal path) are compiled into the app and off until turned
  on in Settings, Plugins; see "Built-in plugins" under verified facts.
  CI draws each once (all but Window shapes) in its screenshot tour, but
  nobody has used them on Windows yet: the owner's PC must check what each
  section lists under "Measure".
- The classic player (a Winamp-style player chosen in Settings), the
  opt-in spinning cover and Liquid Glass's blurred-cover background
  (pull request #18, 8 October 2026) are described under "Classic player
  and cover art". On the owner's PC,
  check that the skin stays sharp at their display scaling, that a
  downloaded `.wsz` skin imports, that the visualiser moves for Local
  Files, and the cost of the spinning cover and Liquid Glass's drift on
  their 165 Hz display.
- The mini player (8 October 2026, the owner asked for a Winamp player
  "like Spotifast does") is described under "Classic player and cover
  art". On the owner's PC, check that it moves and snaps smoothly, stays
  on top, stays sharp at 1x to 4x and when moved to another display, that
  its keys work once clicked, and that Ctrl+M and its close button bring
  the full window back where it was.
- The theme upgrades (8 October 2026: a hovering player, a sidebar that
  reaches the bottom, smoother and longer look-switching animations, a
  bolder look and a livelier Home) are described under "Look, layouts and
  switching". On the owner's PC, judge what CI's pictures cannot show: the
  soft shadows and the play button's glow, the smoothness of Spread from
  the middle and Ripple at 5K and 165 Hz, and the header glows with real
  covers.
- "Spotify Web API only" plays on this PC through Resonate's own player
  (8 October 2026): Spotify's official Web Playback SDK in a hidden
  WebView2, with the Spotify app closed (see "Resonate's own player" under
  verified facts). CI opens it in the installed copy; nobody has played a
  song with it yet. On the owner's PC: sign in again (new permissions),
  check that songs play, skip and seek, that it still plays after a long
  pause, what media keys do, and its power use against the Spotify app's.
- App size and Text size (8 October 2026) are described under "Size". On
  the owner's PC, check sharpness at 125 to 200 %, 150 % text with Paper
  and the wider fonts, Ctrl+Plus on their keyboard, and the window growing
  when App size needs more room.
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
  10 MB in the last round (read on Search, which shows no pictures; read
  on Home it also counted memory given back once another page opened), a
  warm start over 1 s, or a build warning. Memory is read on Search after
  every step of every round, so the report's growth table and the error
  name the step that keeps memory. Single steps swing by up to 10 MB as
  memory comes back a step or two later, so judge a round's total. Open
  (8 October 2026, pull request #32): rounds still grow 3 to 5 MB, about
  4 MB each time the queue opens and closes and each time Settings does
  (all native; no page stays alive). Not yet explained.
  GitHub's machines draw without a graphics card, so judge drawing cost
  on a real PC. WinUI lets go of a closed page only on a later frame, and
  an idle window draws none, so the test asks for frames between
  collections (`CompositionTarget.Rendering`); that is the likely reason
  the Settings page closed last sometimes looked kept on 8 October 2026.
  A page still alive at the end is given Home, Search and Settings
  again, and the log names the step that let it go and whether its
  `Unloaded` ran.
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
  returns at most 10 results per type, and an artist's albums at most 10 per
  request (a higher `limit` is refused with 400 "Invalid limit", so the
  artist page asks for pages of 10, up to 50); browse, artist top tracks, other
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
  must not be in the app's name. The owner asked (8 October 2026) to drop
  the caption under the sidebar, then every "Open in Spotify" button and
  every "from Spotify" credit line (Home's "Song and cover from Spotify",
  About's "Songs, covers and details come from Spotify"): Resonate is meant
  to be used instead of Spotify's app. About keeps only "Resonate is not
  affiliated with Spotify." Before going public this conflicts with the
  guidelines again (open question).

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
  and the mixer. Mind the development-mode quota. The owner asked
  (8 October 2026) that this mode close the Spotify app: `SpotifyAppKeeper`
  closes it when the mode is switched on and when Resonate starts in it
  (asking first, ending it after 6 s, like the equalizer's restart; its
  window stays hidden meanwhile so it does not flash onto the taskbar), and
  switching back to Windows media controls starts it hidden at once.
  Otherwise the mode leaves the app alone: Resonate does not start, hide,
  slow down or restart it, does not listen to its media session, read its
  mixer volume or write its settings file (the equalizer waits and reaches
  local files only). A
  Spotify the user opens again is not closed. Music plays on whatever
  Spotify Connect device Spotify lists: the one already playing, else
  Resonate's own player on this PC (below; a play command waits up to
  12 s while it connects), else the one picked last with the player bar's
  devices button, else this PC's Spotify if the user opened it again,
  else the only device; with several unknown devices it does not guess
  (`WebDeviceResolver`). A skip or seek with nothing active wakes that
  device first. Switching modes takes effect at once, without a restart.
  The Web API only sends commands, so something must play the music: the
  own player (Settings, "Play on this PC", on at first) or another
  device. Before 8 October 2026 there was none, which is why the owner
  heard nothing in this mode. Lossless depends on the device (the own
  player is not Lossless), DJ only starts in a Spotify app, and the
  Spotify app's media session is gone. Spotifast has sound there because
  it bundles librespot, signed in with Spotify's own desktop client ID
  (`65b708073fc0480ea92a077233ca87bd`), at most 320 kbps; that works
  around Spotify's copy protection and stays out (hard rules). Measure:
  switching modes while music plays, and that Spotify closes.
- Resonate's own player (`OwnPlayer` in `Resonate.Spotify/Playback`,
  `Services/WebPlayerPage.cs` and `Assets/WebPlayer/player.html` in the
  app; the owner asked on 8 October 2026 for Web API only to play without
  the Spotify app "consuming power"): Spotify's official Web Playback SDK
  (`https://sdk.scdn.co/spotify-player.js`) in a WebView2 that nobody sees.
  Spotify lists it as a Connect device called "Resonate", and Resonate
  plays on it through the Web API like on any device. Facts checked on
  GitHub's Windows machine (a probe, 8 October 2026): the WebView2 runtime
  (153) ships Widevine, protected AAC, FLAC and Vorbis work, PlayReady
  does not, and the SDK starts and turns down a made-up token (so the
  Electron reports above do not apply to WebView2). It needs Premium and
  the scopes `streaming`, `user-read-email` and `user-read-private`;
  older sign-ins lack them, so it asks to sign in again (it checks the
  saved scopes before starting). The sign-in page also asks to tick "Web
  Playback SDK" in the developer app, in case Spotify checks it. Quality
  is the web player's (AAC, 256 kbps with Premium), not Lossless. The
  WebView2 is a controller on a message-only window (`HWND_MESSAGE`,
  "an invisible WebView"), `IsVisible` false, InPrivate (nothing kept in
  `%LocalAppData%\Resonate\webplayer`), with the page served from
  `https://player.resonate.example` (a secure origin, which protected
  audio needs). It allows only autoplay, no other permission, pop-up,
  download, navigation, developer tools or host objects. Browser
  arguments: autoplay without a click, and no background timer
  throttling, so Spotify keeps hearing from the device while paused. It
  runs only with Web API only, "Play on this PC" and someone signed in,
  starts after the first frame (never in demo, timing or update runs),
  stops on sign-out, after failures tries again after 5 s, 15 s and then
  every minute, and says once when it needs a new sign-in, Premium or the
  WebView2 runtime. A WebView2 that has not started after 45 s counts as
  failed, a page that stops answering is kept for 30 s (WebView2 says so
  also when the PC is only busy), and closing Resonate (`MainWindow.Quit`,
  also from its own close buttons) waits up to 3 s for its goodbye.
  Every copy names its device "Resonate", so another one is never taken
  for the Spotify app on this PC. Demo runs, which CI's checks use, never
  look for updates. SmartScreen look-ups are off
  (`IsReputationCheckingRequired`), and WebView2's crash reports stay on
  the PC (`IsCustomCrashReportingEnabled`) and are deleted when the page
  opens and closes, since they may hold the access token. The WebView2
  runtime itself still sends Microsoft its required diagnostic data (and
  optional data under Windows' Diagnostics & feedback setting) while the
  own player runs; no API turns that off (Microsoft's WebView2 privacy
  page). Its song changes make the player ask `/me/player` at
  once (`PlayerController.RefreshSoon`). Tokens go only to the page,
  never to a log. CI's `--web-player-check` opens it in the installed copy
  and needs protected audio to work and the SDK to answer a made-up token
  with `authentication_error`. Measure: playback while hidden, after a
  long pause, media keys (WebView2 may show its own media session),
  memory and power against the Spotify app.
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
- Windows does not always report that a local song ended (the owner saw
  the music stop after one song). The engine checks once a second: a song
  standing still at its end for two seconds has ended; one standing still
  elsewhere is started again, and if it still has not moved after five
  seconds (and had played) it has ended too, because Windows only
  estimates the length of some MP3s. The local player also moves on by its
  own clock if the engine never says anything.
- Local covers (`LocalCoverCache`, shrunk JPEGs in `local-covers\`): a
  cover read from bytes must reach Windows through `ImageStreams`
  (Windows' own `InMemoryRandomAccessStream`), never .NET's
  `AsRandomAccessStream`, which the Native AOT build does not support
  reliably; that is why local covers never showed before 8 October 2026.
  A file with no cover gets a marker so it is not read again until it or
  its folder changes; a failure that may pass leaves none. Pictures are
  recognised by their first bytes (JPEG, PNG, GIF, BMP, WebP, TIFF, AVIF,
  HEIF). CI's screenshot tour checks a cover embedded in an MP3.
- Spotify covers go through `CoverStore` (`covers\` in the cache folder,
  200 MB on disk, 24 MB in memory, one HTTP/2 connection, at most 24
  downloads at once). Covers on screen go first; covers fetched ahead
  (while the pointer rests on a playlist in the sidebar) use at most six
  downloads. `CoverImages` makes one picture per cover and size, shared by
  every row. Signing out clears the folder.
- No colour tile flashes before a cover (the owner's request, 8 October
  2026): rows and cards (`CoverTile` in `ViewModels.cs`) and page headers
  (`Helpers/PageCover`, which also hides the shadow) draw nothing while a
  cover loads, and the album's colour tile only for a song without a cover
  or once its cover can not be had (`CoverImages.Get(..., out missing)`).
  A song list waits up to 150 ms for the first screen of covers before
  showing its songs (`TracksPage.WarmCoversAsync`; from memory or disk
  that takes a frame or two). Measure: that opening a playlist shows its
  covers with the songs.

Plugins (checked 2026-10-07):
- A Native AOT app cannot load .NET code at run time, so plugins are
  JavaScript run by Jint (4.17.0, a JavaScript engine written in .NET) in a
  separate helper, `Resonate.PluginHost`, itself published with Native AOT
  (about 11 MB, 5 MB zipped, on Linux; CI prints the Windows size and
  memory). Jint works under Native AOT. Its four trim warnings (IL2026, all
  in .NET interop the helper never enables) are reported per method by the
  native compiler even with `TrimmerSingleWarn`, so the helper sets
  `IlcTreatWarningsAsErrors` to false; the analyzers still fail the build
  on warnings in Resonate's own code. Checked on Linux, including regular
  expressions with Unicode properties and that no .NET type is reachable
  from scripts.
- Jint's own time limit (`TimeoutInterval`) counts time on the clock, so a
  plugin waiting for the processor on a busy PC (the helper runs at
  below-normal priority) was stopped for doing nothing wrong; CI's Windows
  machine hit it. `CpuTimeConstraint` counts the call's processor time
  instead (2 s), with 30 s on the clock as a backstop.
- Tests wait for the helper with a ping that it answers only after every
  plugin has handled what came before, never with a quiet period, which
  missed messages on CI's slower machines.
- The app never references Jint: it talks to the helper over standard input
  and output (one JSON message per line), so the installer and start-up
  are unchanged when no plugin is on.
- Release downloads (`/releases/download/<tag>/<file>`) need the repository
  to be public, like the updater. It is (checked 8 October 2026); while it
  was private, turning a plugin on in an installed copy failed with "The
  download did not start".

Built-in plugins (built 8 October 2026; the owner asked for ideas 1, 2, 5,
10, 14, 15, 17, 18 and 20 of the Idea Book "under optional plugins at the
moment", and for synced lyrics like Spotify's, from spotifast's source):
- They need the app's pages, windows and the Web API, which the JavaScript
  helper cannot reach, so they are compiled in and listed in Settings,
  Plugins above the downloaded ones (`Services/BuiltInPlugins.cs`: IDs,
  names, `IsOn`, `Set`, `Changed`; ids saved in `BuiltInPlugins` in
  settings). All are off at first; while off nothing of them runs or
  shows, and switching takes effect at once. Each keeps its settings under
  its own heading in `AppSettings.cs` and its controls in
  `Controls/BuiltInPluginSettings.cs`, and lives in its own files
  (`MainWindow.<Name>.cs` through the `partial void SetUp<Name>()` hooks in
  `MainWindow.BuiltInPlugins.cs`, `HomePage.Stage.cs`,
  `HomePage.Rediscover.cs`, `ArtistPage.Orbit.cs`, `QueuePanel.UpNext.cs`,
  `PlayerBar.Lyrics.cs`, `PlayerBar.SignalPath.cs`). `Services/WindowHook.cs`
  is the one shared subclass of the main window (shortcut and resize
  messages) for plugins that need window messages.
- Lyrics: LRCLIB (`https://lrclib.net/api`, free, no key), as spotifast
  does (its MIT code was ported; its other route, Spotify's own
  transcription, needs librespot and is left out). Only while the lyrics
  pane is open, it sends the song's artist, title, album and length in
  seconds (`/api/get`, then `/api/search` with artist and title), with the
  User-Agent `Resonate/<version> (https://github.com/livexibot/Resonate)`;
  nothing in demo mode. Answers, "no lyrics" included, are cached 30 days
  in `lyrics\` (cache folder, one file per song, SHA-256 named); failures
  are not cached. The pane shares the queue's column, grip and width
  (`QueueWidth`); the queue, Settings and lyrics close one another. Synced
  lyrics follow the player's clock 10 times a second only while the pane
  is open, a song plays and the window shows; the sung line sits about a
  quarter from the top, a click on a line seeks there, and a scroll by hand
  pauses following for 4 s. Measure: that LRCLIB answers real requests.
- Home stage: the top of Home is the playing song, large, over five
  drifting clouds of the cover's colours (`Controls/CloudField`,
  `NowPlayingStage`, colours kept readable by `StageColours.ForText`, 7:1
  and 4.5:1), or the blurred cover (its own switch, off at first). The
  640 px cover is `TrackInfo.FullImageUrl` / `PlayerState.FullArtworkUrl`.
  Each cloud is a colour brush through one dithered alpha mask
  (`Resonate.Themes/CloudMask`, 512 px, triangular noise of 4 alpha steps,
  sent to Windows as a PNG through `LoadedImageSurface`): the compositor's
  radial gradients showed rings on the owner's dark stage (8 October 2026),
  because 8-bit colour has too few shades between two dark colours. A
  visualizer (its own switch, on at first; `Controls/StageVisualizer`,
  maths in `Resonate.Themes/StageBars`) draws slim bars in the cover's
  colours (`StageColours.ForBars`, 3:1 against the page) along the bottom,
  only in the room under the cover and the words. For local files they
  follow the local player's spectrum (`VisualiserFeed.Stage`, a second,
  smooth 75-band analyser); for Spotify songs, which Resonate never hears,
  they sway on their own from compositor expressions on one clock that
  repeats every 20 minutes without a jump. With animations off they are
  hidden. Clouds and bars rest (every animation stopped) while paused,
  hidden, scrolled away, covered, with animations off, or when a
  full-screen app, the lock screen or a dark display is detected. CI's tour
  checks that the mask loads and the bars' expression compiles. Away screen: after 2, 5 (default), 10 or 15 idle
  minutes (`GetLastInputInfo`, checked every 5 s only while music plays),
  with Resonate in front, nothing open or typed into and nothing
  full-screen (`SHQueryUserNotificationState`), the stage covers the window
  (in `ThemeHost.Scene`) with a clock; the input that wakes it is
  swallowed, media keys pass. Measure: the clouds' and bars' cost at 5K
  and 165 Hz, that the rings are gone, `PowerManager.DisplayStatus`
  unpackaged, that accelerators are blocked.
- Rediscover (a Home row): On this day (liked a whole number of years ago
  within 3 days, and album birthdays from `TrackInfo.ReleaseDate`),
  Gathering dust (liked 180 days ago or more and never in the history,
  only once the history covers 21 days) and Deep cuts (albums with 3 or
  more liked songs and songs not liked yet; at most 6 single album
  requests a day, kept in `rediscover.json`, 45 days). Picks change daily,
  not per visit. Artist orbit (artist pages): up to 10 companion artists
  from the user's own playlists, listening sessions and shared songs, and
  up to 8 liked albums, on fixed rings; pictures from Home's cache, else at
  most 10 single artist requests per orbit.
- Up next: edits Resonate's own list order (drag, remove, Delete, Shuffle,
  Clear, Save as playlist, Play next). About 2 s after the last edit (never
  mid-drag) Spotify gets one PUT play: the new window with up to 10 earlier
  songs and the current song at `position_ms`. Read-only (with a note) for
  music started outside Resonate, a song from Spotify's own queue, or a
  list played before it loaded. Local files edit the local queue directly.
  Measure: how audible the brief restart on each edit is, and Previous.
- Smart playlists: rules over Liked Songs or an own playlist (saved in a
  range or the last N days, released before/after/between, longer or
  shorter, explicit, by or not by an artist, not in a playlist; order and
  limit), shown with `TracksPage` (keys `smart:<id>`, `smart-new`), stored
  in settings (enum values are saved as numbers: never renumber them).
  "Keep on Spotify" creates a private playlist and replaces its songs (PUT
  `/playlists/{id}/items`, 100 at a time) after edits and once a day. Song
  lists stored before release dates existed are read again once when a
  rule needs years. Measure: the PUT items and PUT `/playlists/{id}` rename
  calls against a real account.
- Window shapes: Full, Compact (a rail), Column (big cover and controls)
  and Strip (one line, can be pinned on top), picked from the inside size
  when a resize ends (`Resonate.Themes/WindowShapes.cs`: Strip under 200
  px tall, Column under 640 wide or tall and narrow, Compact under 1100
  wide) or from a title-bar button beside the mini player button. While
  on, the window may shrink to 360x64, whatever the App size
  (`UpdateMinimumSize` gives way to it). Summon bar: a global shortcut the
  user records (none by default; RegisterHotKey) and Ctrl+K open a small
  search window (library first via `QuickSearch`, then Spotify search
  after 250 ms); Enter plays, Shift+Enter queues, Esc returns focus.
  Closing the window ends the app, so the shortcut works only while
  Resonate runs (a tray icon would be needed for more). Measure: the strip
  without a title bar, the pin, focus handover, Alt combinations.
- Signal path: a pill in the player bar (LOSSLESS, ADJUSTED, NOT LOSSLESS,
  CAN'T TELL) and a panel with the chain, from Spotify's per-account prefs
  (`audio.play_bitrate_enumeration`, the equalizer, `audio.normalize_v2`
  as a best guess), Spotify's and the mixer volume, and the default output
  device through Core Audio (`DefaultAudioOutput`: its format from
  `PKEY_AudioEngine_DeviceFormat`, Bluetooth from the enumerator name).
  Lossless is taken as 44.1 kHz, so another device rate reads ADJUSTED.
  Web API only, or another device playing, reads CAN'T TELL and never
  touches Spotify's files. Local files are judged by their format.
  Measure: Bluetooth detection, the normalise key, device changes.

Classic player and cover art (checked 2026-10-08):
- Classic Winamp skins (`.wsz`, Winamp 2) are zip archives of BMP sheets
  (`main.bmp` is the 275x116 main window; shade mode is 275x14) plus
  `viscolor.txt` (the visualiser's 24 colours) and `pledit.txt`. Modern
  Winamp 3 and 5 skins (`.wal`, XML) are a different format and refused
  with a message. Webamp (MIT) was the reference for where each sprite sits.
- A skin is untrusted input. `Resonate.Themes/Skins` reads it with its own
  bounded zip reader and BMP decoder: at most 16 MB per archive, 1024
  entries, 8 MB per file and 32 MB unpacked, and 2048 pixels per side,
  each checked before memory is taken. Nothing in a skin is ever run or
  written anywhere but the skins folder, and a missing or broken sheet
  falls back to the built-in skin's.
- Third-party skins belong to their authors, so Resonate bundles none. Its
  one built-in skin, "Resonate Classic", is original and drawn in code
  (`BuiltInSkin`); users add their own `.wsz` files in Settings, which
  copies them to `%LocalAppData%\Resonate\skins`.
- Sharp pixels: each skin pixel covers a whole number of screen pixels,
  `max(1, round(size x RasterizationScale))` (size is 1, or 2 for double
  size while the window has room to keep the song's title beside it), and
  the picture is drawn at that size and shown 1:1, never stretched by XAML.
- The visualiser only hears the local files player: a frame output node on
  the AudioGraph's EQ bus (`AudioGraphTap`), read when each quantum starts,
  feeds a 512-point FFT (`SpectrumAnalyser`) and the oscilloscope. Its
  `IMemoryBufferByteAccess` is called through the raw COM vtable, which
  works under Native AOT. For Spotify songs it stays still: Resonate cannot
  see Spotify's audio and must not capture it (see the hard rules).
- Cover art switches (Settings, Themes, Effects) are global, not part of a
  look. Spinning cover (off until the user turns it on): the playing
  cover is drawn round and turns once every 7 s while a song plays and the
  window shows (composition rotation, paused otherwise; still when Windows
  animations are off). Blurred cover background (on unless the user turns
  it off): looks whose backdrop is the song cover (Liquid Glass) show the
  cover blurred and made vivid (`ArtworkColors.Vivid`: stronger colour,
  shades lifted out of black but kept dark enough for white text), under
  a light tint (0.32); while it is off they show a soft wash of the
  cover's colours instead. With nothing playing they glow with the look's
  two accents, never plain black. Both are one tiny bitmap stretched by
  the GPU, so nothing is blurred per frame. Real covers are never decoded
  in CI (demo covers are made-up gradients), so check Liquid Glass with
  real, dark covers on the owner's PC.
- The mini player (`MiniPlayerWindow`, Ctrl+M, the button beside the
  window's caption buttons, the classic player's menu, or Settings) follows
  Spotifast's: the full window hides (`AppWindow.Hide`, so everything there
  rests) and a borderless window of its own (`OverlappedPresenter` without
  border or title bar, square corners through DWM) shows the classic player
  with Winamp's equalizer (`eqmain.bmp`, `eq_ex.bmp`) and playlist
  (`pledit.bmp`) windows docked under it, opened by its EQ and PL buttons.
  The cover stays beside the main window (cover, song and artist always
  visible), so the windows under it are indented by the cover's width and
  the strip below the cover is black. Sizes are 1x to 4x of the display
  scale, whole screen pixels per skin pixel, and the window is sized to the
  pixel (`ClassicStack.Arrange`; CI's tour checks it). It moves by any part
  that is not a control (`GetCursorPos` and `AppWindow.Move`, not the
  caption drag, so double-clicks still roll windows up), snaps to the
  screen's edges within 10 px (`WindowSnap`), and opens where it was left
  (`MiniPlayerPlace`). The clutter bar's A is always on top (on at first),
  D is 2x. Close, the logo, Esc, Ctrl+M, Alt+F4 and anything that needs the
  full window (search, a page, Settings, a new playlist) go back to it;
  "Exit Resonate" in its menu quits. Keys: Z X C V B, space, arrows.
  Options live in `SkinLibrary` (`Mini*`, raising `MiniOptionsChanged`).
- Winamp's ten EQ sliders drive Spotify's six bands (60 Hz, 150, 400, 1 k,
  2.4 k, 15 k): each slider moves the nearest band, so sliders that share
  one move together (`EqualizerSliders`). The preamp shows Resonate's
  automatic preamp and cannot be dragged; AUTO lays the bands flat (a
  default chosen here, Spotifast-like); PRESETS lists Spotify's presets and
  "Restart Spotify to hear it" when a change waits for Spotify. The
  playlist window is the queue (the song playing first, as the queue pane
  reads it), drawn in the skin's `pledit.txt` font and colours as XAML text
  over the skin's frame; it can only be read and added to, so REM and SEL
  do nothing, ADD opens Search, and a song's menu is on right-click. A
  closed EQ or playlist window leaves the tree, so it reads nothing.
  Dropping a `.wsz` on either player adds and uses it (`SkinDrop`, by path
  only, no casts).

Look, layouts and switching (checked 2026-10-08; the owner asked for a
more modern look without bloat, a centred hovering player, a sidebar that
reaches the bottom, switching animations of 1 to 2 s that ease in and out,
a spread from the middle that is not a circle, a plain morph, a smoother
Ripple, and a more interesting Home):
- Switching looks: every kind but None lasts 1.2 to 1.6 s and eases in and
  out (cubic Bézier with y1 = 0 and y2 = 1). Durations, curves and the
  Random pool live in `Resonate.Themes/ThemeTransitionCatalog.cs` and are
  tested. Kinds: Morph (shapes dissolve while colours flow on the same
  curve), Cross-fade (`Fade`), Spread from the middle (`Grow`, a rounded
  rectangle in the window's shape), Ripple from the click, Split, Blinds,
  Wipe, Surprise me and None. Customize's quick edits cross-fade in 320 ms
  on purpose. `ThemeTransitions` takes one picture of the whole
  `ThemeHost` (a reused `RenderTargetBitmap`) and drives everything from
  one progress value in a `CompositionPropertySet` through expression
  animations, all stopped when the transition ends. Ripple and Spread put
  the picture under the live window (`ThemeHost.Underlay`) and reveal the
  new look through a composition `RectangleClip` scaled by the square root
  of the progress, so the revealed area follows the curve; with Mica or
  acrylic they cross-fade instead. Anything that changes with the look must
  live inside `ThemeHost.Scene`. Switches apply at once while the window is
  hidden or Windows animations are off. The kind is saved by name through
  a tolerant converter (unknown values read as Morph), so never rename a
  member; add new ones at the end. CI's tour holds Spread, Ripple and
  Cross-fade halfway for screenshots (forcing animations on), and the speed
  test reports each switch's first motion and end. Measure: the cost of the
  rounded-rectangle clip at 5K and 165 Hz. Pictures lack the soft shadows,
  so the old look's shadows vanish on the first frame of a switch.
- Player layouts: a look's player is Docked, Floating or Hovering. Hovering
  is a centred pill at most 912 wide over the bottom of the page, at least
  0.9 opaque so text keeps 4.5:1 (`ThemePalette.PlayerFill`); Liquid Glass
  uses it. Over a page too narrow for it and its gaps (360 for the bar,
  the skin and cover for the classic player; Settings open in a small
  window), it sits under the panels instead (`PlayerPlacement.HoveringFits`).
  A global switch, "Sidebar reaches the bottom"
  (`SidebarFullHeight`, off at first), puts the player under the page
  only. The player and the classic player live in one `PlayerSlot` inside
  `ShellGrid`, moved only by attached properties
  (`MainWindow.PlayerPlacement.cs`, maths in
  `Resonate.Themes/PlayerPlacement.cs`), so the classic player is never
  rebuilt. Pages with a list or a scroll end in
  `<Border x:Name="PlayerSpace" Height="0" />` and implement
  `IPlayerInset` (`Pages/*.PlayerInset.cs`); a new page must do the same.
  The bar is Full from 912 wide, Compact below that (no volume slider; the
  mouse wheel on the speaker button changes the volume) and Mini below
  600. The play button's glow (`PlayButtonShadow`,
  `ElevationLevel.PlayButton`) and the cover's shadow are composition
  shadows that CI's screenshots cannot show.
- The look refresh: Display text is 48 Bold set slightly tight;
  `ResonateSectionTextStyle` (24 Bold) titles sections and
  `ResonateEyebrowTextStyle` (12 SemiBold, spaced capitals, secondary
  colour) labels them. Spacing tokens: `ResonatePagePadding` 32,
  `ResonateSectionSpacing` 36, `ResonateSectionHeaderSpacing` 12. Pages
  with a cover show a glow of its colour behind the header
  (`Helpers/CoverHero`): the cover is read at 40 px off the interface
  thread through the cover store, its colour kept in a 200-entry cache,
  with the placeholder colour as fallback. Its strength is
  `ThemePalette.HeroTint`, the strongest tint at which every text stays
  readable; the DJ page uses the accent's (`ResonateHeroGradientBrush`).
  Selected rows and the sidebar's gliding pill (`MainWindow.NavPill.cs`,
  200 ms, placed from the list's height so no row is cast) use
  `ThemePalette.AccentSoft`. Text fields are rounded
  (`ResonateCornerInput`) and outlined in the accent while typing.
- Home: a greeting card (first name, date, a line about today) beside what
  plays or played last, on a wash of the cover's colours
  (`ArtworkSampler.GetWashAsync`, cached, mixed towards the background on
  light looks). The day and week cards have bar charts (`Controls/BarStrip`,
  plain elements built in code, rising once on first load) from
  `ListeningStats.Hourly` and `Daily` (local clock hours and calendar
  days); the week compares with the week before (`ListeningStats.Change`,
  only with two weeks of history). Mix cards are a 2x2 mosaic of the mix's
  most frequent albums. Top artists are round portraits with rank pills.
  Rows show the cards that fit, with "Show all", because a sideways
  scroller would capture the mouse wheel. Cards rise under the pointer
  (`Controls/HoverLift`, 160 ms, still when animations are off). Nothing
  on Home moves by itself once it has settled.

Size (checked 2026-10-08; the owner asked for a setting to scale the whole
app and make text bigger):
- Settings, Layout, Size has App size (80 to 200 %, also Ctrl+Plus, Ctrl+Minus
  and Ctrl+0) and Text size (90 to 150 %). Both belong to the user, not to
  a look; the steps live in `Resonate.Themes/AppScale.cs` and are tested.
- App size: `Controls/ScaleBox` lays out everything under the title bar at
  1/size and draws it that much larger with a `ScaleTransform`
  (`MainWindow.AppSize.cs`). The title bar keeps Windows' size, like the
  caption buttons beside it. Pages see a narrower window, so their compact
  layouts and the Compact and Mini player take over sooner, and the
  window's minimum size grows with App size (within its screen, worked out
  again when the window moves to another screen). Code that compares
  positions must use the content's units (a panel's coordinates), never
  the window's (`GetCurrentPoint(null)`, `TransformToVisual(null)`);
  covers decode at `CoverImages.DecodeWidth`, and pixel-exact drawing
  (the classic skin, the seek bar) multiplies `RasterizationScale` by
  `ThemeService.Scale` (not in the mini player, whose window App size
  does not reach). The classic skin is only pixel-exact when display
  scaling times App size is a whole number (100 % at 200 % App size, for
  example). Menus, tooltips and dialogs open outside the content: menus'
  and tooltips' text takes both sizes (`ResonateMenuFontSize`,
  `ToolTipContentThemeFontSize`), dialog text, drop-down lists and the
  colour picker follow Text size only, and InfoBar text and dialog titles
  keep Windows' size. A menu already built keeps its old size until it is
  built again (the sidebar's sort menu is rebuilt on `SizeChanged`).
- Text size: XAML never gives text a plain `FontSize`; it uses a
  `ResonateFontSize{n}` token from `Tokens.xaml` (the list is
  `AppScale.FontSizes`, and a test fails on a plain size or an unknown
  token). `ThemeService.ApplyTextSize` writes n x Text size into the theme
  dictionary and re-reads theme resources. Icons keep their sizes. Text
  built in code takes a text style. Room for text that lines up in
  columns (song numbers, ranks, times) is a width token
  (`ResonateTrackNumberWidth` and the like), rows use `MinHeight`, and
  width thresholds for text (compact pages, song list columns) are
  multiplied by `ThemeService.TextScale`; pages that use them refit on
  `ThemeService.SizeChanged`.
- CI's tour checks, without a picture, that the page fills the window and
  the hovering player clears the last song at 150 % App size and 125 %
  text (`CheckContentFills`).
- Measure on the owner's PC: that text and icons stay sharp at 125 to
  200 % on the 5K display, that 150 % text fits rows and cards with Paper
  and the wider bundled fonts, that Ctrl+Plus works on their keyboard
  layout, and that the window grows when App size needs more room.

GitHub automation:
- CI cancels a pull request's older run when a newer push arrives, but
  never a run on main: each merge there keeps its own run (8 October
  2026, after merging pull request #37 cancelled #36's run on main).
- Releases and pull requests made with the default `GITHUB_TOKEN` do not
  start other workflows, so `release-please.yml` calls `release.yml`
  directly when a release is created. Pull requests it opens get CI runs
  that wait for approval by someone with write access (newer GitHub
  behaviour); on 8 October 2026 release pull requests sat 2 to 3 hours
  that way. So when a release is wanted, Claude re-runs the release pull
  request's newest CI run at once (`actions_run_trigger`,
  `rerun_workflow_run`): the GitHub tools act as the owner, whose runs
  need no approval. A fine-grained token secret for release-please would
  avoid the wait altogether; only the owner can create one.
- CI's first job, "What changed", lets a pull request that changes only
  Markdown files or `docs/` skip every other job (a skipped job counts as
  passed). It fails open: a push to main, a manual run, or any trouble
  listing the files runs everything. Brief updates that ride along with a
  code change still run in full.
- The Windows app is built once ("Windows app (build)", Native AOT) and
  handed as an artifact (`windows-app`, kept one day) to two jobs that
  run side by side: "Windows app (screenshots, install, update)" and
  "Windows app (speed and memory)", where `perf.md` is printed. The logic
  tests run on Windows in a job of their own beside the build, because
  some of their code paths exist only on Windows (case-insensitive paths,
  the plugin helper's `.exe`, its processor-time limit).
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
  Velopack installs a downloaded update the next time the app starts (it
  exits at once and restarts the new version), so any check of the
  installed copy (`--plugin-check`) runs before the update test.
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

"Spotify Web API only" (an option; Windows media controls stay the
default) closes the Spotify app instead. Then Spotify's official web
player, the Web Playback SDK, plays on this PC, hidden inside Resonate
(see "Resonate's own player" under verified facts). Spotify's own code
still does the playback, at the web player's quality rather than Lossless,
and Resonate never sees the audio.

## Requirements and limits

- Spotify Premium (needed for lossless and for controlling playback).
- The official Spotify desktop app installed and signed in to the same
  account. Resonate should start it hidden if it is not running, and say
  clearly when it cannot be found. Not needed with "Spotify Web API
  only", which plays through Spotify's web player (WebView2 runtime).
- A Spotify developer app (client ID) for the Web API, owned by a Premium
  account. In development mode Spotify allows at most five users, which is
  fine for personal use.
- Spotify only lists the songs of playlists the user owns or collaborates
  on. Other playlists in the library show a note and can still be played
  as a whole.
- Starting a new song or playlist may go through Spotify's servers (always
  on Windows). That is about as fast as the official app, which also has to
  fetch the song first.
- Resonate cannot see Spotify's audio, so the classic player's visualiser
  moves only while a local file plays and stays still for Spotify songs.
  The equaliser in Settings is Spotify's own: Resonate writes it into
  Spotify's settings file while Spotify is closed, so a change reaches
  Spotify songs when Spotify next starts (Resonate starts it, or the user
  presses "Restart Spotify now"). Local files get the same bands at once.
- Spotify's DJ can only be started by opening it in the Spotify app.

## Hard rules

- Never play, decode, record, download or save Spotify audio inside
  Resonate, and do not use librespot. Spotify's own software does all
  playback: its desktop app, or with "Spotify Web API only" its official
  Web Playback SDK, which Resonate hosts in a hidden WebView2 and never
  reads the audio of.
- Never bypass or work around Spotify's DRM or copy protection.
- The local files player plays only files from folders the user chose,
  never anything from Spotify's own folders.
- Never capture Spotify's or the system's audio (loopback recording or
  the like), not even for the visualiser. It hears only the local files
  player.
- Never bundle third-party skins. The classic player ships only
  Resonate's own skin; users add the skins they choose.
- No ad blocking, no unlocking Premium features for free accounts.
- No embedded browser engine (no Electron, no webview for the interface).
  The one webview is the hidden page that runs Spotify's Web Playback SDK
  for "Spotify Web API only" (the owner's request, 8 October 2026); it
  shows nothing and opens nothing else.
- No telemetry and no hosted backend. Everything runs on the owner's
  computer, talking only to Spotify and to GitHub for updates, and to
  LRCLIB for lyrics while the Lyrics plugin's pane is open (the owner
  asked for it, 8 October 2026). The one exception Resonate cannot turn
  off: while its own player runs, the WebView2 runtime sends Microsoft
  Windows' diagnostic data, as Microsoft Edge does (told to the owner).
- Never log access tokens, refresh tokens or authorisation responses. Keep
  tokens in the operating system's credential store, not in plain files.
- The interface is optimistic: a control shows its result the moment it is
  used, and a late answer from Spotify must not undo what the user just did.
- Network and Spotify work never blocks the interface thread.
- Resonate is not affiliated with Spotify. Never make the app look like an
  official Spotify product, never put "Spotify" in its name or icon, and say
  "for Spotify". Spotify's design guidelines require their logo as the
  credit next to Spotify content; the owner had the text credits removed
  (8 October 2026), so this is an open question again before going public.

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
   installer (x64 only, the owner's choice) with Velopack, plus the in-app
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
   Ctrl+N new playlist; later Ctrl+Plus, Ctrl+Minus and Ctrl+0 for App
   size).

To check on the owner's PC: Home after signing in again (two new
permissions), that DJ starts, the equalizer reaching Spotify (and "Restart
Spotify now" bringing the song back), local files playing with the
equalizer, and shuffle staying random across 100-song windows.

After the second milestone, Home gained "Your top on Spotify" (the owner
asked whether stats could come from Spotify, 7 October 2026): Spotify's
top artists and songs over 4 weeks, 6 months or a year, top 5 opening to
top 10, kept in `home.json` with the mixes.

On 8 October 2026 the owner asked for a resizable sidebar, faster covers,
local songs that show their covers and move on, a Web API only mode that
never touches the Spotify app, and "any more basic features like this".
Built then: the sidebar and the queue can be dragged wider (double-click
the gap for the usual width; widths are remembered), covers are kept on
disk and fetched ahead, the window opens where it was left (size, place,
maximised), double-clicking a playlist in the sidebar plays it and
right-clicking it offers Play, Shuffle play and Copy link, the playlist that plays shows a speaker, the window's name is the
song that plays (taskbar and Alt+Tab), and clicking the song's title in
the player bar opens what it plays from.

Later on 8 October 2026 the owner asked to "skip through songs by
dragging like Spotify". Dragging the progress bar already seeked when let
go (now one seek per release, and a drag that a new song interrupts is
dropped). The playing song in the player bar can also be swiped, as on
Spotify's phone app: left for the next song, right for the previous one
(`Controls/PlayerBar.Swipe.cs`; distances and flick speed in
`Resonate.Themes/SongSwipe.cs`, tested).

Also on 8 October 2026 the owner asked to click an artist or album name in
a playlist to open its page. In song rows (playlists, Liked Songs, albums,
search and the queue) the names become links while the pointer is on them
(`Helpers/SongLinks`, `helpers:SongLinks.To="Artists"` or `"Album"` on the
row's TextBlock), the one under the pointer underlined; a click reads the
row it is on then, so a reused row never opens the wrong page.

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
  well for users before merging, and start its CI at once (see "GitHub
  automation": its runs wait for approval otherwise).

### Repository layout

Keep it obvious what is what:

- `src/Resonate.App/` the WinUI 3 app: windows, pages, controls, the
  updater, demo mode, and the theme engine (`Themes/Tokens.xaml` holds every
  token, `ThemeService.cs` applies looks, `ThemeTransitions.cs` animates
  switching, `Controls/ThemeStudio` is the Themes tab of Settings and
  `Controls/LayoutSettings` its Layout tab,
  `MainWindow.PlayerPlacement.cs` places the player, `MainWindow.AppSize.cs`
  and `Controls/ScaleBox.cs` apply App size), and
  the classic player (`Controls/ClassicPlayer`, its Settings section
  `Controls/ClassicPlayerPanel`, and `Services/SkinLibrary.cs`) and the
  mini player (`MiniPlayerWindow.cs`, `MainWindow.MiniPlayer.cs`, with
  `Controls/ClassicEqualizer.cs` and `Controls/ClassicPlaylist.cs`). Both
  players share the plugin button (`Controls/PluginMenu.cs`). The hidden
  page of Resonate's own player is `Services/WebPlayerPage.cs` and
  `Assets/WebPlayer/player.html`.
- `src/Resonate.Spotify/` everything about Spotify that is not Windows:
  sign-in, the Web API client, the library, the player logic (with
  Resonate's own player, `Playback/OwnPlayer.cs`), listening
  history and daily mixes (`History/`), the equalizer and Spotify's
  settings file (`Audio/`), and Local Files' tag reader and index
  (`LocalFiles/`). Any OS.
- `src/Resonate.Themes/` the theme model, independent of WinUI: the six
  presets, what a look can set, the palette worked out from it (readable
  text guaranteed), saved looks, sharing a look as text, picking colours
  from a cover, the switching animations' timing
  (`ThemeTransitionCatalog`), the player's placement
  (`PlayerPlacement`) and the App and Text size steps (`AppScale`); and the classic player's skins (`Skins/`): reading `.wsz`
  files safely, the built-in skin, drawing and hit-testing the main
  window, the equalizer and playlist windows (`EqualizerWindow.cs`,
  `PlaylistWindow.cs`) and how the mini player stacks them
  (`ClassicStack.cs`), and the visualiser's analyser. Any OS, tested.
- `src/Resonate.Windows/` the Windows side of the player: the media
  session, the mixer volume, starting and restarting Spotify, the local
  files player (`LocalAudio/`), the Credential Manager.
- Built-in plugins (Lyrics, Home stage and the rest) live in the app as
  `Services/BuiltInPlugins.cs` plus their own files (see "Built-in
  plugins" under verified facts); their logic sits in `Resonate.Spotify`
  (`Lyrics/`, `History/Rediscover*`, `Library/ArtistOrbit`,
  `Library/SmartPlaylist*`, `Library/QuickSearch`, `Playback/*UpNext*`,
  `Audio/SignalPath`) and `Resonate.Themes` (`StageColours`,
  `WindowShapes`), tested.
- `src/Resonate.Plugins/` optional plugins, everything but running them:
  the catalog built into the app, downloading and checking a plugin,
  its settings, permissions and rate limits (`PluginManager`), and the
  messages to the helper. Any OS, tested.
- `src/Resonate.PluginHost/` the helper that runs plugins (Jint), one
  JavaScript engine per plugin; `prelude.js` is the `resonate` API.
- `plugins/` the plugins themselves (`plugin.json` and a script each).
  `docs/plugins.md` explains them and the API.
- `tools/Resonate.PluginPack/` packs the plugins and the helper and writes
  the catalog the app is built with (`-p:PluginCatalog=<file>`).
- `tools/fonts/` rebuilds the fonts in `src/Resonate.App/Assets/Fonts`
  from Google Fonts' sources (see its README).
- `tests/` automated tests (`dotnet test`, run on Linux and Windows).
- `docs/` user-facing guides (`plugins.md`).
- `.github/workflows/` `ci.yml` (every pull request that changes more
  than text: format, tests on Linux and Windows, the Windows build, then
  in parallel start-up time, screenshots, the install test, the plugin
  check, the web player check and the update test, and the speed and
  memory test; see "GitHub automation"),
  `release-please.yml` (release pull request, then calls `release.yml`),
  `release.yml` (builds the x64 installer with Velopack, packs the
  plugins and their helper, and attaches them; no arm64 build and no
  portable zip, the owner's choice of 8 October 2026).
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
- [ ] Settings, General, Features: tick "Issues". It was off on 8 October
  2026, so follow-ups are noted in this file until it is on.
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
- [x] Make the repository public (decided 7 October 2026, so installed
  copies can see new releases). Public by 8 October 2026.
- [ ] Optional, later: Windows code signing, so the installer does not show
  a SmartScreen warning. This costs money (for example Azure Trusted
  Signing).

## Later ideas

- More plugins, each a small pull request adding a folder to `plugins/`.
- Community plugins, if the owner wants them: they would need a stronger
  sandbox for the helper (an AppContainer with no network or file access)
  and a way to review or sign them first.
- Keyboard shortcuts for everything (the Summon bar's Ctrl+K palette is a
  start).
- A tray icon and close to tray (the Summon bar needs it to work after the
  window is closed), and taskbar-thumbnail controls.
- Equalizer and playlist windows for the classic player in the full window
  too. For now only the mini player has them; in the full window EQ opens
  Settings at the equalizer and PL opens the queue.
- Built-in plugins that become ordinary features once the owner has used
  them.

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
  (the guidelines require it), or no credit while Resonate stays personal
  (the owner had the text credits removed on 8 October 2026). The original
  rule said never use the logo.
- Decided: the repository becomes public so the updater works without
  tokens (7 October 2026). The choice was offered with the Developer Policy
  question spelled out; the logo question below is still open.
- Decided: the owner's display is 5120x2160 at 165 Hz (Windows 11), so a
  frame is about 6 ms.
- Decided (8 October 2026, reversing "no lyrics" of 7 October): synced
  lyrics from LRCLIB, as spotifast does, as a built-in plugin that is off
  at first and fetches only while its pane is open. Spotify's terms ask
  apps not to sync lyrics to recordings; the owner asked for it anyway.
- Decided (8 October 2026): the Idea Book's Home stage, Away screen,
  Rediscover, Up next, Artist orbit, Smart playlists, Window shapes,
  Summon bar and Signal path, and Lyrics, are built-in plugins (compiled
  in, off until turned on) "at the moment"; the owner may later make some
  ordinary features.
- Decided (7 October 2026): spinning covers and the blurred cover behind
  the window are allowed as options (the spinning cover off at first).
  Spotify's design guidelines ask apps not to alter cover art; the owner
  chose to offer these anyway. On 8 October 2026 the owner reported Liquid
  Glass looking black and asked for its background to be the blurred song
  cover, so that switch is now on at first. Cover, song and artist always stay
  visible, also in the classic player.
- Decided (7 October 2026): a Winamp-style classic player, chosen in
  Settings (Classic player, "Use the classic player"), with Resonate's own
  skin and any classic skins the user adds.
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
  Daylight, Liquid Glass (the song's cover, blurred, or as a wash of its
  colours once that is switched off, behind see-through panels), Pure
  Black, Synthwave and Paper. Under them, Customize edits
  everything a look sets: colours, light, dark or black, backdrop
  (colour, gradient, song cover, Mica, acrylic), corners, button shape,
  outlines, spacing, shadows, fonts, and the player (docked, floating or
  hovering, progress bar style, play button, cover). Editing a preset makes
  a custom copy; looks can be saved, renamed, and copied or pasted as text.
  Switching looks animates (morph, cross-fade, spread from the middle,
  ripple from the click, split, blinds, wipe, a random one, or none; the
  owner asked for animated switching, then on 8 October 2026 for 1 to 2 s
  animations that ease in and out). Defaults chosen for the 8 October
  upgrades, reversible: the hovering player is a pill over the page (not a
  separate window) and Liquid Glass uses it; "Sidebar reaches the bottom"
  is a global setting, off at first.
- Plugins (asked 7 October 2026): optional add-ons that are downloaded only
  when turned on in Settings and deleted when turned off. Default chosen
  while the owner's answer is open ("built-in extras only, or community
  plugins later?"): only Resonate's own plugins, kept in `plugins/`,
  reviewed like any change, built by the release workflow and pinned by
  SHA-256 in the catalog built into each release. The first two: Sleep
  timer and Skip rules. A plugin may only see what is playing, control
  playback and change the volume, each only with its permission, and
  Resonate rate-limits all of it. No network, file or Spotify Web API
  access for downloaded plugins yet; adding any is a decision for the
  owner. Built-in plugins (above) are reviewed app code, not scripts.
- Decided (8 October 2026): "Spotify Web API only" closes the Spotify
  app (at first it then played only on the user's other Spotify
  devices); Windows media controls is the mode with Lossless sound on
  this PC. The owner then
  asked for Resonate's own Spotify player in that mode, like Spotifast's;
  Claude declined (it means librespot with Spotify's own client ID, which
  works around Spotify's copy protection, see the hard rules) and offered
  a "hidden engine" instead: Spotify running invisibly only while
  Resonate is open. The owner answered that librespot did not matter to
  them: Web API only should play without the Spotify app running in the
  background and costing power. Decided then: Resonate's own player,
  Spotify's official Web Playback SDK hidden in a WebView2 (see verified
  facts), on at first in that mode ("Play on this PC"). Trade-offs told
  to the owner: the web player's 256 kbps instead of Lossless, a new
  sign-in for its permissions, and a hidden WebView2 (a few browser
  processes) in place of the Spotify app. Windows media controls stays
  the default and the Lossless mode.
- Decided (8 October 2026, the owner's requests): Settings opens in a
  pane on the right of the window beside the page, not in place of it.
  It shares the queue's column and grip (`MainWindow.LayOutPanes`; its
  width is kept in `SettingsPaneWidth`, 440 to 960), so opening one closes
  the other; a click on a grip without dragging keeps no width. The player
  bar has no like button (songs are still liked from lists and menus).
  Setting descriptions stay short: the owner found them too wordy, for
  example the plugins' permission line, which is gone. A downloading
  update shows a progress bar with the size, speed and time left, in the
  corner and in Settings, About (`UpdateProgress.cs`).
- Decided (8 October 2026, the owner's request): the Settings button (a
  gear) sits in the title bar at the top right, left of the mini player
  button, instead of at the foot of the sidebar. Settings has five tabs
  along its top (`SettingsTab`, the last one used is kept while Resonate
  runs): Themes (looks, Customize, effects such as the switching animation
  and cover art, the classic player), Layout (player position, which is
  part of the look; sidebar reaching the bottom; which sidebar links and
  title bar buttons show; App size and Text size), Plugins, Misc (playback
  and the Spotify app, equalizer, Local Files) and About (updates, Spotify
  account, help with the keyboard shortcuts, credits). Only the chosen
  tab is laid out. Rows show a description only when it says something
  the name does not, in a few words. Hidden sidebar links are kept in
  `HiddenSidebarLinks` (Home always shows; Local Files keeps
  `ShowLocalFiles`), the mini player button in `ShowMiniPlayerButton`.
- Decided (8 October 2026, the owner's request): App size and Text size
  are the user's own, not part of a look; the title bar keeps Windows'
  size; Ctrl+Plus, Ctrl+Minus and Ctrl+0 change App size as in a browser.
- Fonts (asked 8 October 2026, "no wacky fonts"): 18 open-licence (SIL
  OFL, no Reserved Font Name) families ship in `Assets/Fonts`, each one
  `.ttc` with the Regular, SemiBold and Bold weights, next to its licence.
  `tools/fonts/build_fonts.py` rebuilds them byte for byte from a pinned
  google/fonts commit, checking each file's SHA-256 (Lora, Playfair
  Display, Merriweather and Lexend reserve their names, so they are left
  out); `BundledFonts` (in
  `Resonate.Themes`) lists them and turns a look's font name into the
  `ms-appx:///Assets/Fonts/<file>#<name>` address XAML loads. Looks store
  plain names, so a shared look falls back to Windows' font elsewhere.
  CI's screenshots draw every bundled font and fail if one does not load.
- History: this repository was reset to a single commit. The earlier
  librespot-based client is not kept here; it was a fork of
  https://github.com/crmne/spotifast, which can be read for ideas such as
  its egui theming, but its playback approach is not used.
