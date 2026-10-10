# Resonate

A fast, good-looking Windows app for Spotify. It opens in under a second,
runs at your monitor's full refresh rate and looks the way you want, while
Spotify's own app keeps playing every song in Lossless.

![Resonate's Home: the playing song on stage, with a visualizer that follows the music](docs/images/home.jpg)

**[Download Resonate for Windows](https://github.com/livexibot/Resonate/releases/latest/download/Resonate-win-x64-Setup.exe)**
· [All versions](https://github.com/livexibot/Resonate/releases)

Windows may say it does not recognise the installer, since it is not
signed yet: choose **More info**, then **Run anyway**. Resonate updates
itself after that.

## What it does

- **Your Spotify library, without the wait.** Home, playlists, Liked Songs,
  search, artist and album pages, the queue and synced lyrics, with
  smooth scrolling and instant clicks.
- **Lossless sound.** You choose what to play in Resonate; the official
  Spotify app plays it in Lossless, hidden in the background.
- **Twenty looks and a full customizer.** Colours, light or dark,
  backgrounds, corners, fonts and where the player sits. The special looks
  have moving scenery and weather.
- **A Home that comes alive.** The playing song on a stage, your listening
  stats, your top artists and daily mixes, and Resonate's own DJ.
- **Extras you can turn on.** A Winamp player, a screensaver, smart
  playlists, an alarm, a sleep timer, media shortcuts and more, in
  Settings, Plugins.
- **Your own music files** in Local Files, beside your Spotify library.

| A playlist | Settings |
| --- | --- |
| ![A playlist in the Midnight look](docs/images/playlist.jpg) | ![Settings, open at the looks](docs/images/settings.jpg) |

## Looks

Pick one in Settings, Themes, change any part of it, and save your own.

![Twelve of Resonate's twenty looks: Midnight, Daylight, Liquid Glass, Velvet, Paper, Terminal, Japan, Snow, Synthwave, Cyberpunk, Afterhours and Liquid Chrome](docs/images/looks.jpg)

## Winamp

<img src="docs/images/winamp-mini.png" width="275" align="right" alt="The Winamp mini player with its equalizer and playlist">

Use a Winamp 2 player in place of the player bar, or press Ctrl+M for a
mini player that stays on top, with Winamp's equalizer and playlist under
it. Add any classic `.wsz` skin you like.

<br clear="right">

## What you need

- Windows 10 or 11
- Spotify Premium
- The Spotify desktop app, signed in
- A free Spotify developer app of your own: Resonate's first screen walks
  you through it in about two minutes

## How it works

Resonate never plays Spotify's music itself. The Spotify app runs hidden
and plays everything in Lossless. Play, pause and skip go to it straight
through Windows' media controls. Choosing a song, a playlist or the DJ,
and showing your library and search, go through Spotify's Web API with
your own developer app.

Spotify limits how many requests a developer app may make. Resonate asks
as little as it can and counts every request (Settings, General, Spotify
account). If Spotify ever refuses for a while, play, pause and skip keep
working, and Resonate says when choosing songs works again.

Rather close the Spotify app? Choose "Spotify Web API only" in Settings,
Playback. Resonate then plays through Spotify's official web player,
hidden inside it, at 256 kbps instead of Lossless.

## More

- [Plugins](docs/plugins.md)
- [What changed in each version](CHANGELOG.md)
- [Helping with Resonate](CONTRIBUTING.md)

Resonate is open source under the [MIT licence](LICENSE).

Resonate is an independent project for Spotify and is not affiliated with
Spotify.
