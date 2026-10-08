# Plugins

Plugins are optional extras for Resonate. Nothing about them is in the
installer: a plugin is downloaded the first time you turn it on in
Settings, and its files are deleted when you turn it off. With no plugin
on, Resonate has nothing extra on disk and nothing extra running.

## The plugins

| Plugin | What it does | It can |
| --- | --- | --- |
| Sleep timer | Pauses after 15, 30 or 60 minutes, your own length, or at the end of the song, fading out over the last minute and putting the volume back afterwards. | see what is playing, control playback, change the volume |
| Skip rules | Skips songs you never want to hear: by artist, by version (live, remix, sped up), by title, or by length. "Always skip this song" and "Always skip this artist" are in the player bar's plugin menu. | see what is playing, control playback |

Each plugin's settings appear under it in Settings once it is on. Plugins
that offer commands (start a sleep timer, skip this artist) add a puzzle
button to the player bar; it lights up while a plugin has something to
report, such as a sleep timer counting down.

## How it works

- **Only Resonate's own plugins.** Plugins live in this repository
  (`plugins/`), are reviewed like any other change, and are built by the
  release workflow. Each release builds a catalog into the app with the
  SHA-256 of every plugin file and of the helper, so the app installs only
  the exact files built for it, from that release's downloads on GitHub.
  A file that does not match is thrown away. Community plugins may come
  later; they would need a stronger sandbox first.
- **A separate helper runs them.** The app is compiled ahead of time and
  cannot load code, so plugins are JavaScript run by a small helper program
  (`Resonate.PluginHost`, about 5 MB to download). It is downloaded with
  the first plugin you turn on, started only while a plugin is on, runs at
  below-normal priority, and is deleted with the last plugin. If it
  crashes, Resonate restarts it twice at most in ten minutes.
- **Plugins only get what they list.** Each plugin runs in its own
  JavaScript engine with no access to files, the network, other programs,
  or .NET, and no `eval`. It sees and does only what its permissions allow,
  checked by the helper and again by Resonate, with limits on how often it
  can skip, change the volume or show a message. A call that keeps the
  processor busy for more than two seconds is stopped (waiting for a busy
  computer does not count), memory is capped at 64 MB per plugin, and a
  plugin with five errors in a minute is stopped, with the reason shown
  under it in Settings.
- **Updates.** Each Resonate update brings its own copies of the plugins
  you have on and downloads them on the next start; your settings stay.

The downloads come from the repository's GitHub releases, which only work
without a sign-in once the repository is public (the same as updates).

## Writing a plugin

A plugin is a folder under `plugins/` with a `plugin.json` and a script.

```json
{
  "id": "sleep-timer",
  "name": "Sleep timer",
  "version": "1.0.0",
  "description": "One short line for Settings.",
  "author": "Resonate",
  "main": "main.js",
  "permissions": ["player.read", "player.control", "player.volume"],
  "settings": [
    { "key": "fade", "type": "toggle", "title": "Fade out before pausing", "default": true },
    { "key": "customMinutes", "type": "number", "title": "Your own length", "min": 5, "max": 240, "step": 5, "unit": "minutes", "default": 90 }
  ]
}
```

- `id`: lower-case letters, digits and dashes; the folder has the same name.
- `permissions`: `player.read` (see what is playing), `player.control`
  (play, pause, skip, seek), `player.volume` (change the volume).
- `settings`: drawn by Resonate in Settings. Types: `toggle`, `number`
  (`min`, `max`, `step`, `unit`), `text`, `list` (one item per line, given
  to the plugin as an array) and `choice` (`options`: a list of `value` and
  `title`). Every value is checked against its type and limits before the
  plugin sees it.

### The `resonate` object

```js
resonate.player.state            // null, or { title, artists, album, uri, contextUri,
                                 //   isPlaying, position, duration, volume, canSeek }
resonate.player.onChange(fn)     // fn(state, before) whenever anything changes
resonate.player.onTrackChange(fn)// fn(state, before) when the song changes
resonate.player.play() / pause() / next() / previous()
resonate.player.seek(seconds)
resonate.player.setVolume(0.5)   // 0 to 1

resonate.settings.get(key)       // the setting's current value
resonate.settings.all
resonate.settings.set(key, value)
resonate.settings.onChange(fn)   // fn(all, before)

resonate.storage.get(key) / set(key, value) / remove(key) / keys()
                                 // kept between starts, up to 64 KB as JSON

resonate.ui.addCommand(id, title, run)   // an item in the player bar's plugin menu
resonate.ui.removeCommand(id)
resonate.ui.setCommands([{ id, title, run }, ...])  // replaces them all
resonate.ui.setStatus(text)      // a short line in Settings and the menu; null clears it
resonate.ui.notify(text)         // a message at the top of the window

setTimeout, setInterval (at least 250 ms), clearTimeout, clearInterval
console.log, console.error       // the last error shows under the plugin in Settings
```

Positions and durations are in seconds. Times move on by themselves while
a song plays, so `resonate.player.state.position` is always current.

### Testing

Plugins run in the real helper in `tests/Resonate.Plugins.Tests`, on a fake
clock, so a sleep timer's hour passes in milliseconds (see
`SleepTimerTests.cs` and `SkipRulesTests.cs`). Every plugin in `plugins/`
is also checked for a valid `plugin.json`, and CI installs and runs all of
them in the installed Windows app (`--plugin-check`).
