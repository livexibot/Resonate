# Changelog

## [0.19.0](https://github.com/livexibot/Resonate/compare/v0.18.0...v0.19.0) (2026-10-10)


### New features

* only one Resonate runs at a time; starting it again brings the open window forward ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* Settings easier to find your way in: Home's visualizer under Layout, the progress bar and Song stats in their own groups, plugins grouped by what they do, and a General tab ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* Settings, General, Spotify account counts the requests sent to Spotify today and in total ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))


### Fixes

* at a larger App size the window is never cut to its top left corner ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* closing the window quits Resonate; the tray icon is gone and Start with Windows opens it minimised ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* no preset turns on the player bar's visualizer ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* no rings in Home's background ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* one arrow step on the sidebar's grip opens the names from covers only ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* Resonate asks Spotify far less often (slower polls, one queue read per song, no needless checks), so the developer app's allowance lasts ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))
* Windows media controls still reach Spotify while its Web API allowance is used up, and the message says when more is allowed ([ffec70c](https://github.com/livexibot/Resonate/commit/ffec70cd0428f1bccd75197105de1a9a0d7c9125))

## [0.18.0](https://github.com/livexibot/Resonate/compare/v0.17.0...v0.18.0) (2026-10-10)


### New features

* a floating player set wider than the page reaches over the whole window ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* a minimal player in the sidebar under the playlists ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* a playlist filter in the sidebar, and your own playlists first in Search ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* Liked Songs, Local Files and DJ covers follow the accent, with a folder icon for Local Files ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* lyrics in the player show the first two lines before the singing starts, say when there are none and wrap long lines ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* Resonate opens on the song you left off, paused where it was, ready to play on ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* Resonate's own DJ with themed sets from your library, Switch it up and a voice between sets ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* song rows show Play and More on hover, the playing song shows a speaker, and the player's title opens its list at the song ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* the mouse's forward button and Alt+Right go forward again after Back ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* the player beside the page as one panel with a large cover, full controls and Up next, resizable down to a slim rail ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* the progress glow follows the wave, Heartbeat and Dots lines ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))


### Fixes

* readable grey text on every look ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* room between sidebar covers at any Cover size ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* Settings and the queue lie over the page in small windows instead of squeezing it, and Home fits smaller windows ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* the lossless badge is never cut off ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))
* the screensaver waits while Resonate is only in the tray ([1f70390](https://github.com/livexibot/Resonate/commit/1f7039008415d5d15aed21b4c52c48380ccbdee0))

## [0.17.0](https://github.com/livexibot/Resonate/compare/v0.16.0...v0.17.0) (2026-10-09)


### New features

* four new bundled fonts: Tektur, Oxanium, Unbounded and Bodoni Moda ([25dcfcc](https://github.com/livexibot/Resonate/commit/25dcfccbebdecee4da5eac9e23abb3cf5f8588a7))
* Ocean, a new dark preset in Synthwave's place ([25dcfcc](https://github.com/livexibot/Resonate/commit/25dcfccbebdecee4da5eac9e23abb3cf5f8588a7))
* Synthwave, Liquid Chrome, Cyberpunk and Afterhours special looks with moving scenery, weather and decorations ([25dcfcc](https://github.com/livexibot/Resonate/commit/25dcfccbebdecee4da5eac9e23abb3cf5f8588a7))


### Fixes

* Cover size reaches every song cover, with room between rows and sharp large covers ([25dcfcc](https://github.com/livexibot/Resonate/commit/25dcfccbebdecee4da5eac9e23abb3cf5f8588a7))
* the accent lies behind Home's cover instead of the album's colour tile ([25dcfcc](https://github.com/livexibot/Resonate/commit/25dcfccbebdecee4da5eac9e23abb3cf5f8588a7))

## [0.16.0](https://github.com/livexibot/Resonate/compare/v0.15.0...v0.16.0) (2026-10-09)


### New features

* a Screensaver above every app that hides the mouse, with its own visualizer, background, OLED mode and brightness ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* change every keyboard shortcut in Settings, About, Help, with Space to play and pause ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* hide any song list column, the column names included ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* more App size and Text size steps, and a Cover size ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* new plugins: Alarm, Focus timer, Skip intros and outros, Volume per device, Song notifications, Keep PC awake, Now playing file, Quiet hours, Pause for other sounds, Desktop lyrics, Beat glow, Resume on start, Media shortcuts and Export history ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* new progress bars (Shimmer, Comet, Heartbeat, Dots, Ripple) and a progress glow ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* plugin settings in a popup, clearer plugin names and settings for every plugin ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* the player's buttons above the volume, also on a narrower bar instead of dropping the slider ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* the tray icon, editing the queue and the taskbar buttons are part of the app, and Start with Windows is a switch in About ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))


### Fixes

* no song cover in Winamp ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* sharp sidebar covers and cover background, no shortcut hints on hover, and lyrics that start right under the song ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* the moving progress bars stop where they are when paused ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))
* the tray icon and Pause on lock hear from Windows again ([dd20ec6](https://github.com/livexibot/Resonate/commit/dd20ec67effe9abbcb19a6b420f5be48215bfeb0))

## [0.15.0](https://github.com/livexibot/Resonate/compare/v0.14.0...v0.15.0) (2026-10-09)


### New features

* lyrics in the player show "Song · Artist" with the sung line and the next one ([31e2ce2](https://github.com/livexibot/Resonate/commit/31e2ce2cb985a07c732c12a9dfaea9cbae68ddc0))
* reset buttons for a look's colours and plugins' number settings ([31e2ce2](https://github.com/livexibot/Resonate/commit/31e2ce2cb985a07c732c12a9dfaea9cbae68ddc0))


### Fixes

* Resonate's own player plays sound, so Spotify Web API only needs no browser tab ([31e2ce2](https://github.com/livexibot/Resonate/commit/31e2ce2cb985a07c732c12a9dfaea9cbae68ddc0))
* the narrow sidebar's icons and covers sit in the middle ([31e2ce2](https://github.com/livexibot/Resonate/commit/31e2ce2cb985a07c732c12a9dfaea9cbae68ddc0))


### Faster

* faster controls with Resonate's own player ([#62](https://github.com/livexibot/Resonate/issues/62)) ([e96f522](https://github.com/livexibot/Resonate/commit/e96f52292450661aaf7fbf6de0393f5e5cfdd511))

## [0.14.0](https://github.com/livexibot/Resonate/compare/v0.13.0...v0.14.0) (2026-10-09)


### New features

* Japan and Snow looks with scenery, falling petals and snow that gather over the app ([f753005](https://github.com/livexibot/Resonate/commit/f753005794fefc8bc0c4462d95f57f04eb2824f2))


### Fixes

* no more freezes or crashes at 125 % display scaling, and a crash log instead of crashes ([f753005](https://github.com/livexibot/Resonate/commit/f753005794fefc8bc0c4462d95f57f04eb2824f2))
* Sensitivity and Smoothing change the bars when no sound is heard, and Home shows one cover after a skip ([f753005](https://github.com/livexibot/Resonate/commit/f753005794fefc8bc0c4462d95f57f04eb2824f2))

## [0.13.0](https://github.com/livexibot/Resonate/compare/v0.12.0...v0.13.0) (2026-10-09)


### New features

* device picker, clearer Settings, cover colours, new visualizers, reset buttons and a modern Search ([#56](https://github.com/livexibot/Resonate/issues/56)) ([ec65914](https://github.com/livexibot/Resonate/commit/ec6591473b0dbb92d15e9257f02b2f0bfad72555))

## [0.12.0](https://github.com/livexibot/Resonate/compare/v0.11.0...v0.12.0) (2026-10-09)


### New features

* lighter while playing, visualizer styles, tray icon, Velvet theme ([#54](https://github.com/livexibot/Resonate/issues/54)) ([bae9ae8](https://github.com/livexibot/Resonate/commit/bae9ae85ba03174a5b31d4449d45e8d8354f5d4d))

## [0.11.0](https://github.com/livexibot/Resonate/compare/v0.10.1...v0.11.0) (2026-10-09)


### New features

* song stats (BPM, key, loudness, energy) from ReccoBeats, off until switched on ([#52](https://github.com/livexibot/Resonate/issues/52)) ([c1ec1b9](https://github.com/livexibot/Resonate/commit/c1ec1b99756f93f869e487b1a831500728226e46))

## [0.10.1](https://github.com/livexibot/Resonate/compare/v0.10.0...v0.10.1) (2026-10-09)


### Fixes

* steadier playback, smoother visualizer, more layout and effect options ([#50](https://github.com/livexibot/Resonate/issues/50)) ([4a91762](https://github.com/livexibot/Resonate/commit/4a91762f9e7dc4c467a16f56226af800d4d40c1f))

## [0.10.0](https://github.com/livexibot/Resonate/compare/v0.9.1...v0.10.0) (2026-10-09)


### New features

* cover-only Liquid Glass, artist links everywhere, two new plugins ([#48](https://github.com/livexibot/Resonate/issues/48)) ([84bcc71](https://github.com/livexibot/Resonate/commit/84bcc7166fe155d22a1d41f57c4eaf93f8119d94))

## [0.9.1](https://github.com/livexibot/Resonate/compare/v0.9.0...v0.9.1) (2026-10-08)


### Fixes

* one Sleep timer, smart playlists in their chosen order, and safer song lists ([#43](https://github.com/livexibot/Resonate/issues/43)) ([16448dd](https://github.com/livexibot/Resonate/commit/16448dd6b9eb19e745710f1f2efedce99609b97e))
* steadier playback controls, plugins, shortcuts and updates ([#46](https://github.com/livexibot/Resonate/issues/46)) ([2af68c6](https://github.com/livexibot/Resonate/commit/2af68c6e4e0534deb762cee999b1b12d038378d6))
* themes that can't break the window, lyrics at your Text size, and no leftover captions ([#47](https://github.com/livexibot/Resonate/issues/47)) ([5e6d3ab](https://github.com/livexibot/Resonate/commit/5e6d3ab0d9149bde4959a51c0300c3e2de3eed98))


### Faster

* lighter on memory and the processor while idle ([#44](https://github.com/livexibot/Resonate/issues/44)) ([db9d576](https://github.com/livexibot/Resonate/commit/db9d576f80ab448b667657ab2777cc4326257285))

## [0.9.0](https://github.com/livexibot/Resonate/compare/v0.8.0...v0.9.0) (2026-10-08)


### New features

* the 8 October bundle tried on the owner's PC ([#41](https://github.com/livexibot/Resonate/issues/41)) ([4463d4e](https://github.com/livexibot/Resonate/commit/4463d4e8d346b72ca327b4353d9e359fb3e611fe))

## [0.8.0](https://github.com/livexibot/Resonate/compare/v0.7.0...v0.8.0) (2026-10-08)


### New features

* add a visualizer to the Home stage and remove the rings from its clouds ([#37](https://github.com/livexibot/Resonate/issues/37)) ([9a2b811](https://github.com/livexibot/Resonate/commit/9a2b8114a66510dbd3e8823eb6801ce4c95906e2))
* play Spotify on this PC in Web API only mode without the Spotify app ([#34](https://github.com/livexibot/Resonate/issues/34)) ([38feced](https://github.com/livexibot/Resonate/commit/38feceda670cef6aa367d678d2fcfb197d157368))
* Settings in tabs, with its button at the top right ([#36](https://github.com/livexibot/Resonate/issues/36)) ([1154a02](https://github.com/livexibot/Resonate/commit/1154a0297a5820e489b8354784b082bebd82514b))

## [0.7.0](https://github.com/livexibot/Resonate/compare/v0.6.0...v0.7.0) (2026-10-08)


### New features

* a Spotifast-style mini player with Winamp's equalizer and playlist windows ([#29](https://github.com/livexibot/Resonate/issues/29)) ([8998d83](https://github.com/livexibot/Resonate/commit/8998d8315351edf1b7f8dcad6504fe5a6d318fad))
* close the Spotify app when Web API only is on ([#27](https://github.com/livexibot/Resonate/issues/27)) ([dbacf3a](https://github.com/livexibot/Resonate/commit/dbacf3a77e906c4c7822e93dbc0d515bb0dc4cfe))
* settings to scale the whole app and make text bigger ([#30](https://github.com/livexibot/Resonate/issues/30)) ([5bc2e12](https://github.com/livexibot/Resonate/commit/5bc2e12ab3c0a63d27707fbba0f489f80e295407))
* swipe the playing song to skip, and tidier seeking by drag ([#26](https://github.com/livexibot/Resonate/issues/26)) ([6a082db](https://github.com/livexibot/Resonate/commit/6a082db052f717dec3d66840d6ea0cca6e1145ef))
* synced lyrics and nine more features as built-in plugins ([#31](https://github.com/livexibot/Resonate/issues/31)) ([d4f9b4f](https://github.com/livexibot/Resonate/commit/d4f9b4f3f0b8a70d24ed07e88fb8b0a4e426c0c8))

## [0.6.0](https://github.com/livexibot/Resonate/compare/v0.5.0...v0.6.0) (2026-10-08)


### New features

* a hovering player, a sidebar that reaches the bottom, smoother look switching, a bolder look and a livelier Home ([#24](https://github.com/livexibot/Resonate/issues/24)) ([22ac550](https://github.com/livexibot/Resonate/commit/22ac550052239bd40cea4da5605b76306007cdec))
* a Winamp-style classic player, a spinning cover, and a vivid blurred cover behind Liquid Glass ([#18](https://github.com/livexibot/Resonate/issues/18)) ([e0cef8a](https://github.com/livexibot/Resonate/commit/e0cef8aa54c9593450281b8a1570fc966f8f1329))
* Settings in a resizable side pane, more fonts, update progress and tidier settings ([#19](https://github.com/livexibot/Resonate/issues/19)) ([194ad0f](https://github.com/livexibot/Resonate/commit/194ad0fabea12b8e1e0096b3e2bc005f52e2b7c6))

## [0.5.0](https://github.com/livexibot/Resonate/compare/v0.4.0...v0.5.0) (2026-10-08)


### New features

* resizable sidebar, faster covers, local song fixes and a true Web API only mode ([#20](https://github.com/livexibot/Resonate/issues/20)) ([7142033](https://github.com/livexibot/Resonate/commit/71420335b33a29141e13f7cf7eeca22fe7676321))

## [0.4.0](https://github.com/livexibot/Resonate/compare/v0.3.0...v0.4.0) (2026-10-07)


### New features

* optional plugins, downloaded only when turned on ([#12](https://github.com/livexibot/Resonate/issues/12)) ([51d63e8](https://github.com/livexibot/Resonate/commit/51d63e85e8877559c704786c42b92c88ff1c9b5f))
* show your top artists and songs on Spotify on Home ([#13](https://github.com/livexibot/Resonate/issues/13)) ([265b30f](https://github.com/livexibot/Resonate/commit/265b30f0edb4c3caf0bfc9b71d0c040fdfd140c1))


### Fixes

* list an artist's albums again on a real account ([#16](https://github.com/livexibot/Resonate/issues/16)) ([8f25aee](https://github.com/livexibot/Resonate/commit/8f25aee21274fb6482626fdf158b18aea0aa9534))


### Faster

* check speed and memory on every change, and fix what is slow ([#11](https://github.com/livexibot/Resonate/issues/11)) ([bbc3d6c](https://github.com/livexibot/Resonate/commit/bbc3d6c315be0c2f836e5dea0d20c5b90e002a7e))

## [0.3.0](https://github.com/livexibot/Resonate/compare/v0.2.0...v0.3.0) (2026-10-07)


### New features

* add Home, Local Files, DJ, sorting, likes, shuffle, repeat, the queue and an equalizer ([#9](https://github.com/livexibot/Resonate/issues/9)) ([d918c9b](https://github.com/livexibot/Resonate/commit/d918c9be44f93f4b2d936803a7a375ee38833928))
* themes with six presets, a customizer and animated switching ([#10](https://github.com/livexibot/Resonate/issues/10)) ([4ff35df](https://github.com/livexibot/Resonate/commit/4ff35df782c8ef862ae983a70b714dbc218f6fe2))


### Fixes

* make playback and sign-in more reliable ([#7](https://github.com/livexibot/Resonate/issues/7)) ([b262f32](https://github.com/livexibot/Resonate/commit/b262f329df77c334849d148fd00c48759fbc3f32))

## [0.2.0](https://github.com/livexibot/Resonate/compare/v0.1.0...v0.2.0) (2026-10-07)


### New features

* keep Spotify in the background, and offer Web API only control ([#5](https://github.com/livexibot/Resonate/issues/5)) ([5b49142](https://github.com/livexibot/Resonate/commit/5b49142340c3e94990dfc5edb2cd49c972a7845d))

## 0.1.0 (2026-10-07)


### New features

* add the Resonate window ([bdbd6f2](https://github.com/livexibot/Resonate/commit/bdbd6f22b6c783e457ea2c8984ff3b18e6845b28))
* add the Spotify sign-in, Web API client and player logic ([8198498](https://github.com/livexibot/Resonate/commit/819849804713bdfc6d1a5f4be7cb8cc4b0d6a45a))
* control the Spotify app through Windows ([069a0ab](https://github.com/livexibot/Resonate/commit/069a0ab51455ae3659e2ce78d8698c57cae8b239))
* first version of Resonate ([1ecf6e7](https://github.com/livexibot/Resonate/commit/1ecf6e711b7b53041ba9dbfc7c27e492452acbae))


### Fixes

* search on opening the page, and tidy the song list and player bar ([cd8bb5a](https://github.com/livexibot/Resonate/commit/cd8bb5a1bc2b4c4730ff327f753a8c1dea5472e0))
* show the album's colour tile in the player bar from the start ([a0115da](https://github.com/livexibot/Resonate/commit/a0115dadb1fb5c588d16e486796a1c16240b271c))
