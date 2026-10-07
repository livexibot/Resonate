# Resonate: the brief for Claude

Resonate is a fast, good-looking Windows interface for Spotify that keeps
Spotify's own lossless playback.

This file is the brief: what the owner wants, how it should work, how work
is done in this repository, and what has already been decided. Claude reads
it at the start of every session. Read all of it before writing code, and
keep it up to date as decisions change. `README.md` is the public page for
visitors; keep it short and in step with this file.

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
- A Spotify developer app (client ID) for the Web API. In development mode
  Spotify only allows a few allowlisted users, which is fine for personal
  use.
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
- Resonate is not affiliated with Spotify. Do not use Spotify's logo or
  make the app look like an official Spotify product; say "for Spotify".

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

Keep it obvious what is what. A suggested layout:

- `src/Resonate.App/` the WinUI 3 app: windows, pages, controls, themes.
- `src/Resonate.Spotify/` the Web API client, sign-in, and the local media
  control channel.
- `tests/` automated tests.
- `docs/` user-facing guides, once there is something to explain.
- `.github/workflows/` `ci.yml`, `release-please.yml`, `release.yml`.
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
- [ ] Create a Spotify developer app at developer.spotify.com and give
  Claude its client ID (it is not a secret with PKCE sign-in).
- [ ] Choose a license before making the repository public (MIT is a
  common, simple choice).
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
- History: this repository was reset to a single commit. The earlier
  librespot-based client is not kept here; it was a fork of
  https://github.com/crmne/spotifast, which can be read for ideas such as
  its egui theming, but its playback approach is not used.
