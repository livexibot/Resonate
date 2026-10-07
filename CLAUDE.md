# Resonate: the brief for Claude

Resonate is a fast, good-looking Windows interface for Spotify that keeps
Spotify's own lossless playback.

This file is the brief: what the owner wants, how it should work, how work
is done in this repository, and what has already been decided. Claude reads
it at the start of every session. Read all of it before writing code, and
keep it up to date as decisions change. `README.md` is the public page for
visitors; keep it short and in step with this file.

## Status and handoff (read first)

- The first milestone is built and waiting in a pull request (see "First
  milestone" for what is done). The Spotify logic is tested on Linux; the
  WinUI 3 app is only compiled, timed and photographed on GitHub's Windows
  machines (CI), because cloud sessions run on Linux.
- Nobody has run Resonate against a real Spotify account yet. The first run
  on the owner's PC must check what CI cannot: sign-in, that Spotify's media
  session (SMTC) reports position and allows seeking, that the per-app mixer
  volume finds Spotify, and that `--minimized` keeps Spotify hidden.
- The two oldest commits are authored "Claude". Fixing that needs a force
  push, which the permission system blocked. Ask the owner before trying.
- The permission system has refused force pushes, deleting branches,
  rewriting history, making release signing optional, and a workflow that
  publishes releases on its own. When a step is refused, explain it to the
  owner in plain words and ask; do not work around it.
- Cloud sessions are given a `claude/...` branch to work on and may only push
  there. Use it for the pull request instead of a `feat/...` name.
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
  uses for screenshots (`--screenshots <folder>`) and start-up timing
  (`--startup-benchmark <file>`).
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
- Themes swap colours by changing the `Color` of shared brushes in
  `Themes/Tokens.xaml`, which updates everything at once, even through
  `StaticResource`. Corner and font tokens apply at start-up only.

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
  works once the repository is public. While it is private, installed
  copies cannot see new versions (never embed a token in the app).
- Branch protection that requires approvals blocks Claude merging its own
  pull requests. Require passing checks only. Squash-merge authorship is not
  documented: check the author after the first merge.

Targets to measure from the first build: cold launch to a usable window
under one second (CI prints it for demo data), page changes within one frame
at the monitor's refresh rate (ask the owner what it is), and play or pause
audible within about 100 ms.

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
never plays audio itself. Instead it is split in two:

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
- Resonate cannot see the audio, so an equaliser or visualiser inside the
  app is out of scope for now. Spotify's own equaliser still applies.

## Hard rules

- Never play, decode, record, download or save Spotify audio inside
  Resonate, and do not use librespot. Spotify's app does all playback.
- Never bypass or work around Spotify's DRM or copy protection.
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

- `src/Resonate.App/` the WinUI 3 app: windows, pages, controls, themes
  (`Themes/Tokens.xaml` and `ThemePreset.cs`), the updater, demo mode.
- `src/Resonate.Spotify/` everything about Spotify that is not Windows:
  sign-in, the Web API client, the library, and the player logic. Any OS.
- `src/Resonate.Windows/` the Windows side of the player: the media
  session, the mixer volume, starting Spotify, the Credential Manager.
- `tests/` automated tests (`dotnet test`, run on Linux and Windows).
- `docs/` user-facing guides, once there is something to explain.
- `.github/workflows/` `ci.yml` (every pull request: format, tests, the
  Windows build with start-up time and screenshots), `release-please.yml`
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
- [ ] Settings, Actions, General, Workflow permissions: "Read and write
  permissions" and "Allow GitHub Actions to create and approve pull
  requests" (release-please needs both).
- [ ] Settings, Rules: protect `main` so changes arrive only through pull
  requests with passing CI, and block force pushes. Do this once CI exists.
- [ ] Create a Spotify developer app at developer.spotify.com (with the
  Premium account), add the redirect URI `http://127.0.0.1:43821/callback`,
  tick "Web API", and paste its client ID into Resonate's first screen.
- [ ] In the Spotify app: switch audio quality to Lossless, stay signed
  in, and let it start with Windows, minimised.
- [ ] Choose a license before making the repository public (MIT is a
  common, simple choice).
- [ ] Make the repository public when ready: until then installed copies
  cannot see new releases (the updater reads public releases only).
- [ ] Optional, later: Windows code signing, so the installer does not show
  a SmartScreen warning. This costs money (for example Azure Trusted
  Signing).

## Later ideas

- More theme presets, saved looks, and copying a look as text.
- Keyboard shortcuts for everything, and a command palette.
- Lyrics, a mini player, and tray controls.
- Queue editing and play history.

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
- Open: the owner's monitor refresh rate, for the frame-time target.
- Look: the first theme is "Midnight" (dark, soft violet accent), with
  "Pure black" and "Daylight" as alternatives in Settings. Waiting for the
  owner's opinion on the screenshots.
- History: this repository was reset to a single commit. The earlier
  librespot-based client is not kept here; it was a fork of
  https://github.com/crmne/spotifast, which can be read for ideas such as
  its egui theming, but its playback approach is not used.
