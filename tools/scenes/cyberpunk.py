"""Cyberpunk: a neon megacity in the rain (see build_scene_art.py, which writes it into SceneArt.xaml).

Smog lit magenta and cyan from below, megastructures fading into it, towers
with lit floors and windows, neon signs that flicker, a hologram that
glitches, searchlights sweeping the clouds, beacons, flying cars in their
lanes and an airship with a screen. Moving parts carry their motion as a Tag
(see Resonate.Themes/SceneMotion.cs).
"""
import math
import random

from sceneart import *  # noqa: F401,F403

W, H = 2400, 700

CYAN = '#00E5FF'
MAGENTA = '#FF2E97'
AMBER = '#FFB547'
VIOLET = '#9D4DFF'


def sky():
    rnd = random.Random(71)
    s = Scene()
    # Smog: heavy, lit from below in magenta and cyan, darker high up.
    s.haze(600, 640, 1100, 260, '#7A1F8F', 0.22)
    s.haze(1800, 700, 1000, 230, '#0C6E8F', 0.2)
    s.haze(1200, 420, 1400, 140, '#3B1360', 0.18)
    for k, (x, y, rx, ry, colour, peak) in enumerate(((400, 300, 520, 26, '#B03AB8', 0.12), (1500, 220, 640, 30, '#4A2F9C', 0.12),
                                                       (2100, 380, 460, 22, '#1B7FA6', 0.12), (900, 520, 600, 30, '#C2338F', 0.14))):
        s.haze(x, y, rx, ry, colour, peak, motion=f'sway p={160 + 40 * k} ph={k / 4:.2f} dx={40 + 10 * k}')

    # A moon, pale through the smog, with bands of it drifting across.
    s.glow(1700, 250, 260, 240, '#E9C6FF', 0.12)
    s.radial(circle(1700, 250, 96), [(0, '#FFF4E6FF'), (0.75, '#FFDCC3F2'), (1, '#FFB79AD8')], 0.42, origin=(0.42, 0.38))
    for k, (x, y, w, h) in enumerate(((1640, 226, 380, 16), (1780, 262, 300, 12), (1690, 300, 460, 20))):
        s.haze(x, y, w, h, '#2A0F3A', 0.55, motion=f'sway p={70 + 30 * k} ph={k / 3:.2f} dx={50 + 10 * k}')

    # Far aircraft: red and white lights blinking high in the smog.
    for k, (x, y) in enumerate(((520, 160), (1340, 90), (1980, 210), (880, 60))):
        s.glow(x, y, 6, 6, '#FF3355' if k % 2 else '#FFFFFF', 0.9, motion=f'blink p={2 + k % 2} ph={k * 0.3:.1f} w=0.2')
    return viewbox(s, 2400, 900, 'top')


def windows(rnd, x, w, top, base, kind, out):
    """A tower's lights: whole floors lit in bands, or scattered windows, by colour."""
    if kind == 'floors':
        y = top + 10
        while y < base - 6:
            if rnd.random() < 0.45:
                colour = rnd.choice(('cyan', 'cyan', 'white', 'amber'))
                x0 = x + 4 + rnd.uniform(0, w * 0.3)
                x1 = x + w - 4 - rnd.uniform(0, w * 0.3)
                if x1 - x0 > 6:
                    out[colour].append(poly([(x0, y), (x1, y), (x1, y + 1.8), (x0, y + 1.8)]))
            y += 7
    else:
        for wy in range(int(top + 8), int(base - 6), 8):
            for wx in range(int(x + 4), int(x + w - 5), 6):
                if rnd.random() < 0.12:
                    colour = rnd.choice(('cyan', 'white', 'amber', 'amber', 'magenta'))
                    out[colour].append(poly([(wx, wy), (wx + 3, wy), (wx + 3, wy + 3.4), (wx, wy + 3.4)]))


def tower(rnd, x, w, top, base, bodies, rims, lights, antennas, beacons, kind=None):
    """A tower with setbacks near its top, an edge lit along one side, and maybe a spire with a beacon."""
    steps = rnd.choice((0, 1, 2))
    insets = [0]
    for _ in range(steps):
        insets.append(insets[-1] + w * rnd.uniform(0.08, 0.15))
    # Up the left side, stepping in at each setback, across the top and down the right.
    left = [(x, base)]
    for k in range(steps + 1):
        y = top + 20 * (steps - k)
        left += [(x + insets[k], y + (20 if k else 0) if k else y), (x + insets[k], y)] if k else [(x, y)]
    right = [(x + w - px + x, py) for px, py in reversed(left)]
    bodies.append(poly(left + right))
    edge = top + 20 * steps
    rims.append(poly([(x + w - 2, edge), (x + w, edge), (x + w, base), (x + w - 2, base)]))
    windows(rnd, x, w, edge, base, kind or rnd.choice(('floors', 'scatter')), lights)
    if rnd.random() < 0.45:
        cx = x + w / 2 + rnd.uniform(-w * 0.1, w * 0.1)
        h = rnd.uniform(30, 80)
        antennas.append(poly([(cx - 1.2, top - h), (cx + 1.2, top - h), (cx + 2.4, top + 1), (cx - 2.4, top + 1)]))
        beacons.append((cx, top - h))


def city(s, rnd, layer, x0, x1, top_range, base, width_range, fill, light_alpha):
    """A row of towers, its lights by colour; returns where its tallest tops are, for signs and searchlights."""
    bodies, rims, antennas = [], [], []
    lights = {'cyan': [], 'white': [], 'amber': [], 'magenta': []}
    beacons, tops = [], []
    x = x0
    while x < x1:
        w = rnd.uniform(*width_range)
        top = rnd.uniform(*top_range)
        tower(rnd, x, w, top, base, bodies, rims, lights, antennas, beacons)
        tops.append((x, w, top))
        x += w + rnd.uniform(-6, 18)
    s.linear(bodies, fill)
    s.solid(antennas, fill[-1][1])
    s.solid(rims, CYAN if layer % 2 else MAGENTA, 0.35 * light_alpha)
    for colour, value in (('cyan', '#7CF6FF'), ('white', '#E6F4FF'), ('amber', '#FFC46B'), ('magenta', '#FF6FC2')):
        s.solid(lights[colour], value, light_alpha)
    for k, (bx, by) in enumerate(beacons[:6]):
        s.glow(bx, by, 7, 7, '#FF3355', 0.95, motion=f'blink p={2 + k % 3} ph={(k * 0.29) % 1:.2f} w=0.3')
    return tops


def sign(s, x, y, w, h, colour, glyphs, rnd, motion=None):
    """A vertical neon sign: a glowing frame and strokes like letters, in one colour."""
    with s.group(motion=motion):
        s.glow(x + w / 2, y + h / 2, w * 1.6, h * 0.75, colour, 0.35)
        s.solid(rect(x, y, w, h), '#E6050611')
        s.stroke(rect(x + 2, y + 2, w - 4, h - 4), colour, 1.6, 0.95)
        strokes = []
        cell = (h - 12) / glyphs
        for g in range(glyphs):
            gy = y + 6 + g * cell
            cx = x + w / 2
            r = min(w * 0.3, cell * 0.36)
            # Each "letter": two or three strokes in a little square.
            choice = rnd.randrange(4)
            if choice == 0:
                strokes.append(line((cx - r, gy + cell * 0.2), (cx + r, gy + cell * 0.2), (cx, gy + cell * 0.8)))
            elif choice == 1:
                strokes.append(line((cx - r, gy + cell * 0.25), (cx + r, gy + cell * 0.25)))
                strokes.append(line((cx, gy + cell * 0.1), (cx - r * 0.6, gy + cell * 0.85)))
            elif choice == 2:
                strokes.append(line((cx - r, gy + cell * 0.15), (cx - r, gy + cell * 0.8), (cx + r, gy + cell * 0.8)))
                strokes.append(line((cx + r * 0.2, gy + cell * 0.15), (cx + r * 0.2, gy + cell * 0.55)))
            else:
                strokes.append(line((cx - r, gy + cell * 0.5), (cx + r, gy + cell * 0.5)))
                strokes.append(line((cx, gy + cell * 0.15), (cx, gy + cell * 0.85)))
        s.stroke(strokes, '#FFFFFFFF', 2.4, 0.95)
        s.stroke(strokes, colour, 6, 0.45)


def billboard(s, x, y, w, h, rnd):
    """A hologram billboard: a gradient screen with scan lines and a ringed emblem, glitching now and then."""
    with s.group(motion='glitch p=13 ph=0.3 w=0.03 dx=7; flicker p=17 ph=0.6 w=0.015 lo=0.3'):
        s.glow(x + w / 2, y + h / 2, w * 0.95, h * 0.95, CYAN, 0.22)
        s.linear(rect(x, y, w, h), [(0, '#E60FA3C4'), (0.5, '#E64A2FB0'), (1, '#E6D12A8F')], start=(0, 0), end=(1, 1))
        lines = [poly([(x, yy), (x + w, yy), (x + w, yy + 1.2), (x, yy + 1.2)]) for yy in range(int(y + 3), int(y + h), 5)]
        s.solid(lines, '#FF000000', 0.35)
        cx, cy = x + w * 0.32, y + h / 2
        s.stroke([circle(cx, cy, h * 0.3), circle(cx, cy, h * 0.18)], '#FF9CFBFF', 2.2, 0.9)
        s.solid(circle(cx, cy, h * 0.07), '#FFFFFFFF', 0.9)
        bars = [rect(x + w * 0.56, y + h * (0.28 + 0.16 * k), w * (0.34 - 0.07 * k), h * 0.07) for k in range(4)]
        s.solid(bars, '#FFFFFFFF', 0.8)
        s.stroke(rect(x, y, w, h), '#FF9CFBFF', 1.6, 0.8)


def searchlight(s, x, y, length, angle, spread, period, phase, swing):
    """A beam from (x, y) up into the smog, sweeping to and fro."""
    a = math.radians(angle)
    tip = (x + math.sin(a) * length, y - math.cos(a) * length)
    left = (tip[0] - math.cos(a) * spread, tip[1] - math.sin(a) * spread)
    right = (tip[0] + math.cos(a) * spread, tip[1] + math.sin(a) * spread)
    s.linear(poly([(x - 3, y), left, right, (x + 3, y)]), [(0, argb('#BFF6FF', 0)), (0.45, argb('#BFF6FF', 0.07)), (1, argb('#E8FCFF', 0.34))],
             start=(0, 0), end=(0, 1), motion=f'swing p={period} ph={phase} a={swing} cx={x} cy={y}')


def car(s, x, y, direction, period, phase, colour):
    """A flying car: its lights and a trail, crossing the city in its lane."""
    dx = (W + 400) * direction
    start = -200 if direction > 0 else W + 200
    with s.group(motion=f'travel p={period} ph={phase:.2f} dx={dx} w=0.05'):
        tail = start - 150 * direction
        s.stroke_linear(line((tail, y), (start, y)), [(0, argb(colour, 0)), (1, argb(colour, 0.7))], 3,
                        start=(0, 0) if direction > 0 else (1, 0), end=(1, 0) if direction > 0 else (0, 0))
        s.solid(poly([(start - 18, y - 5), (start - 8, y - 9), (start + 10, y - 9), (start + 20, y - 4), (start + 22, y + 3),
                      (start - 22, y + 3)]), '#FF0D1124')
        s.solid(poly([(start - 18, y + 3), (start + 18, y + 3), (start + 18, y + 4.4), (start - 18, y + 4.4)]), CYAN, 0.8)
        s.glow(start + 20 * direction, y - 1, 16, 8, colour, 0.9)
        s.solid([ellipse(start + 20 * direction, y - 1, 3.2, 2)], '#FFFFFFFF', 0.95)


def airship(s, rnd):
    """An airship crossing slowly, its belly a screen."""
    x, y = -260, 150
    with s.group(motion='travel p=400 ph=0.35 dx=2900 w=0.06'):
        s.radial(ellipse(x, y, 150, 38), [(0, '#FF1E2338'), (0.7, '#FF12152A'), (1, '#FF0B0D1C')], origin=(0.5, 0.3))
        s.solid(poly([(x + 120, y - 6), (x + 170, y - 34), (x + 176, y - 30), (x + 150, y)]), '#FF12152A')
        s.solid(poly([(x + 120, y + 6), (x + 170, y + 30), (x + 176, y + 26), (x + 150, y)]), '#FF12152A')
        s.linear(rect(x - 80, y - 10, 150, 26), [(0, '#E6FF2E97'), (0.5, '#E69D4DFF'), (1, '#E600E5FF')], start=(0, 0), end=(1, 0))
        s.solid([rect(x - 80, yy, 150, 1) for yy in range(int(y - 8), int(y + 16), 4)], '#FF000000', 0.3)
        s.glow(x - 5, y + 3, 120, 40, MAGENTA, 0.25)
        s.solid([ellipse(x - 150, y, 3, 3), ellipse(x + 150, y - 2, 3, 3)], '#FFFF3355')


def land():
    rnd = random.Random(73)
    s = Scene()

    # Megastructures far off, nearly lost in the smog.
    far = [(-20, 700)]
    x = -20
    while x < W + 40:
        w = rnd.uniform(90, 220)
        top = rnd.uniform(60, 230)
        far += [(x, top), (x + w, top)]
        x += w
    far.append((W + 40, 700))
    s.linear(poly(far), [(0, '#FF1A1438'), (1, '#FF0E0B22')], 0.85)
    s.haze(1200, 330, 1400, 70, '#6A2C8F', 0.18)

    airship(s, rnd)
    # Searchlights behind the middle towers.
    for k, (x, angle, period, phase, swing) in enumerate(((820, -18, 23, 0.0, 14), (1490, 12, 29, 0.4, 16), (1980, -8, 19, 0.7, 12))):
        searchlight(s, x, 340, 330, angle, 64, period, phase, swing)

    city(s, rnd, 1, -20, W + 40, (150, 330), 720, (60, 130), [(0, '#FF1A1F3A'), (1, '#FF0C0F1F')], 0.55)
    s.haze(1200, 520, 1500, 90, '#8A1F7A', 0.2)
    # Cars in their lanes, some going each way.
    for k, (y, direction, period, phase, colour) in enumerate(((250, 1, 31, 0.0, '#FF3355'), (284, -1, 43, 0.4, '#E8F6FF'),
                                                               (352, 1, 27, 0.65, '#FF3355'), (388, -1, 37, 0.15, '#7CF6FF'),
                                                               (318, 1, 53, 0.85, '#FFB547'))):
        car(s, 0, y, direction, period, phase, colour)

    tops = city(s, rnd, 2, -40, W + 40, (300, 470), 720, (70, 160), [(0, '#FF10132A'), (1, '#FF06070F')], 0.85)

    # Neon signs down the nearer towers' sides, a few flickering.
    for k, (x, y, w, h, colour, glyphs, motion) in enumerate((
            (300, 440, 34, 150, MAGENTA, 4, 'flicker p=9 ph=0.1 w=0.03 lo=0.2'),
            (690, 470, 30, 120, CYAN, 3, None),
            (1260, 430, 36, 170, AMBER, 4, 'flicker p=14 ph=0.5 w=0.02 lo=0.25'),
            (1760, 450, 30, 140, MAGENTA, 4, None),
            (2120, 420, 34, 160, CYAN, 4, 'flicker p=11 ph=0.8 w=0.025 lo=0.2'))):
        sign(s, x, y, w, h, colour, glyphs, rnd, motion)
    billboard(s, 920, 400, 220, 110, rnd)

    # Mist at street level, lit by everything above it.
    s.linear(poly([(-20, 600), (W + 20, 600), (W + 20, 720), (-20, 720)]),
             [(0, argb('#B0207F', 0)), (0.6, argb('#B0207F', 0.16)), (1, argb('#3A0F4A', 0.4))])
    return viewbox(s, W, H, 'bottom')


def cyberpunk():
    return sky() + land()
