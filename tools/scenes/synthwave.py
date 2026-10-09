"""Synthwave: an outrun sunset (see build_scene_art.py, which writes it into SceneArt.xaml).

A night sky running down to a hot pink horizon: stars that twinkle and now
and then shoot, a striped sun with thin clouds across it, a city skyline in
front of it, neon wireframe mountains to either side, palms at the edges, and
a glowing grid on the ground that races towards you. Moving parts carry their
motion as a Tag (see Resonate.Themes/SceneMotion.cs).
"""
import math
import random

from sceneart import *  # noqa: F401,F403

W, H = 2400, 600
HORIZON = 230
SUN_X, SUN_R = 1200, 206

PINK = '#FF3EA5'
HOT = '#FF2E88'
CYAN = '#22E4FF'
VIOLET = '#8B3DFF'


def sky():
    rnd = random.Random(31)
    s = Scene()

    # A faint band of violet haze high up, and the pink rising from the horizon below.
    s.haze(700, 260, 900, 120, '#7B2BD6', 0.12)
    s.haze(1800, 160, 700, 90, '#C02BB8', 0.08)

    # Stars: most still, some in groups that twinkle out of step.
    still = {0.8: [], 0.5: [], 0.28: []}
    twinkling = [[] for _ in range(6)]
    for k in range(240):
        x, y = rnd.uniform(0, 2400), 820 * rnd.random() ** 1.35
        r = rnd.choice([0.8, 1.0, 1.2, 1.6, 2.0])
        if k % 5 == 0:
            twinkling[k % 6].append(dot(x, y, r * 1.2))
        else:
            still[rnd.choice(list(still))].append(dot(x, y, r))
    for alpha, figures in still.items():
        s.solid(figures, '#FFFFE9FB', alpha)
    for k, figures in enumerate(twinkling):
        s.solid(figures, '#FFFFF4FF', 0.9, motion=f'twinkle p={7 + 3 * k} ph={k / 6:.3f} lo=0.1')

    # A few bright stars with a cross of light.
    crosses = []
    for x, y, r in ((330, 120, 9), (1720, 70, 7), (2140, 300, 8), (960, 360, 6)):
        crosses.append(poly([(x - r, y), (x, y - 0.9), (x + r, y), (x, y + 0.9)]))
        crosses.append(poly([(x, y - r), (x + 0.9, y), (x, y + r), (x - 0.9, y)]))
    s.solid(crosses, '#FFFFF0FF', 0.9, motion='twinkle p=11 ph=0.2 lo=0.45')

    # Shooting stars: a streak brightest at its head, crossing in a second or so, now and then.
    for k, (x, y, dx, dy, period, phase) in enumerate(((600, 90, 520, 170, 37, 0.1), (1500, 40, 460, 190, 53, 0.55),
                                                         (2050, 140, -480, 150, 71, 0.8))):
        length = 150
        angle = math.atan2(dy, dx)
        tx, ty = x - math.cos(angle) * length, y - math.sin(angle) * length
        start = (0, 0) if dx > 0 else (1, 0)
        end = (1, 0) if dx > 0 else (0, 0)
        if abs(dy) > abs(dx) * 0.6:
            start, end = (start[0], 0), (end[0], 1)
        s.stroke_linear(line((tx, ty), (x, y)), [(0, argb('#FFD6F5', 0)), (0.85, argb('#FFD6F5', 0.7)), (1, '#FFFFFFFF')],
                        2.2, start=start, end=end, motion=f'shoot p={period} ph={phase} dx={dx} dy={dy} w=0.03')
    return viewbox(s, 2400, 900, 'top')


def frond(x, y, angle, length, droop, width):
    """A palm frond from (x, y): a curved blade, widest a third of the way, with its leaflets cut as teeth."""
    a = math.radians(angle)
    pts_top, pts_bottom = [], []
    n = 12
    for i in range(n + 1):
        t = i / n
        px = x + math.cos(a) * length * t
        py = y + math.sin(a) * length * t + droop * t * t * length
        # Along the blade, the normal turns with its droop.
        dxt = math.cos(a)
        dyt = math.sin(a) + 2 * droop * t
        norm = math.hypot(dxt, dyt)
        nx, ny = -dyt / norm, dxt / norm
        wdt = width * math.sin(math.pi * min(1, t * 1.3 + 0.05)) * (1 - t * 0.5)
        tooth = 0.55 if i % 2 else 1.0
        pts_top.append((px + nx * wdt * tooth, py + ny * wdt * tooth))
        pts_bottom.append((px - nx * wdt * 0.55 * tooth, py - ny * wdt * 0.55 * tooth))
    return poly(pts_top + list(reversed(pts_bottom)))


def palm(x, base, height, lean, rnd):
    """A palm: a tapering, curving trunk with rings, and a crown of drooping fronds; returns (trunk, rings, fronds)."""
    trunk_top = []
    trunk_bottom = []
    spine = []
    n = 16
    for i in range(n + 1):
        t = i / n
        px = x + lean * height * t * t
        py = base - height * t
        spine.append((px, py))
        w = lerp(15, 6, t)
        trunk_top.append((px - w, py))
        trunk_bottom.append((px + w, py))
    trunk = poly(trunk_top + list(reversed(trunk_bottom)))
    rings = []
    for i in range(2, n * 3):
        t = i / (n * 3)
        px = x + lean * height * t * t
        py = base - height * t
        w = lerp(15, 6, t)
        rings.append(poly([(px - w, py), (px + w, py - 1.6), (px + w, py), (px - w, py + 1.6)]))
    cx, cy = spine[-1]
    fronds = []
    for k, (angle, length, droop) in enumerate(((-170, 190, 0.55), (-145, 210, 0.45), (-118, 150, 0.35), (-92, 120, 0.6),
                                                 (-62, 160, 0.4), (-35, 215, 0.45), (-8, 195, 0.6), (160, 150, 0.9), (20, 150, 0.95))):
        fronds.append(frond(cx, cy, angle + rnd.uniform(-6, 6), length * height / 430, droop, 15 * height / 430))
    fronds.append(ellipse(cx, cy + 2, 14 * height / 430, 10 * height / 430))
    return trunk, rings, fronds


def ridge(rnd, x0, x1, peaks, base):
    """A mountain range's ridge from x0 to x1: the points of its tent-shaped peaks, with small crags."""
    pts = []
    x = x0
    while x <= x1:
        y = base
        for px, py, spread in peaks:
            y = min(y, py + abs(x - px) * spread)
        pts.append((x, y + rnd.uniform(-5, 5) if base - y > 12 else y))
        x += rnd.uniform(22, 46)
    pts.append((x1, base))
    pts[0] = (x0, base)
    return pts


def mountains(s, rnd, x0, x1, peaks, fill, edge, edge_alpha):
    """Neon wireframe mountains: a dark body, a glowing ridge and facets drawn down from each crag."""
    pts = ridge(rnd, x0, x1, peaks, HORIZON)
    s.linear(poly(pts + [(x1, HORIZON + 2), (x0, HORIZON + 2)]), fill)
    facets = []
    for k, (x, y) in enumerate(pts[1:-1], 1):
        if HORIZON - y < 14:
            continue
        # Each crag down to the foot, leaning away from the range's middle, and across to the next one.
        mid = (x0 + x1) / 2
        foot = x + (x - mid) * 0.18 + rnd.uniform(-20, 20)
        facets.append(line((x, y), (foot, HORIZON)))
        nx, ny = pts[k + 1]
        if HORIZON - ny > 14 and k % 2:
            facets.append(line((x, y), (lerp(x, nx, 0.5), lerp(y, HORIZON, 0.55)), (nx, ny)))
    s.stroke(facets, edge, 1.2, edge_alpha * 0.45)
    s.stroke(line(*pts), edge, 7, edge_alpha * 0.18)
    s.stroke(line(*pts), edge, 2, edge_alpha)


def sun(s):
    """The sun on the horizon: a gradient from gold to hot pink, cut by stripes that widen towards the bottom."""
    top = HORIZON - SUN_R
    slices = []
    y = top
    gap_from = HORIZON - SUN_R * 0.58
    stripe = 0
    while y < HORIZON:
        if y < gap_from:
            y2 = gap_from
        else:
            t = (y - gap_from) / (HORIZON - gap_from)
            height = lerp(18, 7, t)
            y2 = min(HORIZON, y + height)
        # The circle between y and y2, as a polygon.
        pts_l, pts_r = [], []
        steps = max(2, int((y2 - y) / 4))
        for i in range(steps + 1):
            yy = lerp(y, y2, i / steps)
            half = math.sqrt(max(0.0, SUN_R ** 2 - (yy - HORIZON) ** 2))
            pts_l.append((SUN_X - half, yy))
            pts_r.append((SUN_X + half, yy))
        slices.append(poly(pts_r + list(reversed(pts_l))))
        if y2 >= HORIZON:
            break
        t = (y2 - gap_from) / (HORIZON - gap_from)
        y = y2 + lerp(3, 12, t)
        stripe += 1
    # The halo breathes, slowly.
    s.glow(SUN_X, HORIZON - 40, SUN_R * 2.3, HORIZON - 42, '#FF4F9A', 0.32,
           motion=f'pulse p=12 s=0.04 cx={SUN_X} cy={HORIZON - 40}; twinkle p=12 lo=0.75')
    s.glow(SUN_X, HORIZON - SUN_R * 0.55, SUN_R * 1.25, HORIZON - SUN_R * 0.55 - 2, '#FFB347', 0.22)
    s.linear(slices, [(0, '#FFFFE66B'), (0.32, '#FFFFB547'), (0.62, '#FFFF5C7A'), (1, '#FFE0217F')], start=(0, 0), end=(0, 1))


def skyline(s, rnd):
    """A city in front of the sun's lower half: dark towers with a few lit windows and a beacon or two."""
    towers, windows, rims = [], [], []
    x = SUN_X - 330
    beacons = []
    while x < SUN_X + 330:
        w = rnd.uniform(18, 44)
        d = abs(x + w / 2 - SUN_X) / 330
        h = rnd.uniform(26, 92) * (1.1 - d * 0.7)
        top = HORIZON - h
        if rnd.random() < 0.2:
            # A spire.
            towers.append(poly([(x + w / 2 - 1.2, top - h * 0.35), (x + w / 2 + 1.2, top - h * 0.35), (x + w / 2 + 1.2, top),
                                (x + w / 2 - 1.2, top)]))
            beacons.append((x + w / 2, top - h * 0.35))
        towers.append(poly([(x, top), (x + w, top), (x + w, HORIZON + 1), (x, HORIZON + 1)]))
        rims.append(poly([(x, top), (x + w, top), (x + w, top + 1.4), (x, top + 1.4)]))
        for wy in range(int(top + 6), int(HORIZON - 4), 7):
            for wx in range(int(x + 4), int(x + w - 4), 6):
                if rnd.random() < 0.13:
                    windows.append(poly([(wx, wy), (wx + 2.6, wy), (wx + 2.6, wy + 3), (wx, wy + 3)]))
        x += w + rnd.uniform(0, 6)
    s.solid(towers, '#FF14052A')
    s.solid(rims, '#FFFF6FB5', 0.55)
    s.solid(windows, '#FF7CF3FF', 0.75)
    for k, (bx, by) in enumerate(beacons):
        s.glow(bx, by, 9, 9, '#FF3B5C', 0.9, motion=f'blink p={3 + k % 3} ph={k * 0.37 % 1:.2f} w=0.35')


def grid(s):
    """The glowing grid: lines running to the vanishing point, and lines across that come towards you."""
    # The ground, from a faint purple at the horizon to near black.
    s.linear(poly([(-10, HORIZON), (W + 10, HORIZON), (W + 10, H + 40), (-10, H + 40)]),
             [(0, '#FF2A0A44'), (0.25, '#FF16052C'), (1, '#FF07020F')])
    s.glow(SUN_X, HORIZON + 6, 1100, 90, '#FF3EA5', 0.32)

    # Lines towards the vanishing point, fading out into the distance.
    lines = []
    for k in range(-26, 27):
        x = SUN_X + k * 150
        lines.append(line((SUN_X + k * 150 * 0.03, HORIZON + 1), (x, H + 40)))
    s.stroke_linear(lines, [(0, argb(PINK, 0)), (0.15, argb(PINK, 0.25)), (0.6, argb(PINK, 0.75)), (1, argb(PINK, 0.95))],
                    2.4, start=(0, 0), end=(0, 1))
    s.stroke_linear(lines, [(0, argb(PINK, 0)), (0.3, argb(PINK, 0.06)), (1, argb(PINK, 0.22))], 10, start=(0, 0), end=(0, 1))

    # Lines across, equally far apart on the ground, each coming towards you and starting again at the horizon.
    count, far, period = 14, 12, 18
    for i in range(count):
        phase = i / count
        y0 = HORIZON + (H - HORIZON) / (1 + (far - 1) * (1 - phase))
        with s.group(motion=f'approach p={period} ph={phase:.4f} top={HORIZON} bottom={H} y0={y0:.2f} far={far} fade=150'):
            s.solid(poly([(-10, y0 - 5), (W + 10, y0 - 5), (W + 10, y0 + 5), (-10, y0 + 5)]), argb(PINK, 0.14))
            s.solid(poly([(-10, y0 - 1.3), (W + 10, y0 - 1.3), (W + 10, y0 + 1.3), (-10, y0 + 1.3)]), argb('#FF7CC6', 0.95))

    # The horizon itself: a hot line, white where the sun meets it.
    s.linear(poly([(-10, HORIZON - 1.6), (W + 10, HORIZON - 1.6), (W + 10, HORIZON + 1.6), (-10, HORIZON + 1.6)]),
             [(0, argb(PINK, 0.2)), (0.35, argb(PINK, 0.9)), (0.5, '#FFFFE3F3'), (0.65, argb(PINK, 0.9)), (1, argb(PINK, 0.2))],
             start=(0, 0), end=(1, 0))


def land():
    rnd = random.Random(23)
    s = Scene()

    # The dusk glow over the horizon.
    s.glow(SUN_X, HORIZON, 1500, 228, '#E0287F', 0.34)
    s.glow(SUN_X, HORIZON, 700, 160, '#FF6A3D', 0.16)
    sun(s)

    # Thin dark clouds drifting across the sun.
    for k, (x, y, w, h) in enumerate(((SUN_X - 260, HORIZON - 150, 300, 7), (SUN_X + 40, HORIZON - 118, 360, 6),
                                      (SUN_X - 380, HORIZON - 92, 280, 5), (SUN_X + 150, HORIZON - 176, 230, 5))):
        figure = shape([(x, y), (x + w * 0.2, y - h), (x + w * 0.8, y - h * 1.1), (x + w, y), (x + w * 0.7, y + h * 0.6),
                        (x + w * 0.3, y + h * 0.5)], True)
        s.solid(figure, '#FF3A0C4A', 0.9, motion=f'sway p={90 + 20 * k} ph={k / 4:.2f} dx={26 + 6 * k}')

    mountains(s, rnd, -20, 1010, [(150, 70, 0.62), (420, 118, 0.7), (720, 150, 0.55), (930, 200, 0.6)],
              [(0, '#FF2D0C55'), (1, '#FF16062E')], CYAN, 0.9)
    mountains(s, rnd, 1390, 2420, [(1520, 196, 0.6), (1740, 120, 0.68), (2030, 64, 0.6), (2290, 110, 0.7)],
              [(0, '#FF2D0C55'), (1, '#FF16062E')], CYAN, 0.9)
    skyline(s, rnd)
    grid(s)

    # Palms at the edges, black against the glow, lit pink along one side.
    trunks, rings, fronds = [], [], []
    for x, height, lean in ((120, 470, 0.16), (290, 360, -0.1), (2240, 440, -0.18), (2370, 330, 0.08)):
        t, r, f = palm(x, H + 30, height, lean, rnd)
        trunks.append(t)
        rings += r
        fronds += f
    s.solid(trunks, '#FF0C0218')
    s.solid(rings, '#FFFF4FA6', 0.35)
    s.solid(fronds, '#FF0C0218')
    return viewbox(s, W, H, 'bottom')


def synthwave():
    return sky() + land()
