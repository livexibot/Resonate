# Resonate

A fast, modern Windows interface for Spotify, with themes and smooth
animations, that keeps Spotify's own lossless playback.

> **Status: early versions in testing.** Home with listening stats, your
> top artists and songs on Spotify and daily mixes, the library,
> playlists, search, Local Files, DJ, sorting, truly random shuffle,
> repeat, the queue, the equalizer, themes, a Winamp-style classic player,
> optional plugins and automatic updates are built. It has not been tried with a real Spotify account yet.
> The plan is in [CLAUDE.md](CLAUDE.md).

## Why

Spotify's desktop app can take seconds to open, its menus lag behind the
monitor's refresh rate, and it looks dated. Resonate aims to open in well
under a second, run at your display's full refresh rate, look clean, and let
you theme every part of it.

## Themes

Six looks to start from (Midnight, Daylight, Liquid Glass, Pure Black,
Synthwave and Paper), and a customizer for everything: colours, light or
dark, the backdrop, corners, shadows, fonts (Windows' own plus 18
open-licence fonts that come with Resonate), and how the player, its
progress bar and buttons look. The player can sit docked along the bottom,
float, or hover as a centred pill over the page, and the sidebar can reach
the bottom of the window. Save your own looks, share them as text, and pick
how switching animates: a morph, a cross-fade, a spread from the middle, a
ripple from the click and more, each eased over one to two seconds. Liquid
Glass puts the playing song's cover, blurred, behind the window, and if you
like, the cover can spin like a record; both are switches in Settings.
App size makes everything in the window larger or smaller (Ctrl+Plus and
Ctrl+Minus too), and Text size makes just the text larger.

## Classic player

A player in the style of Winamp 2 can take the player bar's place, with
its own original skin built in. Add any classic `.wsz` skin you have, show
it at double size, or roll it up to one line. Its visualiser moves for
your own music files; Spotify's sound can't be analysed.

## Plugins

Optional extras that are downloaded only when you turn them on in Settings,
and deleted when you turn them off: a sleep timer that fades out, and skip
rules for artists, versions (live, remix, sped up) or lengths you never
want to hear. See [docs/plugins.md](docs/plugins.md).

## How it works

Resonate never plays Spotify's music itself. The official Spotify app runs
hidden in the background and does all the playback, so every song plays in
Spotify Lossless. Resonate is the window you use: it controls Spotify
through Windows' own media controls (instant play, pause and skip) and
through Spotify's official Web API (library, search, playlists). The
equalizer in Settings is Spotify's own.

The only music Resonate plays itself is your own files in Local Files,
which Spotify does not let other apps start.

Want the Spotify app closed? Pick "Spotify Web API only" in Settings:
Resonate then closes the desktop app, controls Spotify only through the Web
API and plays on any Spotify Connect device you choose (your phone, a
speaker, the web player). Nothing on this PC plays Spotify's songs in that
mode; switch back to Windows media controls for sound here.

## Requirements

- Windows 10 or 11
- Spotify Premium
- The Spotify desktop app, installed and signed in (or, with "Spotify Web
  API only", any Spotify app or speaker that is online)
- A free Spotify developer app of your own (Resonate's first screen walks
  you through it in about two minutes)

## How this project is developed

Every change is a pull request with a plain-language description, merged
into `main` as one commit. Releases are automatic: each one lists its
changes in `CHANGELOG.md` and on the Releases page, and
installed copies update themselves.

Resonate is an independent project for Spotify and is not affiliated with
Spotify.
