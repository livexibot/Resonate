#!/usr/bin/env python3
"""Builds the fonts Resonate bundles for its look editor.

For every family in FAMILIES this downloads the font and its licence from the
google/fonts repository at one pinned commit, checks each download against its
SHA-256, makes static faces at the weights in WEIGHTS (every other axis stays
at its default), gives them clean names, and packs them into one TrueType
Collection (<Stem>.ttc) per family in src/Resonate.App/Assets/Fonts, next to
the family's licence (<Stem>-OFL.txt). Then it opens every file it wrote and
checks the faces, names and weights.

    python3 tools/fonts/build_fonts.py

Needs Python 3.10 or newer and fontTools 4.66.1 (pip install fonttools==4.66.1).
Running it again writes byte-for-byte the same files. See README.md here.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import re
import sys
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path

import fontTools
from fontTools.ttLib import TTCollection, TTFont
from fontTools.varLib import instancer

# The google/fonts commit every file comes from (main on 8 October 2026).
SOURCE_COMMIT = "5e8a3ba899557829a76cfdac30fa512bda91d7ca"
SOURCE_URL = "https://raw.githubusercontent.com/google/fonts/{commit}/{path}"

# The weights each collection holds. The app uses Normal (400) and SemiBold (600).
WEIGHTS = (400, 600, 700)
STYLE_NAMES = {400: "Regular", 600: "SemiBold", 700: "Bold"}

# The fontTools version that made the files in the repository. Another version
# may make slightly different (but equally valid) files.
FONTTOOLS_VERSION = "4.66.1"

# (family name, folder in google/fonts, font file). The font file is a variable
# font, or a dictionary of one static file per weight. The family name is what
# the app asks for, and the file is <family name without spaces>.ttc.
FAMILIES: list[tuple[str, str, str | dict[int, str]]] = [
    # Sans serif
    ("Inter", "ofl/inter", "Inter[opsz,wght].ttf"),
    ("Manrope", "ofl/manrope", "Manrope[wght].ttf"),
    ("DM Sans", "ofl/dmsans", "DMSans[opsz,wght].ttf"),
    ("Plus Jakarta Sans", "ofl/plusjakartasans", "PlusJakartaSans[wght].ttf"),
    ("Outfit", "ofl/outfit", "Outfit[wght].ttf"),
    ("Figtree", "ofl/figtree", "Figtree[wght].ttf"),
    ("Space Grotesk", "ofl/spacegrotesk", "SpaceGrotesk[wght].ttf"),
    ("Work Sans", "ofl/worksans", "WorkSans[wght].ttf"),
    ("Sora", "ofl/sora", "Sora[wght].ttf"),
    ("Rubik", "ofl/rubik", "Rubik[wght].ttf"),
    ("Montserrat", "ofl/montserrat", "Montserrat[wght].ttf"),
    ("Poppins", "ofl/poppins", {
        400: "Poppins-Regular.ttf",
        600: "Poppins-SemiBold.ttf",
        700: "Poppins-Bold.ttf",
    }),
    ("Nunito", "ofl/nunito", "Nunito[wght].ttf"),
    ("Geist", "ofl/geist", "Geist[wght].ttf"),
    # Serif
    ("Literata", "ofl/literata", "Literata[opsz,wght].ttf"),
    ("Newsreader", "ofl/newsreader", "Newsreader[opsz,wght].ttf"),
    # Monospace
    ("JetBrains Mono", "ofl/jetbrainsmono", "JetBrainsMono[wght].ttf"),
    ("Geist Mono", "ofl/geistmono", "GeistMono[wght].ttf"),
]

# Not bundled, because their licence reserves the font name (a changed copy,
# such as one cut to three weights, may not use it): Lora, Playfair Display,
# Merriweather, Lexend, IBM Plex, and Adobe's Source fonts (Adobe reserves
# "Source" upstream). Left out to keep the download small: EB Garamond,
# Crimson Pro and Onest.

# SHA-256 of every file downloaded from SOURCE_COMMIT. After changing the
# commit or the families, run the script: it stops and prints the lines to put
# here for any file whose hash is missing.
SOURCE_SHA256: dict[str, str] = {
    "ofl/inter/Inter[opsz,wght].ttf": "29160a80ff49ddcab2c97711247e08b1fab27a484a329ce8b813d820dc559031",
    "ofl/inter/OFL.txt": "5b9321a4298cfeb6b34354164a1c3afc3db114569984c502b9b35d988fd58c57",
    "ofl/manrope/Manrope[wght].ttf": "3ae11c49db0455a3cc33e37d380f20fdb8c7f8b41dc07625c177e3d87a9d6ae6",
    "ofl/manrope/OFL.txt": "58172e0c0fac2cda8a37b348164bb55e44b0e69051e557e92b1d3f6910141f7b",
    "ofl/dmsans/DMSans[opsz,wght].ttf": "8cd08d97e89c24d0aa92edd2f0f4c8ee6195eee9b7c9f154865a58b02f0c1c0d",
    "ofl/dmsans/OFL.txt": "9af36190332437f5ecd09974de43c1f7c77a310a996cdd8ceb25628b458840e1",
    "ofl/plusjakartasans/PlusJakartaSans[wght].ttf": "89b3fb38aa0d275d7a731d0d817a4f1622b316b4d7fbdedcf02ee9099ff68bc8",
    "ofl/plusjakartasans/OFL.txt": "995c7199cab65954f545996326755daee7b63cc6b42b06c13da1f9502ab08a99",
    "ofl/outfit/Outfit[wght].ttf": "fc7287273e66929776e2ba54f144fe699080bec29f61bf649d70d871468aeade",
    "ofl/outfit/OFL.txt": "c676351bf8576b9aba743cd5eaa8c0e7ee0d51f805d720447b4df4ddb6a2e416",
    "ofl/figtree/Figtree[wght].ttf": "26ad3db9b31ff7dde67a91ff515d022d2f495cd506590699cf264f0bfe6fb714",
    "ofl/figtree/OFL.txt": "140d37233e7f3ce7313798befa9600893bcceaf41a55fa0fa5ad52f7f657a268",
    "ofl/spacegrotesk/SpaceGrotesk[wght].ttf": "acad6de1fc93436f5c0f1f4137751ef04f1aea3063e7036535970ffcfbd79f72",
    "ofl/spacegrotesk/OFL.txt": "564ce565c371c5e5bbf286006565a7c9aa55a9f56e7ca58d56e05d649dd61a72",
    "ofl/worksans/WorkSans[wght].ttf": "f50f61f2ba738e239442d40bf1069adb195c224b6a5a73a581fc2f3ed62a9f63",
    "ofl/worksans/OFL.txt": "749aca05078664ce682dce1b1b10096ac397cb088c1a6df4e1bb56f0092a9272",
    "ofl/sora/Sora[wght].ttf": "84ff7096ae3ec6c8be47d906d1a0ba4de7f2ce78c615275c77301964a316e16c",
    "ofl/sora/OFL.txt": "ba0b9729c9428ba79a0459ab8ec575791b51509dbec213e383d0316d37fec299",
    "ofl/rubik/Rubik[wght].ttf": "1b3a7437ba2af80e465e773ed60c5036d1ba6ace492d89046dbcf18fb31e4e88",
    "ofl/rubik/OFL.txt": "472cbe7c25441df63e9c7864b43eb3c0f4b3df950c66a76224e6cfe1eae843fb",
    "ofl/montserrat/Montserrat[wght].ttf": "0f7b311b2f3279e4eef9b2f968bcdbab6e28f4daeb1f049f4f278a902bcd82f7",
    "ofl/montserrat/OFL.txt": "8b7141c03fa4f8d44e6345d5d4931709290f0f67875e452e95ac1fd3a027802e",
    "ofl/poppins/Poppins-Regular.ttf": "7e65201e9b79159e2300267cc885e16c8dcef2424cdfa09a29bfb0980a94a7ba",
    "ofl/poppins/Poppins-SemiBold.ttf": "d3bf1bdaf0550e83da9ac0b1d1d9fe6db086835a83aa28578e609a394b9a0286",
    "ofl/poppins/Poppins-Bold.ttf": "983676516167748b74de6f4771fb384c664fd913acb8b471122ecacf5da5ea6c",
    "ofl/poppins/OFL.txt": "6be04893d770899a015649c7aa3b582f871b272f8747a92b78b17c3e5c8b2573",
    "ofl/nunito/Nunito[wght].ttf": "bb55a5ca5c2042335b3991af27c4d0705d0ef41cac6164ac737fd8f2a1e85207",
    "ofl/nunito/OFL.txt": "580df76c95a1ec5ab878ceb25bb3d85c6a076804e9c970c8c6972aea775fdf65",
    "ofl/geist/Geist[wght].ttf": "73894e0448cae90a92b6c2f8732b7bb9acb7b94c418bff559dad4a18e1de9659",
    "ofl/geist/OFL.txt": "1781d2806a07d91c4edf4740b88449fab7d0eadad53f7c351b94cd4d4eb8c00f",
    "ofl/literata/Literata[opsz,wght].ttf": "b41138c9373112f32abb589cc22e8674b06ed4048b0c513be922bdd26f274440",
    "ofl/literata/OFL.txt": "8742963604cd89dc81437811a850018fc03b2bfad686d7422c8235967c87614e",
    "ofl/newsreader/Newsreader[opsz,wght].ttf": "8a08d13f8a6c0d51be379a60af84f945f65369a67e509ee3c3bdcc421254d7c1",
    "ofl/newsreader/OFL.txt": "fdfad38143ec470553cae82a1e45320bdd1b9ec70415d37bd0171051d8a4ded8",
    "ofl/jetbrainsmono/JetBrainsMono[wght].ttf": "48715a42ec242c21e9f02692891e147d022299a52e48d5e413e1a942193ffeda",
    "ofl/jetbrainsmono/OFL.txt": "b2fe5e8987594e9ffd1d2ca52a2f5d73eb8335243893c5d6254b5ad69269591d",
    "ofl/geistmono/GeistMono[wght].ttf": "d00e590b8eb3a59acc329b2d044fd143ae935090b7da33199ebee27cc7de8196",
    "ofl/geistmono/OFL.txt": "1781d2806a07d91c4edf4740b88449fab7d0eadad53f7c351b94cd4d4eb8c00f",
}

REPO_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = REPO_ROOT / "src" / "Resonate.App" / "Assets" / "Fonts"

# Tables that only variable fonts have; none may be left in a static face.
VARIATION_TABLES = ("fvar", "gvar", "avar", "cvar", "HVAR", "VVAR", "MVAR", "STAT")

# Tables dropped from every face: a digital signature does not survive editing.
DROPPED_TABLES = ("DSIG",)

# Name records this script writes itself (all others are kept as they are).
# 18, 21 and 22 (Mac compatible full name, WWS family and subfamily) are
# dropped so nothing contradicts the names below.
MANAGED_NAME_IDS = (1, 2, 3, 4, 6, 16, 17, 18, 21, 22, 25)

FS_ITALIC = 1 << 0
FS_BOLD = 1 << 5
FS_REGULAR = 1 << 6
MAC_BOLD = 1 << 0
MAC_ITALIC = 1 << 1


class BuildError(Exception):
    pass


def stem(family: str) -> str:
    return family.replace(" ", "")


def source_paths(folder: str, fonts: str | dict[int, str]) -> list[str]:
    files = [fonts] if isinstance(fonts, str) else [fonts[w] for w in WEIGHTS]
    return [f"{folder}/{name}" for name in files] + [f"{folder}/OFL.txt"]


def download(paths: list[str], into: Path) -> dict[str, Path]:
    """Downloads each path from SOURCE_COMMIT and checks its SHA-256."""
    files: dict[str, Path] = {}
    missing: list[str] = []
    for path in paths:
        url = SOURCE_URL.format(commit=SOURCE_COMMIT, path=urllib.parse.quote(path))
        target = into / path
        target.parent.mkdir(parents=True, exist_ok=True)
        print(f"  download {path}")
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        digest = hashlib.sha256(data).hexdigest()
        expected = SOURCE_SHA256.get(path)
        if expected is None:
            missing.append(f'    "{path}": "{digest}",')
        elif expected != digest:
            raise BuildError(f"{path}: SHA-256 is {digest}, expected {expected}")
        target.write_bytes(data)
        files[path] = target
    if missing:
        raise BuildError(
            "These files have no pinned SHA-256 yet. Check them, then add these lines "
            "to SOURCE_SHA256:\n" + "\n".join(missing))
    return files


def check_licence(path: str, text: str) -> None:
    """Only OFL 1.1 fonts without a Reserved Font Name may be renamed and repacked."""
    if "SIL Open Font License, Version 1.1" not in text:
        raise BuildError(f"{path} is not the SIL Open Font License 1.1")
    copyright_block = text.split("This Font Software is licensed", 1)[0]
    if re.search(r"reserved\s+font\s+name", copyright_block, re.IGNORECASE):
        raise BuildError(f"{path} declares a Reserved Font Name, so this font cannot be modified")


def licence_text(raw: bytes) -> str:
    """The licence as UTF-8 with LF line endings and one final newline (as git stores it)."""
    text = raw.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")
    return text.rstrip("\n") + "\n"


def static_face(file: Path, weight: int) -> TTFont:
    """One static face at the given weight, from a variable or a static font."""
    font = TTFont(file, recalcTimestamp=False)
    if "fvar" not in font:
        if font["OS/2"].usWeightClass != weight:
            raise BuildError(f"{file.name} has weight {font['OS/2'].usWeightClass}, expected {weight}")
        return font

    location: dict[str, float] = {}
    for axis in font["fvar"].axes:
        if axis.axisTag == "wght":
            if not axis.minValue <= weight <= axis.maxValue:
                raise BuildError(f"{file.name} has no weight {weight}")
            location["wght"] = weight
        else:
            location[axis.axisTag] = axis.defaultValue
    if "wght" not in location:
        raise BuildError(f"{file.name} has no weight axis")
    try:
        face = instancer.instantiateVariableFont(font, location, updateFontNames=True, static=True)
    except (ValueError, KeyError):
        # updateFontNames needs a STAT table it understands; the names are
        # rewritten below either way.
        font = TTFont(file, recalcTimestamp=False)
        face = instancer.instantiateVariableFont(font, location, static=True)
    face.recalcTimestamp = False
    return face


def clean_up(face: TTFont, family: str, weight: int) -> None:
    """Gives a face the names, weight and style bits Windows expects."""
    style = STYLE_NAMES[weight]
    ribbi = weight in (400, 700)  # Regular and Bold belong to the family itself
    name = face["name"]

    if face["post"].italicAngle != 0 or face["OS/2"].fsSelection & FS_ITALIC:
        raise BuildError(f"{family}: expected an upright font")

    version_match = re.search(r"\d+\.\d+", name.getDebugName(5) or "")
    version = version_match.group(0) if version_match else f"{face['head'].fontRevision:.3f}"
    vendor = face["OS/2"].achVendID.replace("\0", "").strip() or "NONE"
    postscript = f"{stem(family)}-{style}"
    names = {
        1: family if ribbi else f"{family} {style}",
        2: style if ribbi else "Regular",
        3: f"{version};{vendor};{postscript}",
        4: f"{family} {style}",
        6: postscript,
    }
    if not ribbi:
        names[16] = family
        names[17] = style

    for tag in VARIATION_TABLES + DROPPED_TABLES:
        if tag in face:
            del face[tag]
    # Windows reads the Windows (platform 3) names only.
    name.names = [r for r in name.names if r.platformID == 3 and r.nameID not in MANAGED_NAME_IDS]
    for name_id, value in names.items():
        name.setName(value, name_id, 3, 1, 0x409)
    name.removeUnusedNames(face)

    os2 = face["OS/2"]
    os2.usWeightClass = weight
    selection = os2.fsSelection & ~(FS_ITALIC | FS_BOLD | FS_REGULAR)
    if weight == 400:
        selection |= FS_REGULAR
    elif weight == 700:
        selection |= FS_BOLD
    os2.fsSelection = selection
    head = face["head"]
    head.macStyle = (head.macStyle & ~(MAC_BOLD | MAC_ITALIC)) | (MAC_BOLD if weight == 700 else 0)


def build_collection(family: str, folder: str, fonts: str | dict[int, str],
                     files: dict[str, Path]) -> bytes:
    faces = []
    for weight in WEIGHTS:
        font_file = fonts if isinstance(fonts, str) else fonts[weight]
        face = static_face(files[f"{folder}/{font_file}"], weight)
        clean_up(face, family, weight)
        faces.append(face)
    collection = TTCollection()
    collection.fonts = faces
    buffer = io.BytesIO()
    collection.save(buffer, shareTables=True)
    return buffer.getvalue()


def name_of(face: TTFont, name_id: int) -> str | None:
    record = face["name"].getName(name_id, 3, 1, 0x409)
    return record.toUnicode() if record else None


def verify(family: str, path: Path, seen_names: set[tuple[int, str]]) -> None:
    """Opens a written collection and checks every face."""
    collection = TTCollection(path)
    weights = []
    for face in collection.fonts:
        os2, head = face["OS/2"], face["head"]
        weight = os2.usWeightClass
        weights.append(weight)
        style = STYLE_NAMES.get(weight)
        typographic = name_of(face, 16) or name_of(face, 1)
        subfamily = name_of(face, 17) or name_of(face, 2)
        problems = []
        if style is None:
            problems.append(f"unexpected weight {weight}")
        if typographic != family:
            problems.append(f"family is {typographic!r}")
        if subfamily != style:
            problems.append(f"subfamily is {subfamily!r}")
        if "glyf" not in face or "CFF " in face or "CFF2" in face:
            problems.append("not TrueType outlines")
        problems += [f"has a {tag} table" for tag in VARIATION_TABLES + DROPPED_TABLES if tag in face]
        bold = weight == 700
        if bool(os2.fsSelection & FS_BOLD) != bold or bool(head.macStyle & MAC_BOLD) != bold:
            problems.append("bold bits do not match the weight")
        if bool(os2.fsSelection & FS_REGULAR) != (weight == 400):
            problems.append("regular bit does not match the weight")
        if os2.fsSelection & FS_ITALIC or head.macStyle & MAC_ITALIC:
            problems.append("marked italic")
        for name_id in (1, 2, 3, 4, 6, 16, 17):
            value = name_of(face, name_id)
            if value is not None and (not value.isprintable() or value != value.strip()):
                problems.append(f"name {name_id} {value!r} has hidden characters")
        for name_id in (3, 4, 6):  # unique ID, full name, PostScript name
            key = (name_id, name_of(face, name_id) or "")
            if key in seen_names:
                problems.append(f"name {name_id} {key[1]!r} is not unique")
            seen_names.add(key)
        if problems:
            raise BuildError(f"{path.name} {weight}: " + "; ".join(problems))
    if weights != list(WEIGHTS):
        raise BuildError(f"{path.name} has weights {weights}, expected {list(WEIGHTS)}")


def write_if_changed(path: Path, data: bytes) -> None:
    if path.exists() and path.read_bytes() == data:
        return
    temp = path.with_name(path.name + ".tmp")
    temp.write_bytes(data)
    temp.replace(path)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--download-dir", type=Path,
                        help="keep the downloads in this new, empty folder (default: a temporary folder)")
    args = parser.parse_args()

    if fontTools.version != FONTTOOLS_VERSION:
        print(f"warning: fontTools {fontTools.version} is not {FONTTOOLS_VERSION}; "
              "the files may differ slightly from the ones in the repository", file=sys.stderr)

    names = [family for family, _, _ in FAMILIES]
    if len(set(names)) != len(names):
        raise BuildError("family names must be unique")

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    temp_dir = None
    if args.download_dir:
        download_dir = args.download_dir
        download_dir.mkdir(parents=True, exist_ok=True)
        if any(download_dir.iterdir()):
            raise BuildError(f"{download_dir} is not empty")
    else:
        temp_dir = tempfile.TemporaryDirectory(prefix="resonate-fonts-")
        download_dir = Path(temp_dir.name)

    try:
        print(f"google/fonts at {SOURCE_COMMIT}")
        paths = [p for _, folder, fonts in FAMILIES for p in source_paths(folder, fonts)]
        files = download(paths, download_dir)

        expected_files = set()
        for family, folder, fonts in FAMILIES:
            licence = files[f"{folder}/OFL.txt"].read_bytes()
            text = licence_text(licence)
            check_licence(f"{folder}/OFL.txt", text)
            print(f"  build {family}")
            ttc = OUTPUT_DIR / f"{stem(family)}.ttc"
            ofl = OUTPUT_DIR / f"{stem(family)}-OFL.txt"
            write_if_changed(ttc, build_collection(family, folder, fonts, files))
            write_if_changed(ofl, text.encode("utf-8"))
            expected_files |= {ttc.name, ofl.name}

        # Remove the files of families no longer in the list.
        for path in sorted(OUTPUT_DIR.iterdir()):
            if (path.suffix == ".ttc" or path.name.endswith("-OFL.txt")) and path.name not in expected_files:
                print(f"  remove {path.name}")
                path.unlink()

        seen_names: set[tuple[int, str]] = set()
        total = 0
        print()
        for family, _, _ in FAMILIES:
            path = OUTPUT_DIR / f"{stem(family)}.ttc"
            verify(family, path, seen_names)
            licence = OUTPUT_DIR / f"{stem(family)}-OFL.txt"
            check_licence(licence.name, licence.read_text(encoding="utf-8"))
            size = path.stat().st_size
            total += size
            print(f"  {path.name:<24} {size / 1024:8.0f} KB  {family}")
        print(f"  {'total':<24} {total / 1024:8.0f} KB  {len(FAMILIES)} families")
    finally:
        if temp_dir:
            temp_dir.cleanup()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BuildError as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
