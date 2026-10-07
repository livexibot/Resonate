# Resonate

A fast, modern Windows interface for Spotify, with themes and smooth
animations, that keeps Spotify's own lossless playback.

> **Status: first version in testing.** Sign-in, the library, playlists,
> search, the player bar, themes, plugins and automatic updates are built;
> it has not been tried with a real Spotify account yet. Nothing is
> released, so there is nothing to download yet. The plan is in
> [CLAUDE.md](CLAUDE.md).

## Why

Spotify's desktop app can take seconds to open, its menus lag behind the
monitor's refresh rate, and it looks dated. Resonate aims to open in well
under a second, run at your display's full refresh rate, look clean, and let
you theme every part of it.

## Themes

Six looks to start from (Midnight, Daylight, Liquid Glass, Pure Black,
Synthwave and Paper), and a customizer for everything: colours, light or
dark, the backdrop (even the playing song's cover, blurred), corners,
shadows, fonts, and how the player, its progress bar and buttons look. Save
your own looks, share them as text, and pick how switching animates.

## Plugins

Optional extras that are downloaded only when you turn them on in Settings,
and deleted when you turn them off: a sleep timer that fades out, and skip
rules for artists, versions (live, remix, sped up) or lengths you never
want to hear. See [docs/plugins.md](docs/plugins.md).

## How it works

Resonate does not play music itself. The official Spotify app runs hidden in
the background and does all the playback, so every song plays in Spotify
Lossless. Resonate is the window you use: it controls Spotify through
Windows' own media controls (instant play, pause and skip) and through
Spotify's official Web API (library, search, playlists).

## Requirements

- Windows 10 or 11
- Spotify Premium
- The Spotify desktop app, installed and signed in
- A free Spotify developer app of your own (Resonate's first screen walks
  you through it in about two minutes)

## How this project is developed

Every change is a pull request with a plain-language description, merged
into `main` as one commit. Releases are automatic: each one lists its
changes in `CHANGELOG.md` and on the Releases page, and
installed copies update themselves.

Resonate is an independent project for Spotify and is not affiliated with
Spotify.
