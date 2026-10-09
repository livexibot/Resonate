"""Liquid Chrome: liquid metal (see build_scene_art.py, which writes it into SceneArt.xaml).

A dark studio: soft lights high up, a ribbon of chrome flowing across the
sky, mirrored blobs drifting in it, and a sea of liquid metal rolling below,
each swell in its own rhythm. Moving parts carry their motion as a Tag (see
Resonate.Themes/SceneMotion.cs).
"""
import math
import random

from sceneart import *  # noqa: F401,F403

W, H = 2400, 520

# Chrome: the sky's light above, the dark of the horizon across the middle, the floor's glow below with a tint of pearl.
CHROME = [(0, '#FFF7F9FD'), (0.16, '#FFD3DAE6'), (0.4, '#FF7C8597'), (0.49, '#FF1E222B'), (0.56, '#FF333A48'),
          (0.72, '#FFA9B3C6'), (0.85, '#FFE6D8F1'), (0.93, '#FFC9E6F6'), (1, '#FF8E95A8')]
CHROME_DIM = [(0, '#FFB9C1CF'), (0.3, '#FF6A7283'), (0.5, '#FF1A1D25'), (0.62, '#FF2C323E'), (0.82, '#FF8790A3'),
              (1, '#FF4B5161')]


def blob_points(cx, cy, r, rnd, wobble=0.12, n=10, squash=1.0):
    pts = []
    phase = rnd.uniform(0, math.tau)
    for k in range(n):
        a = math.tau * k / n
        rr = r * (1 + wobble * math.sin(2 * a + phase) + wobble * 0.5 * math.sin(3 * a + phase * 1.7))
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr * squash))
    return pts


def chrome_blob(s, cx, cy, r, rnd, motion, squash=1.0):
    """A mirrored blob: chrome, a window of light near its top, a rim of light below and a faint glow behind."""
    with s.group(motion=motion):
        s.glow(cx, cy + r * 0.2, r * 1.9, r * 1.6, '#9DB4FF', 0.1)
        pts = blob_points(cx, cy, r, rnd, squash=squash)
        s.linear(blob(pts), CHROME)
        # The softbox reflected near the top left, and a thin bright edge at the bottom.
        s.radial(ellipse(cx - r * 0.3, cy - r * 0.5 * squash, r * 0.42, r * 0.2), [(0, '#E6FFFFFF'), (0.6, '#66FFFFFF'), (1, '#00FFFFFF')])
        s.solid(ellipse(cx + r * 0.38, cy - r * 0.28 * squash, r * 0.07, r * 0.05), '#FFFFFFFF', 0.9)
        rim = [(cx + math.cos(a) * r * 0.92, cy + math.sin(a) * r * 0.92 * squash) for a in [math.pi * (0.15 + 0.7 * k / 12) for k in range(13)]]
        s.stroke(line(*rim), '#FFFFE8FA', 2.2, 0.55)


def ribbon(s, rnd):
    """A ribbon of chrome flowing across the sky: a band that twists, thin where it turns, in sharp bands of reflected light."""
    spine, widths = [], []
    for k in range(73):
        t = k / 72
        x = lerp(-80, 2480, t)
        y = 250 + 90 * math.sin(t * math.tau * 1.15 + 0.6) + 40 * math.sin(t * math.tau * 2.3)
        spine.append((x, y))
        widths.append(5 + 36 * abs(math.sin(t * math.tau * 1.6 + 0.4)))
    s.glow(1200, 260, 1300, 170, '#B6C6FF', 0.06)
    # Across the band, top to bottom: the sky's light, a cool grey, the dark horizon, a pearl glow and the floor's light.
    bands = ((-1, -0.55, '#FFEFF3F9'), (-0.55, -0.12, '#FF9FA8BA'), (-0.12, 0.2, '#FF1C2029'), (0.2, 0.45, '#FF4A4560'),
             (0.45, 0.78, '#FFE4D4EE'), (0.78, 1, '#FFA9C3D9'))
    for a, b, colour in bands:
        top = [(x, y + w * a) for (x, y), w in zip(spine, widths)]
        bottom = [(x, y + w * b) for (x, y), w in zip(spine, widths)]
        s.solid(band(top, bottom), colour, 0.95)
    s.stroke(line(*[(x, y - w) for (x, y), w in zip(spine, widths)]), '#FFFFFFFF', 1.4, 0.6)


def sky():
    rnd = random.Random(61)
    s = Scene()
    # Studio lights: soft, wide and cool, one warm.
    s.glow(500, 120, 900, 260, '#DCE6FF', 0.1)
    s.glow(1900, 80, 700, 200, '#FFE1F4', 0.07)
    with s.group(motion='sway p=120 dy=14 dx=30'):
        ribbon(s, rnd)
    chrome_blob(s, 380, 470, 92, rnd, 'orbit p=90 rx=36 ry=22')
    chrome_blob(s, 1960, 330, 150, rnd, 'orbit p=150 ph=0.4 rx=-44 ry=26', squash=0.94)
    chrome_blob(s, 2230, 640, 58, rnd, 'orbit p=75 ph=0.2 rx=26 ry=-30')
    chrome_blob(s, 1060, 560, 44, rnd, 'orbit p=60 ph=0.7 rx=-30 ry=18')
    chrome_blob(s, 1500, 120, 32, rnd, 'orbit p=50 ph=0.5 rx=22 ry=14')
    return viewbox(s, 2400, 900, 'top')


def swell(base, period, amps, phases, x0, x1):
    """A swell's crest from x0 to x1, repeating every period."""
    pts = []
    steps = int((x1 - x0) / (period / 30)) + 1
    for k in range(steps + 1):
        x = lerp(x0, x1, k / steps)
        y = base + sum(a * math.sin(math.tau * (n + 1) * x / period + p) for n, (a, p) in enumerate(zip(amps, phases)))
        pts.append((x, y))
    return pts


# A swell's face below its crest, in steps that follow the crest: white at the crest, the sky's grey, the dark horizon, then a pearl glow lower down.
FACE = ((0, 2.5, '#FFFFFFFF'), (2.5, 6, '#FFE3E8F0'), (6, 11, '#FFBAC2D0'), (11, 17, '#FF8790A3'), (17, 24, '#FF565E70'),
        (24, 32, '#FF30353F'), (32, 44, '#FF1A1D24'), (44, 58, '#FF121419'), (58, 74, '#FF23222D'), (74, 92, '#FF383650'),
        (92, 110, '#FF22212D'), (110, 600, '#FF0B0C10'))


def sea(s, rnd):
    """Liquid metal, in swells from the far ones, dim and slow, to the near ones, bright and rolling."""
    s.glow(1200, 150, 1500, 130, '#E8EEFF', 0.12)
    layers = (
        # base, repeats every, heights, seconds per repeat, rise and fall, crest light
        (170, 600, (10, 4, 2), 40, 3, 0.55),
        (232, 800, (16, 6, 3), 52, 4, 0.7),
        (305, 1000, (24, 9, 4), 64, 6, 0.85),
        (398, 1200, (32, 12, 5), 80, 8, 1.0),
    )
    for k, (base, period, amps, seconds, bob, light) in enumerate(layers):
        phases = [rnd.uniform(0, math.tau) for _ in amps]
        crest = swell(base, period, amps, phases, -20, W + period + 20)
        with s.group(motion=f'scroll p={seconds} dx={-period}; sway p={seconds / 2:g} ph={k / 4:.2f} dy={bob}', opacity=0.7 + 0.3 * light):
            with coarse():
                for a, b, colour in FACE:
                    upper = [(x, min(y + a, H + 40)) for x, y in crest]
                    lower = [(x, min(y + b, H + 40)) for x, y in reversed(crest)]
                    s.solid(poly(upper + lower), colour)
            s.stroke(line(*crest), '#FFFFFFFF', 1.4, 0.4 + 0.5 * light)
            glints = [ellipse(x, y + 2.5, 26, 1.3) for x, y in crest[2:-1:4]]
            s.solid(glints, '#FFFFFFFF', 0.3 * light)


def land():
    rnd = random.Random(67)
    s = Scene()
    sea(s, rnd)
    # A mirrored sphere floating over the sea, bobbing, and its reflection below it doing the same the other way.
    for cx, cy, r, period in ((620, 120, 46, 14), (1780, 96, 30, 11)):
        chrome_blob(s, cx, cy, r, rnd, f'sway p={period} dy=10', squash=1.0)
    return viewbox(s, W, H, 'bottom')


def chrome():
    return sky() + land()
