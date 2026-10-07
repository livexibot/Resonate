# Resonate

A fast, good-looking desktop interface for Spotify that keeps Spotify's own
lossless playback.

This repository was reset on purpose to start over. Nothing is built yet.
This file is the brief: what the owner wants, how it should work, and what
has already been decided. Read all of it before writing code, and keep it
up to date as decisions change.

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
6. A GitHub Actions release workflow (on `v*` tags) that builds the
   Windows app (x64, and arm64 if cheap) and publishes it, plus an in-app
   updater that installs new releases from this repository. Release signing uses a key the owner
   creates; never commit a private key.

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
