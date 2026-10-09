# Bundled fonts

Resonate ships a few well-known interface fonts in
`src/Resonate.App/Assets/Fonts`, so a look can use them on any PC, installed
or not. `build_fonts.py` makes those files; this page says how.

## What is in the folder

For each family, two files:

- `<Name>.ttc`: one font collection with three static faces, Regular (400),
  SemiBold (600) and Bold (700). The app uses Regular and SemiBold.
- `<Name>-OFL.txt`: the family's licence, the SIL Open Font License 1.1.

The app lists them in `src/Resonate.Themes/BundledFonts.cs`. The tests in
`tests/Resonate.Themes.Tests/BundledFontsTests.cs` check that the list and
the folder match and that every file holds the right faces and names.

The families: Inter, Manrope, DM Sans, Plus Jakarta Sans, Outfit, Figtree,
Space Grotesk, Work Sans, Sora, Rubik, Montserrat, Poppins, Nunito and Geist
(sans serif); Literata and Newsreader (serif); JetBrains Mono and Geist Mono
(monospace); Tektur, Oxanium, Unbounded and Bodoni Moda (display faces for
the Synthwave, Cyberpunk, Liquid Chrome and Afterhours looks). About 9 MB
in all.

## Rebuilding

You need Python 3.10 or newer and fontTools 4.66.1:

```
pip install fonttools==4.66.1
python3 tools/fonts/build_fonts.py
```

The script downloads every font and licence from the
[google/fonts](https://github.com/google/fonts) repository at one pinned
commit into a temporary folder, and stops if a file's SHA-256 is not the one
written in the script. For each family it makes the three weights from the
variable font (every other axis, such as optical size, stays at its
default; Poppins has no variable font, so its static files are used), gives
each face clean names, packs them into one `.ttc`, copies the
licence, and then opens every file again to check it. Running it again
writes exactly the same files.

## Changing the list

Everything to change is at the top of the script:

- **Add a family:** add a line to `FAMILIES`, run the script, check the
  files it prints and add their SHA-256 lines to `SOURCE_SHA256`, run it
  again, then add the family to `BundledFonts.cs`.
- **Update the fonts:** set `SOURCE_COMMIT` to a newer google/fonts commit,
  empty `SOURCE_SHA256`, and do the same.

Only fonts under the Open Font License 1.1 that reserve no font name can be
bundled, because cutting a font down to three weights and renaming the faces
is a change the licence would otherwise forbid under that name. The script
refuses any other. That rules out Lora, Playfair Display, Merriweather,
Lexend, IBM Plex and Adobe's Source fonts.
