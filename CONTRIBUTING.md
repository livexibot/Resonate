# Helping with Resonate

Thank you for wanting to help. Resonate is a small project with one owner,
so every issue and pull request is read, and nothing is merged until the
owner has approved it.

## Something is wrong

[Open an issue](https://github.com/livexibot/Resonate/issues/new/choose)
with the "Something is wrong" form. Say what happened, how to make it
happen, and your Resonate version (Settings, General, Updates). If
Resonate crashed or stopped playing, add `crash.log` and `playback.log`
from `%LocalAppData%\Resonate\data`.

Security problems go [privately](https://github.com/livexibot/Resonate/security/advisories/new),
not in an issue (see [SECURITY.md](SECURITY.md)).

## An idea

Open an issue with the "An idea" form. Pictures or examples from other
apps help.

## Code

1. For anything larger than a small fix, open an issue first, so we can
   agree on it before you spend time on it.
2. Fork the repository, make a branch, and open a pull request into `main`
   with a title like `feat: ...` or `fix: ...`. Fill in the template:
   what was there before, what is there after, and how.
3. CI builds the app, runs the tests and takes screenshots. It runs on
   your pull request once it has been allowed to.
4. The pull request is reviewed, and merged once the owner approves it.

### What Resonate will not take

- Anything that plays, records, downloads or saves Spotify's audio inside
  Resonate, librespot, or anything that works around Spotify's copy
  protection. Spotify's own software does all playback.
- Ad blocking, or unlocking Premium features.
- Telemetry, accounts or a server. Resonate talks only to Spotify, GitHub
  (updates), LRCLIB (lyrics) and ReccoBeats (song stats, when turned on).
- An embedded browser for the interface.
- Third-party Winamp skins in the repository (users add their own).
- Plugins from outside the project, for now.

### Building

On Windows, with the .NET 10 SDK, Visual Studio Build Tools with the C++
tools, and the Windows SDK 10.0.26100:

```
dotnet build src/Resonate.App/Resonate.App.csproj -c Debug -p:Platform=x64
dotnet test
```

Start the built app with `--demo` for made-up music, so you need no
Spotify account to try a change. Run `dotnet format` before you push.

The Spotify logic (`src/Resonate.Spotify`), the themes (`src/Resonate.Themes`)
and their tests also build and run on Linux.

## Be kind

Everyone here follows the [code of conduct](CODE_OF_CONDUCT.md).
