"""Afterhours: the city after midnight through a rainy window (see build_scene_art.py, which writes it into SceneArt.xaml).

Low clouds lit by the city, a plane blinking across, a skyline in layers with
warm windows, a crown lit red and beacons, an elevated highway with
streetlights and two streams of traffic, a river full of reflections, a
neon bar sign, and the lights nearest the glass out of focus. Moving parts
carry their motion as a Tag (see Resonate.Themes/SceneMotion.cs).
"""
import math
import random

from sceneart import *  # noqa: F401,F403

W, H = 2400, 620
ROAD = 440
WATER = 520

AMBER = '#FFB35C'
RED = '#FF4757'
TEAL = '#4FD1C5'


def sky():
    rnd = random.Random(81)
    s = Scene()
    # Clouds hanging low, lit warm from below by the city; a darker band over them.
    for k, (x, y, rx, ry, colour, peak) in enumerate(((500, 700, 900, 120, '#7A2A2A', 0.3), (1700, 760, 1000, 130, '#8A3A22', 0.28),
                                                       (1100, 600, 800, 60, '#4A1A28', 0.25), (300, 520, 500, 40, '#3A1420', 0.2),
                                                       (2000, 560, 600, 50, '#40162A', 0.2))):
        s.haze(x, y, rx, ry, colour, peak, motion=f'sway p={180 + 60 * k} ph={k / 5:.2f} dx={50 + 15 * k}')
    # Long banks of cloud, their undersides lit by the city.
    for k, (x0, x1, y, h) in enumerate(((-100, 900, 610, 26), (1300, 2500, 560, 30), (500, 1700, 470, 18), (1600, 2300, 380, 14))):
        top = [(lerp(x0, x1, t / 16), y - h * math.sin(math.pi * t / 16) ** 0.7 * rnd.uniform(0.6, 1.3)) for t in range(17)]
        bottom = [(lerp(x0, x1, t / 16), y + h * 0.25 * math.sin(math.pi * t / 16) + rnd.uniform(-2, 2) * math.sin(math.pi * t / 16))
                  for t in range(17)]
        with s.group(motion=f'sway p={240 + 60 * k} ph={k / 4:.2f} dx={60 + 20 * k}'):
            s.linear(band(top, bottom), [(0, '#FF1A0D16'), (0.7, '#FF3A1820'), (1, '#FF7A3A2A')], 0.85)

    # A few stars where the clouds break.
    stars = [dot(rnd.uniform(0, 2400), 360 * rnd.random() ** 1.4, rnd.choice([0.8, 1.0, 1.3])) for _ in range(70)]
    s.solid(stars, '#FFFFF0E8', 0.35)
    # A plane going over, its lights blinking.
    with s.group(motion='travel p=150 ph=0.2 dx=2800 dy=-120 w=0.05'):
        s.solid(ellipse(-200, 300, 2, 2), '#FFFFFFFF', 0.9)
        s.glow(-206, 300, 7, 7, RED, 0.9, motion='blink p=1.5 w=0.3')
        s.glow(-194, 300, 7, 7, '#7CFFB0', 0.8, motion='blink p=1.5 ph=0.5 w=0.3')
    return viewbox(s, 2400, 900, 'top')


def skyline(s, rnd, base, tops, widths, fill, window, density, lit_alpha):
    """A layer of buildings with lit windows; returns the buildings as (x, width, top)."""
    bodies, lights, built = [], [], []
    x = -30
    while x < W + 30:
        w = rnd.uniform(*widths)
        top = rnd.uniform(*tops)
        bodies.append(poly([(x, top), (x + w, top), (x + w, base), (x, base)]))
        built.append((x, w, top))
        for wy in range(int(top + 7), int(base - 4), 7):
            row = rnd.random() < 0.5
            for wx in range(int(x + 4), int(x + w - 4), 5):
                if rnd.random() < density * (1.6 if row else 0.5):
                    lights.append(poly([(wx, wy), (wx + 2.6, wy), (wx + 2.6, wy + 3.2), (wx, wy + 3.2)]))
        x += w + rnd.uniform(-4, 10)
    s.linear(bodies, fill)
    s.solid(lights, window, lit_alpha)
    return built


def crown(s, x, w, top):
    """A tower's crown lit up: a stepped top floodlit red and amber, a spire with a beacon."""
    steps = []
    for k in range(4):
        inset = w * 0.12 * (k + 1)
        steps.append(poly([(x + inset, top - 18 * k), (x + w - inset, top - 18 * k), (x + w - inset, top - 18 * (k + 1) + 2),
                           (x + inset, top - 18 * (k + 1) + 2)]))
    s.glow(x + w / 2, top - 30, w * 1.2, 90, RED, 0.3)
    s.linear(steps, [(0, '#FFFFB36B'), (1, '#FFE0303F')], 0.85)
    cx = x + w / 2
    s.solid(poly([(cx - 1.5, top - 150), (cx + 1.5, top - 150), (cx + 3, top - 70), (cx - 3, top - 70)]), '#FF2A1418')
    s.glow(cx, top - 152, 9, 9, RED, 1, motion='blink p=2 w=0.35')


def highway(s, rnd):
    """An elevated highway on pillars, streetlights along it, red tail lights going one way and headlights the other."""
    deck = poly([(-20, ROAD), (W + 20, ROAD), (W + 20, ROAD + 16), (-20, ROAD + 16)])
    pillars = [poly([(x - 9, ROAD + 16), (x + 9, ROAD + 16), (x + 12, WATER + 8), (x - 12, WATER + 8)]) for x in range(80, W, 260)]
    s.solid(pillars, '#FF0E080C')
    s.solid(deck, '#FF120A0E')
    s.solid(poly([(-20, ROAD - 1), (W + 20, ROAD - 1), (W + 20, ROAD + 1), (-20, ROAD + 1)]), AMBER, 0.45)
    # Streetlights: a post, a lamp and its pool of light.
    posts, lamps = [], []
    for x in range(40, W, 130):
        posts.append(poly([(x - 1, ROAD - 34), (x + 1, ROAD - 34), (x + 1, ROAD), (x - 1, ROAD)]))
        posts.append(poly([(x, ROAD - 34), (x + 12, ROAD - 36), (x + 12, ROAD - 34), (x, ROAD - 32)]))
        lamps.append((x + 12, ROAD - 34))
    s.solid(posts, '#FF1A1014')
    for k, (x, y) in enumerate(lamps):
        s.glow(x, y + 16, 34, 30, AMBER, 0.22)
    s.solid([ellipse(x, y + 1, 4, 1.8) for x, y in lamps], '#FFFFE2B0')

    # Traffic as trails of light, in a pattern that repeats every period, so it can run round for ever.
    for lane, (y, colour, direction, period, seconds) in enumerate(((ROAD - 4, RED, 1, 900, 40), (ROAD + 6, '#FFFFF0DC', -1, 1100, 34))):
        starts = []
        x = rnd.uniform(0, 60)
        while x < period - 80:
            starts.append((x, rnd.uniform(26, 70)))
            x += rnd.uniform(60, 190)
        trails, halos = [], []
        for copy in range(-1, int(W / period) + 2):
            for sx, length in starts:
                px = sx + copy * period
                trails.append(poly([(px, y - 1.2), (px + length, y - 0.8), (px + length, y + 0.8), (px, y + 1.2)]))
                halos.append(ellipse(px + length / 2, y, length * 0.7, 5))
        with s.group(motion=f'scroll p={seconds} dx={direction * period}'):
            s.solid(halos, colour, 0.14)
            s.solid(trails, colour, 0.9)


def bridge(s):
    """A suspension bridge carrying the highway: two lit towers, the main cables strung with lights, and hangers."""
    towers = []
    cables, hangers, lights = [], [], []
    for x in (640, 1760):
        towers.append(poly([(x - 14, WATER + 6), (x - 9, 190), (x + 9, 190), (x + 14, WATER + 6)]))
        towers.append(poly([(x - 16, 236), (x + 16, 236), (x + 16, 246), (x - 16, 246)]))
        s.glow(x, 190, 30, 30, RED, 0.9, motion=f'blink p=3 ph={0.5 if x > 1000 else 0} w=0.4')
    # The cables sag between the towers and run down to the banks beyond them.
    spans = [(-40, ROAD - 4, 640, 194), (640, 194, 1760, 194), (1760, 194, 2440, ROAD - 4)]
    for x0, y0, x1, y1 in spans:
        pts = []
        for k in range(41):
            t = k / 40
            sag = 150 * 4 * t * (1 - t) if (x0, x1) == (640, 1760) else 30 * 4 * t * (1 - t)
            pts.append((lerp(x0, x1, t), lerp(y0, y1, t) + sag))
        cables.append(line(*pts))
        for k in range(1, 40, 2):
            x, y = pts[k]
            if y < ROAD - 6:
                hangers.append(line((x, y), (x, ROAD - 2)))
            lights.append(ellipse(x, y, 2.4, 2.4))
    s.solid(towers, '#FF1A0E14')
    for x in (640, 1760):
        s.linear(poly([(x + 4, WATER + 6), (x + 3, 192), (x + 9, 192), (x + 14, WATER + 6)]), [(0, argb(AMBER, 0.55)), (1, argb(AMBER, 0.1))])
        s.glow(x, 200, 26, 60, AMBER, 0.25)
    s.stroke(hangers, '#FF5A3A3A', 0.8, 0.6)
    s.stroke(cables, '#FF6A4646', 2, 0.9)
    s.solid(lights, '#FFFFD9A0', 0.95, motion='twinkle p=6 lo=0.7')


def water(s, rnd, buildings):
    """The river: dark water with the city's lights drawn down it in broken streaks that shimmer."""
    s.linear(poly([(-20, WATER), (W + 20, WATER), (W + 20, H + 20), (-20, H + 20)]), [(0, '#FF1E0E15'), (1, '#FF07040A')])
    near = {AMBER: [], RED: [], '#FFFFF0DC': []}
    far = {AMBER: [], RED: [], '#FFFFF0DC': []}
    columns = [(x + rnd.uniform(0, w), rnd.choice((AMBER, AMBER, '#FFFFF0DC', RED))) for x, w, top in buildings for _ in range(max(1, int(w / 22)))]
    columns += [(x + 12, AMBER) for x in range(40, W, 130)]
    for x, colour in columns:
        y = WATER + 3
        while y < H + 10:
            width = rnd.uniform(2, 9) * (1 - (y - WATER) / (H - WATER) * 0.5)
            if rnd.random() < 0.7:
                group = near if y < WATER + 40 else far
                group[colour].append(poly([(x - width, y), (x + width, y), (x + width * 0.7, y + 1.6), (x - width * 0.7, y + 1.6)]))
            y += rnd.uniform(3, 7)
    for k, (colour, figures) in enumerate(near.items()):
        s.solid(figures, colour, 0.55, motion=f'sway p={3 + k} ph={k / 3:.2f} dx=2.5; twinkle p={5 + k * 2} ph={k / 3:.2f} lo=0.6')
    for k, (colour, figures) in enumerate(far.items()):
        s.solid(figures, colour, 0.28, motion=f'sway p={4 + k} ph={0.5 + k / 3:.2f} dx=3.5; twinkle p={6 + k * 2} ph={k / 4:.2f} lo=0.5')
    s.glow(1200, WATER, 1300, 40, AMBER, 0.12)


def bar_sign(s, x, y):
    """A neon cocktail glass on a wall, flickering now and then."""
    with s.group(motion='flicker p=19 ph=0.4 w=0.02 lo=0.15'):
        s.glow(x, y, 70, 70, '#FF4FA3', 0.35)
        glass = [line((x - 26, y - 26), (x + 26, y - 26), (x, y + 2), (x - 26, y - 26)), line((x, y + 2), (x, y + 30)),
                 line((x - 14, y + 30), (x + 14, y + 30))]
        s.stroke(glass, '#FF4FA3', 7, 0.4)
        s.stroke(glass, '#FFFFD6EC', 2.2)
        s.stroke(line((x + 6, y - 20), (x + 20, y - 40)), '#FF4FD1C5', 2.2)
        s.solid(circle(x - 6, y - 16, 4), '#FF7CFFB0')


def bokeh(s, rnd):
    """The lights nearest the glass, out of focus: soft discs with a slightly brighter rim, breathing slowly."""
    for k in range(16):
        x = rnd.uniform(0, W)
        y = rnd.uniform(140, H - 20)
        r = rnd.uniform(30, 90)
        colour = rnd.choice((AMBER, AMBER, RED, '#FF8A5C', TEAL, '#FFE9C9'))
        stops = [(0, argb(colour, 0.1)), (0.8, argb(colour, 0.13)), (0.93, argb(colour, 0.2)), (1, argb(colour, 0))]
        s.radial(circle(x, y, r), stops, motion=f'twinkle p={9 + k % 7} ph={k / 16:.3f} lo=0.4; orbit p={40 + 7 * k} ph={k / 16:.3f} rx={4 + k % 5} ry={3 + k % 3}')


def land():
    rnd = random.Random(83)
    s = Scene()
    s.glow(1200, ROAD - 40, 1500, 260, '#B4472B', 0.18)
    skyline(s, rnd, ROAD, (70, 260), (60, 150), [(0, '#FF2E1D33'), (1, '#FF1C1124')], '#FFE9B08A', 0.07, 0.5)
    built = skyline(s, rnd, ROAD + 8, (150, 340), (56, 130), [(0, '#FF1E1019'), (1, '#FF120A10')], '#FFFFC98A', 0.1, 0.8)
    tallest = min(built[3:-3], key=lambda b: b[2])
    crown(s, *tallest)
    for k, (x, w, top) in enumerate(sorted(built, key=lambda b: b[2])[:5]):
        if (x, w, top) != tallest:
            s.glow(x + w / 2, top - 4, 7, 7, RED, 0.9, motion=f'blink p={3 + k % 2} ph={k * 0.23:.2f} w=0.3')
    highway(s, rnd)
    bridge(s)
    water(s, rnd, built)
    bar_sign(s, 2160, 360)
    bokeh(s, rnd)
    return viewbox(s, W, H, 'bottom')


def afterhours():
    return sky() + land()
