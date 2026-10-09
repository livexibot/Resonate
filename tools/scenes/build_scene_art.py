"""Writes src/Resonate.App/Controls/SceneArt.xaml: the special looks' scenery as XAML paths.

Run it from the repository's root: python3 tools/scenes/build_scene_art.py

Each scene is a sky anchored to the top of the window and a landscape anchored
to the bottom and scaled to the window's width, both drawn as flat shapes with
soft gradients: farther layers lighter and hazier, nearer ones darker, light
from the moon at the top right. Shapes of one colour are merged into one Path,
so a scene is about a hundred elements. Randomness is seeded, so running the
script again writes the same file.
"""
import math
import random

OUT = 'src/Resonate.App/Controls/SceneArt.xaml'

from sceneart import *  # noqa: F401,F403
from sceneart import _STEP, _area, _op, _q, _stops, _t  # noqa: F401


# ---------------------------------------------------------------- Mountains

class Range:
    """A mountain range: a ridge line over [0, width] from tent-shaped peaks plus crags.

    peaks are (x, y, left slope, right slope); the light comes from the right,
    so each peak's left face is in shadow.
    """

    def __init__(self, rnd, peaks, width, base, step=6, crag=((180, 16), (60, 7), (18, 2.6))):
        self.rnd = rnd
        self.base = base
        self.step = step
        self.xs = [i * step for i in range(int(width / step) + 1)]
        bumps = noise(rnd, width, step, crag)
        self.ys = []
        for i, x in enumerate(self.xs):
            y = base
            for px, py, sl, sr in peaks:
                y = min(y, py + (px - x) * sl if x < px else py + (x - px) * sr)
            # Crags fade out near the summits' tips so peaks stay sharp.
            self.ys.append(min(base, y + bumps[i]))
        self.peaks = []
        for px, py, _, _ in peaks:
            i0 = max(0, int(px / step) - 4)
            i1 = max(i0, min(len(self.xs) - 1, int(px / step) + 4))
            top = min(range(i0, i1 + 1), key=lambda i: self.ys[i])
            self.peaks.append(top)

    def y(self, x):
        i = max(0, min(len(self.xs) - 2, int(x / self.step)))
        t = (x - self.xs[i]) / self.step
        return lerp(self.ys[i], self.ys[i + 1], t)

    def ridge(self, i0=0, i1=None):
        i1 = len(self.xs) - 1 if i1 is None else i1
        return [(self.xs[i], self.ys[i]) for i in range(i0, i1 + 1)]

    @rounded
    def body(self):
        pts = self.ridge()
        return poly([(pts[0][0], self.base)] + pts + [(pts[-1][0], self.base)])

    def valleys(self):
        """For each peak, the lowest ridge point between it and its neighbours."""
        out = []
        order = sorted(self.peaks)
        for k, top in enumerate(order):
            left = order[k - 1] if k else 0
            right = order[k + 1] if k + 1 < len(order) else len(self.xs) - 1
            lv = max(range(left, top + 1), key=lambda i: self.ys[i]) if top > left else top
            rv = max(range(top, right + 1), key=lambda i: self.ys[i]) if right > top else top
            out.append((lv, top, rv))
        return out

    @rounded
    def shadows(self, depth, spurs=2):
        """The face of each peak away from the moon: a facet from the ridge down to a jagged spine, and spur shadows."""
        rnd = self.rnd
        figures = []
        for lv, top, rv in self.valleys():
            sx, sy = self.xs[top], self.ys[top]
            vx, vy = self.xs[lv], self.ys[lv]
            bottom = min(self.base, sy + depth)
            foot = (sx + rnd.uniform(0.05, 0.3) * (bottom - sy) * 0.5, bottom)
            spine = []
            for k in range(1, 8):
                t = k / 8
                spine.append((lerp(sx, foot[0], t ** 0.8) + rnd.uniform(-1, 1) * 4 * t, lerp(sy, bottom, t)))
            spine.append(foot)
            # Back up to the valley along the gully between this peak and the last.
            gully = []
            for k in range(1, 4):
                t = k / 4
                gully.append((lerp(foot[0], vx, t) + rnd.uniform(-5, 5), lerp(bottom, vy, t ** 0.9) + rnd.uniform(-3, 3)))
            figures.append(poly(self.ridge(lv, top) + spine + gully))
            # Spurs on the lit face: narrow shadowed wedges below the ridge.
            span = rv - top
            for _ in range(spurs if span >= 8 else 0):
                i = top + int(span * rnd.uniform(0.2, 0.7))
                ax, ay = self.xs[i], self.ys[i]
                length = rnd.uniform(0.3, 0.65) * (bottom - ay)
                if length < 12:
                    continue
                w = rnd.uniform(5, 12)
                wedge = [(ax, ay + 1)]
                for k in range(1, 5):
                    t = k / 4
                    wedge.append((ax + w * t * 0.25 + rnd.uniform(-1.5, 1.5), ay + length * t))
                wedge.append((ax - w * 0.6, ay + length * 0.8))
                wedge.append((ax - w * 0.5, ay + length * 0.3))
                figures.append(poly(wedge))
        return figures

    @rounded
    def caps(self, rnd, frac=(0.26, 0.4), teeth=(5, 9), streaks=4, height=None):
        """Snow on each peak: from the ridge down to a toothed lower edge, with streaks running on down the gullies.

        Returns (caps, streaks) as figures.
        """
        caps, lines = [], []
        for lv, top, rv in self.valleys():
            sx, sy = self.xs[top], self.ys[top]
            h = (height or self.base) - sy
            depth = h * rnd.uniform(*frac)
            level = sy + depth
            i0, i1 = top, top
            while i0 > lv and self.ys[i0] < level:
                i0 -= 1
            while i1 < rv and self.ys[i1] < level:
                i1 += 1
            if i1 - i0 < 4:
                continue
            x0, x1 = self.xs[i0], self.xs[i1]
            n = rnd.randint(*teeth)
            lower = []
            tips = []
            for k in range(1, 2 * n):
                t = k / (2 * n)
                x = lerp(x0, x1, t) + rnd.uniform(-0.25, 0.25) * (x1 - x0) / (2 * n)
                sag = math.sin(math.pi * t) ** 0.7
                if k % 2:
                    y = level + depth * sag * rnd.uniform(0.1, 0.55)
                    tips.append((x, y, t))
                else:
                    y = level - depth * sag * rnd.uniform(0.0, 0.3)
                lower.append((x, max(y, self.y(x) + 2)))
            caps.append(poly(self.ridge(i0, i1) + list(reversed(lower))))
            # Streaks of snow below some of the teeth, leaning away from the summit.
            for x, y, t in rnd.sample(tips, min(streaks, len(tips))):
                ln = depth * rnd.uniform(0.15, 0.45)
                lean = (t - 0.5) * ln * 1.6
                w = rnd.uniform(1.5, 3.5)
                lines.append(poly([(x - w, y - 3), (x + w, y - 3), (x + lean * 0.5 + w * 0.4, y + ln * 0.5), (x + lean, y + ln),
                                   (x + lean * 0.5 - w * 0.5, y + ln * 0.55)]))
        return caps, lines

    @rounded
    def ribs(self, rnd, per_peak=6, frac=0.3):
        """Dark rock ribs breaking through the snow near each summit, falling away from it."""
        figures = []
        for lv, top, rv in self.valleys():
            sx, sy = self.xs[top], self.ys[top]
            h = self.base - sy
            for _ in range(per_peak):
                side = rnd.choice((-1, 1))
                i = top + side * rnd.randint(2, max(3, int((rv - top if side > 0 else top - lv) * 0.5)))
                i = max(lv, min(rv, i))
                x, y = self.xs[i], self.ys[i]
                ln = h * frac * rnd.uniform(0.2, 0.6)
                w = rnd.uniform(1.2, 3)
                y += rnd.uniform(5, 12)
                pts = [(x - w * 0.4, y), (x + w * 0.4, y)]
                for k in range(1, 4):
                    t = k / 3
                    pts.append((x + side * ln * 0.3 * t + w * (1 - t) * 0.6 + rnd.uniform(-1, 1), y + ln * t))
                pts.append((x + side * ln * 0.3 - w * 0.3, y + ln * 0.85))
                figures.append(poly(pts))
        return figures

    def rim(self, width=1.8):
        """A thin moonlit edge along each peak's right shoulder."""
        figures = []
        for lv, top, rv in self.valleys():
            i1 = top + int((rv - top) * 0.75)
            line = self.ridge(top, i1)
            if len(line) < 3:
                continue
            under = [(x - width * 0.3, y + width * (0.3 + 0.7 * math.sin(math.pi * min(1, k / (len(line) - 1) * 1.6)) ** 0.5))
                     for k, (x, y) in enumerate(line)]
            figures.append(poly(line + list(reversed(under))))
        return figures


# ---------------------------------------------------------------- Trees


def spruce(cx, base, h, rnd, lean=0.0, spread=0.25, tiers=None, detail=2, snow=0.3):
    """A spruce: drooping branch tiers with ragged tips, a short trunk, snow resting on each tier.

    Returns (body, lit right half, snow on the lit side, snow on the shaded side) as figures.
    """
    n = tiers or max(4, min(12, int(h / 30)))
    crown_bottom = base - h * 0.07
    top_y = base - h

    def tx(y):
        return cx + lean * (base - y)

    attach = [lerp(top_y, crown_bottom, (k / n) ** 1.1) for k in range(n + 1)]
    widths = [h * spread * ((k + 1) / n) ** 0.8 * rnd.uniform(0.8, 1.15) for k in range(n)]
    sides, pads = {}, {}
    for side in (1, -1):
        outline = [(tx(top_y), top_y)]
        snow_pads = []
        for k in range(n):
            ya, yb = attach[k], attach[k + 1]
            gap = yb - ya
            w = widths[k] * rnd.uniform(0.88, 1.1)
            droop = gap * rnd.uniform(0.8, 1.2) + h * 0.008
            notch = (tx(ya) + side * (widths[k - 1] * 0.2 if k else 0), ya)
            tip = (tx(ya) + side * w, ya + droop)
            upper = [notch]
            for j in range(1, detail + 1):
                u = j / (detail + 1)
                upper.append((lerp(notch[0], tip[0], u),
                              lerp(notch[1], tip[1], u) - math.sin(math.pi * u) * droop * 0.16 + rnd.uniform(-1, 1) * gap * 0.04))
            upper.append(tip)
            outline += upper[1:]
            # The ragged underside, back in to where the next tier starts.
            nxt = (tx(yb) + side * w * 0.22, yb)
            outline.append((tip[0] - side * w * 0.05, tip[1] + gap * 0.16))
            for j in range(1, detail):
                u = j / detail
                outline.append((lerp(tip[0], nxt[0], u) + side * rnd.uniform(0, w * 0.05), lerp(tip[1], nxt[1], u) + gap * 0.12))
                outline.append((lerp(tip[0], nxt[0], u + 0.12), lerp(tip[1], nxt[1], u + 0.12) - gap * 0.02))
            outline.append(nxt)
            # Snow along the top of the branch, thickest towards the tip.
            th = max(1.0, gap * snow * rnd.uniform(0.75, 1.15))
            u0, u1 = rnd.uniform(0.12, 0.3), rnd.uniform(0.86, 0.98)
            m = 2 + detail
            top, low = [], []
            for j in range(m + 1):
                u = lerp(u0, u1, j / m)
                px, py = along(upper, u)
                bump = math.sin(math.pi * (0.15 + 0.75 * j / m)) ** 0.5 if 0 < j < m else 0.15
                top.append((px, py - th * bump * rnd.uniform(0.8, 1.1)))
                if j in (0, m // 2, m):
                    low.append((px, py + th * 0.2 + 0.3))
            snow_pads.append(poly(top + list(reversed(low))))
        sides[side] = outline
        pads[side] = snow_pads
    tw = max(1.2, h * 0.02)
    right = sides[1] + [(tx(crown_bottom) + tw, crown_bottom), (cx + tw, base)]
    left = sides[-1] + [(tx(crown_bottom) - tw, crown_bottom), (cx - tw, base)]
    body = poly(right + list(reversed(left))[:-1])
    lit = poly(right + [(cx, base)])
    tip = (tx(top_y), top_y)
    cap = poly([(tip[0], tip[1] - h * 0.01), (tip[0] + h * 0.016, tip[1] + h * 0.045), (tip[0], tip[1] + h * 0.035),
                (tip[0] - h * 0.014, tip[1] + h * 0.045)])
    return body, lit, pads[1] + [cap], pads[-1]


@rounded
def forest_band(rnd, x0, x1, base, hmin, hmax, spacing, depth=30, flip=1):
    """A far forest: a zigzag of spires on a strip of ground, as one figure (flip=-1 mirrors it downwards)."""
    pts = [(x0, base + depth * flip), (x0, base)]
    x = x0
    while x < x1:
        h = rnd.uniform(hmin, hmax) * (1 + 0.45 * math.sin(x / 130) * math.sin(x / 47))
        pts.append((round(x + spacing * 0.5), round(base - h * flip)))
        x += spacing * rnd.uniform(0.7, 1.3)
        pts.append((round(x), round(base - h * flip * rnd.uniform(0.15, 0.4))))
    pts += [(x1, base), (x1, base + depth * flip)]
    return poly(pts)


def cast(x, y, h, w, length=0.9):
    """A long soft shadow on the snow from something at (x, y), thrown away from the moon (to the lower left)."""
    return poly([(x + w * 0.5, y - 1), (x - w * 0.3, y - 2), (x - h * length, y + h * length * 0.16), (x - h * length * 0.8, y + h * length * 0.2)])


@rounded
def ground(points, bottom):
    """Rolling ground: a smooth line through points (left to right) and everything below it."""
    pts = [(points[0][0], bottom)] + list(points) + [(points[-1][0], bottom)]
    return shape(pts, [False] + [True] * len(points) + [False])


def y_at(points, x):
    """The height of a line (points left to right) at x."""
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        if x0 <= x <= x1:
            return lerp(y0, y1, (x - x0) / (x1 - x0))
    return points[0][1] if x < points[0][0] else points[-1][1]


# ---------------------------------------------------------------- Snow

SNOW_W, SNOW_H = 2400, 540


def snow_sky():
    rnd = random.Random(11)
    sky = Scene()
    # The aurora: soft curtains over the left of the sky, each a row of tall glows
    # brightest near their lower ends, with faint rays rising out of them. Nothing
    # in it has a hard edge, so it stays quiet behind the page.
    for (x0, x1, yb, tall, color, peak, seed) in ((60, 1400, 300, 230, '#5FE3C4', 0.19, 3), (700, 1650, 230, 180, '#7C9DFF', 0.13, 5)):
        r = random.Random(seed)
        phase = r.uniform(0, 6)
        n = 9
        rays = []
        for k in range(n):
            t = (k + 0.5) / n
            x = lerp(x0, x1, t)
            y = yb + 55 * math.sin(x / 290 + phase) + 16 * math.sin(x / 83 + phase * 2)
            e = math.sin(math.pi * t) ** 0.7
            h = tall * (0.7 + 0.3 * e) * r.uniform(0.85, 1.1)
            rx = (x1 - x0) / n * 0.95
            sky.radial(ellipse(x, y - h * 0.42, rx, h * 0.55),
                       [(0, argb(color, peak * e)), (0.45, argb(color, peak * e * 0.6)), (0.8, argb(color, peak * e * 0.18)), (1, argb(color, 0))],
                       origin=(0.5, 0.78))
            for _ in range(4):
                rx_ = x + r.uniform(-rx * 0.7, rx * 0.7)
                w = r.uniform(2, 7)
                foot = y - r.uniform(0, 30)
                rays.append(poly([(rx_, foot), (rx_ + w, foot), (rx_ + w * 0.8, yb - tall * 1.1), (rx_ + w * 0.2, yb - tall * 1.1)]))
        sky.linear(rays, [(0, argb(color, 0)), (0.55, argb(color, 0.12)), (0.85, argb(color, 0.45)), (1, argb(color, 0.1))], 0.3)

    # Stars: many small, fewer bright, thinning towards the horizon.
    groups = {0.9: [], 0.6: [], 0.35: [], 0.2: []}
    bright = []
    for _ in range(230):
        x, y = rnd.uniform(0, 2400), 820 * rnd.random() ** 1.5
        r = rnd.choice([0.8, 1.0, 1.0, 1.2, 1.4, 1.7, 2.1])
        a = rnd.choice([0.9, 0.6, 0.35, 0.35, 0.2, 0.2])
        if y > 500:
            a = min(a, 0.35)
        groups[a].append(dot(x, y, r))
    for alpha, figures in groups.items():
        sky.solid(figures, '#FFE9F3FF', alpha)
    for _ in range(11):
        x, y = rnd.uniform(60, 2340), rnd.uniform(20, 420)
        bright.append((x, y, rnd.uniform(5, 10)))
    glints = []
    for x, y, ln in bright:
        w = 0.7
        glints.append(poly([(x, y - ln), (x + w, y - w), (x + ln, y), (x + w, y + w), (x, y + ln), (x - w, y + w), (x - ln, y), (x - w, y - w)]))
        glints.append(circle(x, y, 1.6))
    sky.solid(glints, '#FFF4F9FF', 0.85)
    for x, y, ln in bright[:7]:
        sky.glow(x, y, ln * 2.2, ln * 2.2, '#CFE4FF', 0.35)
    return viewbox(sky, 2400, 900, 'top')


def snow_moon():
    moon = Scene()
    cx, cy, r = 250, 150, 54
    moon.glow(cx + 20, cy, 240, 240, '#BFD9F2', 0.16)
    moon.radial(circle(cx + 6, cy, 116), [(0, argb('#D8ECFF', 0.08)), (0.46, argb('#D8ECFF', 0.16)), (0.62, argb('#D8ECFF', 0.08)), (1, argb('#D8ECFF', 0))])
    # The dark of the moon, faintly lit by the earth; it hides the stars behind it.
    moon.radial(circle(cx, cy, r), [(0, '#FF1E2D44'), (0.8, '#FF1B2A40'), (1, '#FF2A3B55')], 0.92, origin=(0.7, 0.45))
    # The crescent, lit from the right.
    crescent = (f'M {cx},{cy - r} A {r},{r} 0 1 1 {cx},{cy + r} A {r * 0.8:.1f},{r} 0 0 0 {cx},{cy - r} Z')
    moon.radial(crescent, [(0, '#FFFFFFFF'), (0.6, '#FFEAF3FF'), (1, '#FFC9DDF2')], 0.95, origin=(0.75, 0.4))
    # Maria on the crescent.
    moon.solid([ellipse(cx + 38, cy - 18, 6, 9), ellipse(cx + 44, cy + 14, 4, 7), ellipse(cx + 30, cy + 34, 5, 4)], '#FF9DB4CF', 0.35)
    return fixed(moon, 500, 420, '0,-10,130,0')


def snow_land():
    rnd = random.Random(23)
    land = Scene()
    W, H = SNOW_W, SNOW_H
    # A pale glow over the horizon, behind the mountains.
    land.glow(1300, 330, 1500, 300, '#7FA9D4', 0.14)

    # Far range: hazy and low in contrast.
    far = Range(rnd, [(160, 150, 0.7, 0.8), (520, 105, 0.75, 0.6), (900, 170, 0.6, 0.7), (1420, 120, 0.65, 0.8),
                      (1760, 150, 0.6, 0.75), (2100, 95, 0.8, 0.65), (2400, 160, 0.7, 0.7)], W, H, 6,
                crag=((160, 12), (50, 5), (16, 2)))
    land.linear(far.body(), [(0, '#FF35527A'), (0.45, '#FF2A4568'), (1, '#FF284568')])
    caps, streaks = far.caps(rnd, (0.3, 0.45), (5, 8), 3, height=330)
    land.linear(caps + streaks, [(0, '#FFA9C0DA'), (1, '#FF7F9BBD')], 0.55)
    land.solid(far.shadows(230, spurs=2), '#FF1A2E4A', 0.4)
    land.solid(far.rim(1.3), '#FFE6F1FF', 0.3)
    land.linear(poly([(0, 150), (W, 150), (W, H), (0, H)]),
                [(0, argb('#41628A', 0)), (0.4, argb('#41628A', 0.5)), (0.65, argb('#3A5A80', 0.9)), (1, '#FF36557A')])

    # Middle range: the big peaks, with snowfields, rock ribs and moonlit edges.
    mid = Range(rnd, [(-40, 250, 0.7, 0.75), (330, 215, 0.8, 0.95), (700, 255, 0.9, 0.7), (1180, 120, 0.82, 0.62),
                      (1640, 205, 0.75, 0.9), (1950, 165, 0.8, 0.7), (2300, 230, 0.9, 0.75)], W, H, 5,
                crag=((150, 14), (45, 7), (14, 2.6)))
    land.linear(mid.body(), [(0, '#FF2A4363'), (0.5, '#FF1E3554'), (1, '#FF1A3050')])
    caps, streaks = mid.caps(rnd, (0.3, 0.42), (6, 10), 3, height=380)
    land.linear(caps, [(0, '#FFD3E1F0'), (0.4, '#FFAABED6'), (1, '#FF869FBE')], 0.7)
    land.solid(streaks, '#FF9DB4CF', 0.75)
    land.solid(mid.ribs(rnd, 7, 0.32), '#FF22395A', 0.75)
    land.solid(mid.shadows(260, spurs=3), '#FF0E1C33', 0.5)
    land.solid(mid.rim(2.0), '#FFF2F8FF', 0.5)

    # Mist between the range and the valley.
    land.linear(poly([(0, 255), (W, 255), (W, 400), (0, 400)]),
                [(0, argb('#9DB9D9', 0)), (0.55, argb('#8FAFD2', 0.14)), (0.8, argb('#86A7CB', 0.22)), (1, argb('#86A7CB', 0.06))])

    # A far forest along the valley.
    land.solid(forest_band(rnd, -10, W + 10, 374, 14, 30, 7), '#FF1A3050')
    land.solid(forest_band(rnd, -10, W + 10, 386, 10, 22, 6), '#FF13263D')

    # The valley floor, then the frozen lake on it, with the moon's light on the ice and the forest mirrored faintly.
    land.linear(poly([(-10, 388), (W + 10, 388), (W + 10, 500), (-10, 500)]), [(0, '#FF42628A'), (0.3, '#FF33527A'), (1, '#FF29466A')])
    lake_top = [(560, 386), (900, 383), (1300, 385), (1700, 382), (2050, 386), (2260, 389)]
    lake_bottom = [(560, 392), (800, 414), (1200, 420), (1650, 418), (2000, 410), (2260, 392)]
    land.linear(band(lake_top, lake_bottom), [(0, '#FF3D5E84'), (0.4, '#FF2A496E'), (1, '#FF22405F')])
    land.solid(forest_band(random.Random(5), 600, 2200, 388, 8, 16, 7, 4, -1), '#FF1C3354', 0.45)
    land.glow(1900, 400, 260, 16, '#E1EEFF', 0.55)
    land.glow(1900, 402, 70, 9, '#F2F8FF', 0.5)
    sheen = []
    r = random.Random(9)
    for _ in range(26):
        x = r.uniform(640, 2180)
        y = r.uniform(390, 412)
        ln = r.uniform(20, 90)
        sheen.append(poly([(x, y), (x + ln, y - 0.4), (x + ln, y + 0.8), (x, y + 1.1)]))
    land.solid(sheen, '#FFD4E4F5', 0.22)

    # Snowy knolls in the middle ground, left and right.
    left_knoll = [(-40, 418), (120, 394), (330, 388), (520, 400), (700, 420), (860, 446), (990, 470)]
    right_knoll = [(1420, 470), (1560, 434), (1760, 406), (1980, 394), (2200, 384), (2440, 386)]
    land.linear([ground(left_knoll, 500), ground(right_knoll, 500)], [(0, '#FF5A7899'), (0.22, '#FF41607F'), (1, '#FF26405F')])

    # Middle-distance spruces on the knolls.
    bodies, shadows, snow_lit, snow_dark = [], [], [], []
    r = random.Random(31)
    spots = [(60, 70), (120, 96), (175, 64), (240, 84), (690, 74), (740, 98), (790, 66), (845, 84),
             (1585, 60), (1640, 90), (1700, 70), (1760, 110), (1830, 76), (1890, 96), (2000, 64), (2060, 88)]
    for x, h in spots:
        y = y_at(left_knoll if x < 1200 else right_knoll, x) + 7
        h *= r.uniform(0.9, 1.1)
        b, _, sl, sd = spruce(x, y, h, r, lean=r.uniform(-0.04, 0.04), spread=0.24, detail=1)
        bodies.append(b)
        shadows.append(cast(x, y - 3, h, h * 0.2, 0.7))
        snow_lit += sl
        snow_dark += sd
    shadows.append(cast(560, 432, 70, 90, 0.6))
    land.solid(shadows, '#FF1E3658', 0.35)
    land.solid(bodies, '#FF0F1F33')
    land.solid(snow_dark, '#FF7D95B2', 0.95)
    land.solid(snow_lit, '#FFC3D5E9', 0.95)

    snow_cabin(land, 560, 432)

    # The near ground: a drift across the whole width, its hollows in blue shadow.
    near = [(-20, 470), (200, 452), (430, 446), (620, 452), (900, 468), (1200, 478), (1500, 470), (1800, 456),
            (2050, 446), (2250, 440), (2420, 446)]
    land.linear(ground(near, H), [(0, '#FF56739A'), (0.3, '#FF3A5880'), (1, '#FF233E60')])

    # Tall spruces framing both sides.
    bodies, lits, snow_lit, snow_dark, shadows = [], [], [], [], []
    r = random.Random(47)
    for x, h, lean in ((-20, 420, 0.02), (70, 330, -0.01), (175, 260, 0.03), (300, 185, -0.02),
                       (2140, 205, 0.02), (2235, 300, -0.02), (2345, 400, 0.0), (2425, 340, -0.03)):
        y = 500 + r.uniform(-4, 6)
        b, l, sl, sd = spruce(x, y, h, r, lean=lean, spread=0.25, detail=2)
        bodies.append(b)
        lits.append(l)
        snow_lit += sl
        snow_dark += sd
        shadows.append(cast(x, y - 4, h, h * 0.18, 0.55))
    land.solid(shadows, '#FF1E3658', 0.35)
    land.solid(bodies, '#FF07111E')
    land.solid(lits, '#FF0C1A2B')
    land.solid(snow_dark, '#FF6E87A6')
    land.solid(snow_lit, '#FFBFD2E7')

    # The front drift over the trees' feet, with a lit crest and sparkle.
    front = [(-20, 498), (160, 488), (380, 494), (640, 506), (980, 512), (1400, 510), (1800, 502), (2100, 492),
             (2300, 486), (2420, 490)]
    land.linear(ground(front, H), [(0, '#FF6886AA'), (0.18, '#FF4A6890'), (1, '#FF28456A')])
    crest = []
    for (x0, y0), (x1, y1) in zip(front, front[1:]):
        crest.append(poly([(x0, y0 - 0.4), (x1, y1 - 0.4), (x1 - 4, y1 + 1.4), (x0 + 4, y0 + 1.4)]))
    land.solid(crest, '#FFC9DBEE', 0.5)
    sparkle = []
    r = random.Random(53)
    for _ in range(60):
        x = r.uniform(0, W)
        y = r.uniform(450, 535) if (x < 700 or x > 1750) else r.uniform(505, 535)
        sparkle.append(dot(x, y, r.uniform(0.8, 1.8)))
    land.solid(sparkle, '#FFEAF4FF', 0.7)
    return viewbox(land, W, H, 'bottom')


def snow_cabin(c, x, ground):
    """A log cabin half buried in snow, a warm light in its windows and smoke from its chimney."""
    gw, L, wall, gh = 40, 74, 30, 24
    x0, x1 = x - (gw + L) / 2, x + (gw + L) / 2
    yw = ground - wall
    peak = (x0 + gw / 2, yw - gh)
    # Light on the snow and the glow around the windows.
    c.glow(x0 + gw + L * 0.5, ground + 6, 120, 22, '#FFB866', 0.32)
    c.glow(x0 + gw + L * 0.5, yw + 12, 90, 70, '#FFB866', 0.22)
    # Walls: the gable end in shadow, the long wall a little lighter.
    c.solid(poly([(x0, ground), (x0, yw), peak, (x0 + gw, yw), (x0 + gw, ground)]), '#FF1E1820')
    c.solid(poly([(x0 + gw, ground), (x1, ground - 3), (x1, yw - 3), (x0 + gw, yw)]), '#FF2E2328')
    logs = []
    for k in range(1, 7):
        y = yw + k * wall / 7
        logs.append(poly([(x0, y), (x0 + gw, y), (x0 + gw, y + 0.8), (x0, y + 0.8)]))
        logs.append(poly([(x0 + gw, y), (x1, y - 3), (x1, y - 2.2), (x0 + gw, y + 0.8)]))
    c.solid(logs, '#FF120D12', 0.8)
    # Windows: two on the long wall, one in the gable.
    wins = []
    for wx in (x0 + gw + 14, x0 + gw + L - 26):
        t = (wx - x0 - gw) / L
        wy = yw + 8 - 3 * t
        wins.append(poly([(wx, wy), (wx + 12, wy - 0.5), (wx + 12, wy + 11.5), (wx, wy + 12)]))
    wins.append(poly([(peak[0] - 4, yw - 9), (peak[0] + 4, yw - 9), (peak[0] + 4, yw - 1), (peak[0] - 4, yw - 1)]))
    c.linear(wins, [(0, '#FFFFDDA0'), (1, '#FFFFAE55')])
    bars = []
    for wx in (x0 + gw + 14, x0 + gw + L - 26):
        t = (wx - x0 - gw) / L
        wy = yw + 8 - 3 * t
        bars.append(poly([(wx + 5.4, wy), (wx + 6.6, wy), (wx + 6.6, wy + 12), (wx + 5.4, wy + 12)]))
        bars.append(poly([(wx, wy + 5.4), (wx + 12, wy + 5), (wx + 12, wy + 6.2), (wx, wy + 6.6)]))
    c.solid(bars, '#FF3A2622', 0.9)
    # Chimney, then the roof under its snow.
    cx0 = x0 + gw + L * 0.62
    c.solid(poly([(cx0, yw - gh * 0.5), (cx0, yw - gh - 10), (cx0 + 9, yw - gh - 10.5), (cx0 + 9, yw - gh * 0.5)]), '#FF2A2026')
    roof = [(peak[0] - 2, peak[1] - 2), (x1 - 8, peak[1] - 5), (x1 + 6, yw - 1), (x0 + gw + 3, yw + 2)]
    c.solid(poly(roof), '#FF1A1418')
    r = random.Random(61)
    top = [(peak[0] - 3, peak[1] - 6)]
    for k in range(1, 8):
        t = k / 8
        top.append((lerp(peak[0] - 3, x1 - 8, t), lerp(peak[1] - 6, peak[1] - 9, t) + r.uniform(-0.8, 0.8)))
    top += [(x1 - 6, peak[1] - 8.5), (x1 + 8, yw - 2)]
    drip = []
    for k in range(9):
        t = k / 8
        drip.append((lerp(x1 + 8, x0 + gw + 2, t), lerp(yw + 0.5, yw + 3.5, t) + (1.6 if k % 2 else -0.2)))
    snow_roof = shape(top + drip, [True] * len(top) + [True] * len(drip))
    c.linear(snow_roof, [(0, '#FFDCE8F5'), (1, '#FF9FB7D2')], 1, (0, 0), (1, 1))
    gable_snow = poly([(peak[0] - 3, peak[1] - 6), (peak[0] + 1, peak[1] - 3), (x0 + gw + 2, yw + 2.5), (x0 + gw - 1, yw + 4),
                       (x0 - 4, yw + 4), (x0 - 6, yw + 1.5)])
    c.solid(gable_snow, '#FF8EA6C3')
    c.solid(blob([(cx0 - 1.5, yw - gh - 10), (cx0 + 4.5, yw - gh - 14), (cx0 + 10.5, yw - gh - 10.5), (cx0 + 4.5, yw - gh - 8.5)]), '#FFD2E0F0')
    icicles = []
    for k in range(10):
        t = (k + 0.5) / 10
        ix = lerp(x0 + gw + 4, x1 + 4, t)
        iy = lerp(yw + 3.5, yw + 0.5, t)
        ln = r.uniform(2.5, 7)
        icicles.append(poly([(ix - 1, iy), (ix + 1, iy), (ix + 0.1, iy + ln)]))
    c.solid(icicles, '#FFCFE0F2', 0.8)
    # Smoke, drifting right and fading.
    sx, sy = cx0 + 4.5, yw - gh - 14
    left_edge, right_edge = [], []
    for k in range(13):
        t = k / 12
        mx = sx + 50 * t * t + 6 * math.sin(t * 7)
        my = sy - 120 * t
        w = 2 + 12 * t
        left_edge.append((mx - w, my))
        right_edge.append((mx + w * 0.8, my))
    c.linear(shape(left_edge + list(reversed(right_edge)), True), [(0, argb('#C9D8EA', 0)), (0.5, argb('#C9D8EA', 0.18)), (1, argb('#C9D8EA', 0.4))], 1, (0, 0), (0, 1))
    # The fence, half buried.
    posts, caps = [], []
    for k in range(6):
        px = x1 + 22 + k * 24
        py = ground + 8 + k * 2.5
        posts.append(poly([(px, py), (px, py - 16), (px + 3, py - 16.5), (px + 3, py)]))
        caps.append(blob([(px - 1.5, py - 16), (px + 1.5, py - 19.5), (px + 4.5, py - 16.3), (px + 1.5, py - 15)]))
    rails = poly([(x1 + 22, ground - 3), (x1 + 22 + 5 * 24 + 3, ground + 9.5), (x1 + 22 + 5 * 24 + 3, ground + 11), (x1 + 22, ground - 1.5)])
    c.solid(posts + [rails], '#FF1A1418')
    c.solid(caps, '#FFCBDBEC')


# ---------------------------------------------------------------- Japan

JAPAN_W, JAPAN_H = 2400, 540


def japan_sky():
    rnd = random.Random(7)
    sky = Scene()
    # A few faint stars, and long soft streaks of cloud.
    stars = {0.7: [], 0.4: [], 0.22: []}
    for _ in range(110):
        x, y = rnd.uniform(0, 2400), 640 * rnd.random() ** 1.6
        stars[rnd.choice(list(stars))].append(dot(x, y, rnd.choice([0.8, 1.0, 1.2, 1.5])))
    for alpha, figures in stars.items():
        sky.solid(figures, '#FFFFF1E4', alpha)
    for x, y, rx, ry, peak in ((520, 560, 520, 22, 0.1), (1650, 480, 480, 18, 0.08), (1900, 700, 560, 26, 0.1), (300, 330, 340, 14, 0.07)):
        sky.haze(x, y, rx, ry, '#E3A3C4', peak)
    return viewbox(sky, 2400, 900, 'top')


def cloud(x0, x1, y, height, rnd, bumps=5):
    """A cloud with a flat base and round billows on top; returns (body, the moonlit rims of its billows)."""
    # Billows grow towards the middle: uneven spacing gives them uneven sizes.
    weights = [0.6 + math.sin(math.pi * (k + 0.5) / bumps) * rnd.uniform(0.7, 1.2) for k in range(bumps)]
    total = sum(weights)
    pts = [(x0, y)]
    acc = 0
    for k in range(bumps - 1):
        acc += weights[k]
        t = acc / total
        pts.append((lerp(x0, x1, t), y - height * (0.2 + 0.55 * math.sin(math.pi * t)) * rnd.uniform(0.85, 1.1)))
    pts.append((x1, y))
    q = [_q(p) for p in pts]
    body = [f'M {_t(q[0][0])},{_t(q[0][1])}']
    rims = []
    for (ax, ay), (bx, by) in zip(q, q[1:]):
        r = round(math.dist((ax, ay), (bx, by)) * 0.56)
        body.append(f'a {_t(r)},{_t(r)} 0 0 1 {_t(bx - ax)},{_t(by - ay)}')
        t = round(height * 1.2)
        rims.append(f'M {_t(ax)},{_t(ay)} a {_t(r)},{_t(r)} 0 0 1 {_t(bx - ax)},{_t(by - ay)} l 0,{_t(t)} '
                    f'a {_t(r)},{_t(r)} 0 0 0 {_t(ax - bx)},{_t(ay - by)} z')
    body.append('z')
    return ' '.join(body), rims


def japan_moon():
    """The moon, with clouds drifting across its lower half and a few birds crossing its light."""
    m = Scene()
    cx, cy, r = 500, 196, 76
    m.glow(cx, cy, 330, 290, '#FFD9A8', 0.13)
    m.glow(cx, cy, 140, 140, '#FFE6C4', 0.15)
    m.radial(circle(cx, cy, r), [(0, '#FFFFF6E8'), (0.7, '#FFF7E0BF'), (1, '#FFEBC797')], 0.78, origin=(0.4, 0.36))
    m.solid([ellipse(cx - 22, cy - 18, 20, 15), ellipse(cx + 18, cy - 28, 12, 9), ellipse(cx + 8, cy + 14, 24, 13),
             ellipse(cx - 30, cy + 22, 10, 8)], '#FFD9B48A', 0.3)
    rnd = random.Random(17)
    bodies, rims = [], []
    for x0, x1, y, h, n in ((cx - 300, cx + 40, cy + 58, 40, 6), (cx - 30, cx + 250, cy + 98, 34, 5), (cx - 480, cx - 270, cy + 112, 26, 4)):
        b, rim = cloud(x0, x1, y, h, rnd, n)
        bodies.append(b)
        rims += rim
    m.linear(bodies, [(0, '#FF5B3B63'), (0.6, '#FF3F2849'), (1, '#FF2E1D38')], 0.94)
    m.solid(rims, '#FFF2C2A4', 0.45)
    m.haze(cx - 260, cy + 130, 300, 12, '#E8B3C9', 0.22)
    birds = []
    for bx, by, s in ((cx - 200, cy - 50, 11), (cx - 166, cy - 74, 9), (cx - 236, cy - 84, 8), (cx - 128, cy - 98, 7),
                      (cx - 270, cy - 30, 6.5)):
        birds.append(shape([(bx - s, by - s * 0.15), (bx - s * 0.45, by - s * 0.55), (bx, by), (bx + s * 0.5, by - s * 0.6),
                            (bx + s * 1.05, by - s * 0.2), (bx + s * 0.5, by - s * 0.4), (bx, by + s * 0.22), (bx - s * 0.45, by - s * 0.3)],
                           [False, True, False, True, False, True, False, True]))
    m.solid(birds, '#FF1A1020', 0.85)
    return fixed(m, 760, 480, '0,-50,40,0')


def fuji(land, rnd):
    """Mount Fuji: concave flanks, a crater rim, a snow cap streaming down its gullies, the moonlit side to the right."""
    cx, top, foot = 1290, 78, 440
    fmax = (JAPAN_H - top) / (foot - top)

    def xl(f):
        return cx - 46 - 640 * f ** 1.55

    def xr(f):
        return cx + 48 + 690 * f ** 1.55

    def at(s, f):
        return lerp(xl(f), xr(f), s), top + (foot - top) * f

    fs = [fmax * (k / 26) ** 1.4 for k in range(27)]
    left = [(xl(f), top + (foot - top) * f) for f in reversed(fs)]
    right = [(xr(f), top + (foot - top) * f) for f in fs]
    rim = [(cx - 30, top - 5), (cx - 16, top - 1), (cx - 5, top - 7), (cx + 9, top - 2), (cx + 22, top - 8), (cx + 34, top - 3)]
    with coarse():
        land.linear(poly(left + rim + right), [(0, '#FF4A3A64'), (0.3, '#FF36284F'), (1, '#FF211834')])

    # The snow: down to a ragged line, much lower in the gullies.
    fingers = [(rnd.uniform(0.03, 0.97), rnd.uniform(0.04, 0.3), rnd.uniform(0.008, 0.02)) for _ in range(34)]
    edge = []
    n = 220
    for k in range(n + 1):
        s = k / n
        f = 0.22 + 0.03 * math.sin(s * 11 + 1) + 0.012 * math.sin(s * 37)
        taper = math.sin(math.pi * s) ** 0.8
        for fsx, depth, w in fingers:
            f += depth * taper * max(0.0, 1 - abs(s - fsx) / w)
        edge.append(at(s, f))
    f0 = (edge[0][1] - top) / (foot - top)
    f1 = (edge[-1][1] - top) / (foot - top)
    cap_left = [(xl(f), top + (foot - top) * f) for f in [f0 * (1 - k / 6) for k in range(6)]]
    cap_right = [(xr(f), top + (foot - top) * f) for f in [f1 * k / 6 for k in range(1, 7)]]
    with coarse():
        cap = poly(cap_left + rim + cap_right + list(reversed(edge)))
    land.linear(cap, [(0, '#FFFBF1F6'), (0.5, '#FFE6D5E6'), (1, '#FFC2A9C9')], 0.68)

    # Gullies on the flanks below the snow, and ribs of rock in it.
    gullies, ribs = [], []
    for k in range(22):
        s = rnd.uniform(0.04, 0.96)
        f_a = rnd.uniform(0.3, 0.5)
        f_b = f_a + rnd.uniform(0.2, 0.55)
        w = rnd.uniform(0.003, 0.008)
        a_side = [at(s - w * math.sin(math.pi * t) ** 0.6, lerp(f_a, f_b, t)) for t in (0, 0.25, 0.5, 0.75, 1)]
        b_side = [at(s + w * math.sin(math.pi * t) ** 0.6, lerp(f_a, f_b, t)) for t in (0.75, 0.5, 0.25)]
        gullies.append(poly(a_side + b_side))
    for k in range(16):
        s = rnd.uniform(0.12, 0.88)
        f_a = rnd.uniform(0.03, 0.12)
        f_b = f_a + rnd.uniform(0.06, 0.14)
        w = rnd.uniform(0.003, 0.006)
        ribs.append(poly([at(s, f_a), at(s + w, lerp(f_a, f_b, 0.5)), at(s, f_b), at(s - w * 0.6, lerp(f_a, f_b, 0.6))]))
    with coarse():
        land.solid(gullies, '#FF1A1229', 0.45)
    land.solid(ribs, '#FF8D7899', 0.45)

    # The side away from the moon, in shadow.
    spine = [at(0.47 - 0.025 * f + rnd.uniform(-0.004, 0.004), f) for f in [fmax * k / 14 for k in range(15)]]
    shade = left + rim[:3] + spine[1:]
    with coarse():
        land.solid(poly(shade), '#FF120A20', 0.5)
    # Moonlight along the right shoulder.
    line = [(xr(f), top + (foot - top) * f) for f in [0.6 * (k / 12) ** 1.3 for k in range(13)]]
    under = [(x - 2.6 * (1 - k / 12) - 0.6, y + 1.8) for k, (x, y) in enumerate(line)]
    land.solid(poly(rim[-2:] + line + list(reversed(under))), '#FFFFE2CC', 0.5)


def hills(points, bottom=JAPAN_H):
    return ground(points, bottom)


@rounded
def treeline(points, rnd, size=(5, 11), bottom=12):
    """Round treetops along a hill's crest: one figure of arcs, closed a little below the crest."""
    x = points[0][0]
    y = y_at(points, x)
    cur = _q((x, y))
    out = [f'M {_t(cur[0])},{_t(cur[1])}']
    while x < points[-1][0]:
        r = rnd.uniform(*size)
        nx = x + r * rnd.uniform(1.3, 2.0)
        ny = y_at(points, nx) + rnd.uniform(-1, 1.5)
        q = _q((nx, ny))
        ry = r * rnd.uniform(0.8, 1.1)
        out.append(f'a {_t(round(r * 10 / 10) * 10)},{_t(round(ry) * 10)} 0 0 1 {_t(q[0] - cur[0])},{_t(q[1] - cur[1])}')
        cur, x, y = q, nx, ny
    out.append(f'l 0,{bottom} L {_t(_q(points[0])[0])},{_t(_q(points[0])[1] + bottom * 10)} z')
    return ' '.join(out)


def pagoda(land, cx, base):
    """A five-storey pagoda: walls with lit lattice doors, bracketed eaves with upturned corners, a spire."""
    walls, lights, posts, brackets, roofs, rims, rails = [], [], [], [], [], [], []
    y = base
    for k in range(5):
        R = 104 - k * 10
        B = 38 - k * 3.6
        hgt = 38 - k * 2.4
        roof_y = y - hgt
        walls.append(poly([(cx - B, y), (cx - B, roof_y), (cx + B, roof_y), (cx + B, y)]))
        # Lit doors: a panel of warm light split by dark mullions.
        dw = B * 0.46
        lights.append(poly([(cx - dw, y - 4), (cx - dw, roof_y + 10), (cx + dw, roof_y + 10), (cx + dw, y - 4)]))
        for j in range(-2, 3):
            px = cx + j * dw / 2.5
            posts.append(poly([(px - 0.7, y - 4), (px - 0.7, roof_y + 10), (px + 0.7, roof_y + 10), (px + 0.7, y - 4)]))
        # Pillars at the corners.
        for px in (cx - B + 2, cx + B - 2):
            posts.append(poly([(px - 1.8, y), (px - 1.8, roof_y), (px + 1.8, roof_y), (px + 1.8, y)]))
        # A railing round the balcony of each upper storey.
        if k:
            rails.append(poly([(cx - B - 7, y), (cx - B - 7, y - 7), (cx + B + 7, y - 7), (cx + B + 7, y), (cx + B + 5, y),
                               (cx + B + 5, y - 5.4), (cx - B - 5, y - 5.4), (cx - B - 5, y)]))
            for j in range(-4, 5):
                px = cx + j * (B + 5) / 4
                rails.append(poly([(px - 0.6, y), (px - 0.6, y - 6), (px + 0.6, y - 6), (px + 0.6, y)]))
        brackets.append(poly([(cx - B - 3, roof_y + 1), (cx - B - 12, roof_y - 6), (cx + B + 12, roof_y - 6), (cx + B + 3, roof_y + 1)]))
        # The roof: a thin eave sweeping up at the corners, its top rising to the next storey.
        ty = roof_y - 6
        lift = 15 - k
        eave = [(cx - R - 10, ty - lift), (cx - R + 8, ty - 1), (cx - R * 0.5, ty + 2.5), (cx + R * 0.5, ty + 2.5), (cx + R - 8, ty - 1),
                (cx + R + 10, ty - lift)]
        topline = [(cx + R + 6, ty - lift - 4.5), (cx + R - 12, ty - 8), (cx + R * 0.45, ty - 12), (cx + B * 0.7, ty - 17),
                   (cx - B * 0.7, ty - 17), (cx - R * 0.45, ty - 12), (cx - R + 12, ty - 8), (cx - R - 6, ty - lift - 4.5)]
        roofs.append(shape(eave + topline, [False, True, True, True, True, False, False, True, True, False, False, True, True, False]))
        rims.append(shape([(cx + R * 0.45, ty - 12), (cx + R - 12, ty - 8), (cx + R + 6, ty - lift - 4.5), (cx + R + 4, ty - lift - 2),
                           (cx + R - 12, ty - 5.8), (cx + R * 0.45, ty - 9.6)], [True, True, False, False, True, True]))
        y = ty - 17
    # The spire: a mast with nine rings and a flame-shaped finial.
    mast = [poly([(cx - 2.2, y + 2), (cx - 1.4, y - 80), (cx + 1.4, y - 80), (cx + 2.2, y + 2)]),
            poly([(cx - 10, y + 1), (cx - 8, y - 8), (cx + 8, y - 8), (cx + 10, y + 1)])]
    for j in range(9):
        mast.append(ellipse(cx, y - 15 - j * 6, 6.6 - j * 0.25, 1.6))
    mast.append(shape([(cx, y - 98), (cx + 4, y - 88), (cx + 2.5, y - 82), (cx - 2.5, y - 82), (cx - 4, y - 88)], [False, True, True, True, True]))
    land.glow(cx, base - 110, 180, 200, '#FF9F5C', 0.14)
    land.solid(walls, '#FF4E1A24')
    land.linear(lights, [(0, '#FFFFC27A'), (1, '#FFE7843F')], 0.75)
    land.solid(posts, '#FF2E0F16')
    land.solid(rails + brackets, '#FF1E0F17')
    land.solid(roofs + mast, '#FF150D1B')
    land.solid(rims, '#FFF0B9A6', 0.45)


def torii_parts(cx, water, half=150, height=262, pillar=16):
    """A torii's pieces as point lists: (lit vermilion, shaded vermilion, black, plaque)."""
    top = water - height
    lit, dark, black, plaque = [], [], [], []
    for px in (cx - half * 0.64, cx + half * 0.64):
        lit.append([(px, water), (px, top + 40), (px + pillar * 0.5 - 1, top + 40), (px + pillar * 0.5 + 1.5, water)])
        dark.append([(px - pillar * 0.5 - 1.5, water), (px - pillar * 0.5 + 1, top + 40), (px, top + 40), (px, water)])
        black.append([(px - pillar * 0.5 - 2.5, water), (px - pillar * 0.5 - 2.5, water - 12), (px + pillar * 0.5 + 2.5, water - 12),
                      (px + pillar * 0.5 + 2.5, water)])
    nuki_y = top + 84
    lit.append([(cx - half * 0.94, nuki_y), (cx + half * 0.94, nuki_y), (cx + half * 0.94, nuki_y + 11), (cx - half * 0.94, nuki_y + 11)])
    dark.append([(cx - half * 0.94, nuki_y + 11), (cx + half * 0.94, nuki_y + 11), (cx + half * 0.92, nuki_y + 14.5), (cx - half * 0.92, nuki_y + 14.5)])
    shimaki_y = top + 20
    lit.append([(cx - half * 0.99, shimaki_y), (cx + half * 0.99, shimaki_y), (cx + half * 0.99, shimaki_y + 12), (cx - half * 0.99, shimaki_y + 12)])
    dark.append([(cx - half * 0.99, shimaki_y + 12), (cx + half * 0.99, shimaki_y + 12), (cx + half * 0.97, shimaki_y + 15.5),
                 (cx - half * 0.97, shimaki_y + 15.5)])
    lit.append([(cx - 5, shimaki_y + 15), (cx + 5, shimaki_y + 15), (cx + 5, nuki_y), (cx - 5, nuki_y)])
    plaque.append([(cx - 13, shimaki_y + 20), (cx + 13, shimaki_y + 20), (cx + 13, shimaki_y + 52), (cx - 13, shimaki_y + 52)])
    kasagi = [(cx - half - 22, top - 12), (cx - half * 0.6, top - 1), (cx, top + 1), (cx + half * 0.6, top - 1), (cx + half + 22, top - 12),
              (cx + half + 17, top + 8), (cx + half * 0.6, top + 19), (cx, top + 21), (cx - half * 0.6, top + 19), (cx - half - 17, top + 8)]
    return lit, dark, black, plaque, kasagi, shimaki_y


def torii(land, cx, water, water_top):
    """A vermilion torii standing in the lake, and its reflection broken by ripples."""
    lit, dark, black, plaque, kasagi, shimaki_y = torii_parts(cx, water)
    smooth_kasagi = [False, True, False, True, False, False, True, False, True, False]

    def flip(pts):
        return [(x, 2 * water - y) for x, y in pts]
    # The reflection first, under the gate, cut off where the water ends.
    refl = [poly(flip(p)) for p in lit + dark] + [shape(flip(kasagi), smooth_kasagi)]
    land.linear(refl, [(0, '#99B83A30'), (0.5, '#33A0322C'), (1, '#00A0322C')], 0.8)
    land.solid([poly(p) for p in dark], '#FF8A231F')
    land.solid([poly(p) for p in lit], '#FFD4472F')
    land.solid([poly(p) for p in black] + [shape(kasagi, smooth_kasagi)], '#FF1A0C10')
    land.solid([poly(p) for p in plaque], '#FF231014')
    land.solid(poly([(cx - 9.5, shimaki_y + 23.5), (cx + 9.5, shimaki_y + 23.5), (cx + 9.5, shimaki_y + 48.5), (cx - 9.5, shimaki_y + 48.5)]),
               '#FFC9A15E', 0.5)
    # Moonlight along the kasagi's top.
    k = kasagi
    land.solid(shape([k[2], k[3], k[4], (k[4][0] - 3, k[4][1] + 3), (k[3][0], k[3][1] + 2.5), (k[2][0], k[2][1] + 2.5)],
                     [False, True, False, False, True, False]), '#FFF5B7A0', 0.4)


def sakura(cx, base, h, rnd, lean=0.0, spread=1.0, width=1.0):
    """A cherry tree in bloom: a dark trunk and spreading limbs under clumps of blossom lit from the upper right.

    Returns (wood, shadow, blossom, lit rim, petals) figures; the rim goes under the blossom.
    """
    wood = []
    spots = []

    def grow(x, y, ang, length, w, depth):
        x2 = x + math.cos(ang) * length
        y2 = y - math.sin(ang) * length
        bend = rnd.uniform(-0.16, 0.16) * length
        mx = (x + x2) / 2 + math.sin(ang) * bend
        my = (y + y2) / 2 + math.cos(ang) * bend
        nx, ny = math.sin(ang), math.cos(ang)
        w1 = w * 0.64
        wood.append(shape([(x - nx * w, y - ny * w), (mx - nx * (w + w1) / 2, my - ny * (w + w1) / 2), (x2 - nx * w1, y2 - ny * w1),
                           (x2 + nx * w1, y2 + ny * w1), (mx + nx * (w + w1) / 2, my + ny * (w + w1) / 2), (x + nx * w, y + ny * w)],
                          [False, depth > 1, False, False, depth > 1, False]))
        if depth <= 1:
            spots.extend([(mx, my), (x2, y2)])
        if depth == 0:
            return
        kids = 2 if depth == 3 else rnd.choice((2, 2, 3))
        for j in range(kids):
            a = ang + (j - (kids - 1) / 2) * rnd.uniform(0.6, 0.95) * spread + rnd.uniform(-0.12, 0.12)
            # Limbs spread wide and level out, as cherry trees do.
            a = lerp(a, math.pi / 2 + (a - math.pi / 2) * 1.35, 0.5)
            a = max(0.12, min(math.pi - 0.12, a))
            grow(x2, y2, a, length * rnd.uniform(0.66, 0.82) * width, w1 * 0.9, depth - 1)

    grow(cx, base, math.pi / 2 + lean, h * 0.3, h * 0.032, 3)
    deep, mid, light, petals = [], [], [], []
    R = h * 0.058
    with coarse():
        for x, y in spots:
            # A clump: a shaded mass to the lower left, and small rounded tufts over it.
            cx_, cy_ = x + rnd.uniform(-R * 0.5, R * 0.5), y + rnd.uniform(-R * 0.2, R * 0.6)
            deep.append(ellipse(cx_ - R * 0.2, cy_ + R * 0.22, R * rnd.uniform(1.05, 1.25), R * rnd.uniform(0.78, 0.92)))
            for _ in range(rnd.choice((2, 3, 3))):
                px, py = cx_ + rnd.uniform(-R * 0.85, R * 0.85), cy_ + rnd.uniform(-R * 0.55, R * 0.35)
                r = R * rnd.uniform(0.5, 0.85)
                mid.append(ellipse(px, py, r * 1.06, r * 0.94))
                # Drawn under the blossom, shifted towards the moon: only a lit rim shows.
                light.append(ellipse(px + r * 0.16, py - r * 0.18, r * 1.04, r * 0.92))
    for x, y in spots[::2]:
        a = rnd.uniform(-1.9, 0.2)
        d = R * rnd.uniform(0.2, 0.8)
        petals.append(dot(x + math.cos(a) * d, y - R * 0.2 + math.sin(a) * d, rnd.uniform(1.1, 1.9)))
    return wood, deep, mid, light, petals


def lantern(cx, base, s=1.0):
    """A stone lantern: (stone, lit edges, light) figures."""
    def P(pts):
        return [(cx + x * s, base + y * s) for x, y in pts]
    stone = [poly(P([(-11, 0), (-11, -5), (11, -5), (11, 0)])), poly(P([(-3.5, -5), (-3, -26), (3, -26), (3.5, -5)])),
             poly(P([(-10, -26), (-8, -31), (8, -31), (10, -26)])),
             poly(P([(-7, -31), (-7, -45), (-4.5, -45), (-4.5, -31)])), poly(P([(4.5, -31), (4.5, -45), (7, -45), (7, -31)])),
             shape(P([(-17, -44), (-12, -47), (0, -54), (12, -47), (17, -44), (13, -45.5), (0, -50), (-13, -45.5)]),
                   [False, True, False, True, False, True, False, True]),
             poly(P([(-1.6, -53), (-2.8, -57), (0, -61), (2.8, -57), (1.6, -53)]))]
    edges = [poly(P([(8, -5), (11, -5), (11, 0), (8, 0)])), poly(P([(1.5, -5), (2.8, -26), (3, -26), (3.5, -5)])),
             poly(P([(5.8, -31), (7, -31), (7, -45), (5.8, -45)])), poly(P([(4, -48.6), (12, -47), (17, -44), (12, -46), (4, -47.4)]))]
    light = [poly(P([(-4.5, -32), (4.5, -32), (4.5, -44), (-4.5, -44)]))]
    return stone, edges, light


def japan_land():
    rnd = random.Random(13)
    land = Scene()
    W, H = JAPAN_W, JAPAN_H
    # The dusk's glow low over the horizon.
    land.glow(1250, 390, 1450, 240, '#E0607E', 0.15)

    # Far ridges to either side of the mountain.
    far_line = [(-20, 318), (160, 292), (380, 310), (560, 284), (760, 316), (980, 345), (1700, 350), (1900, 312), (2080, 290),
                (2260, 306), (2420, 294)]
    with coarse():
        land.linear(hills(far_line, H), [(0, '#FF3A2649'), (1, '#FF2A1B3A')], 0.7)
    fuji(land, rnd)

    # Mist across the mountain's foot, in soft bands.
    land.linear(poly([(0, 290), (W, 290), (W, 420), (0, 420)]),
                [(0, argb('#C98AAE', 0)), (0.5, argb('#C98AAE', 0.1)), (0.8, argb('#B9789E', 0.16)), (1, argb('#B9789E', 0.04))])
    for x, y, rx, ry, peak in ((1140, 350, 520, 20, 0.24), (1820, 322, 360, 14, 0.2), (360, 300, 380, 16, 0.18)):
        land.haze(x, y, rx, ry, '#F2BDD6', peak)

    # Rolling hills, nearer ones darker, trees along their crests and groves in blossom.
    mid_line = [(-20, 372), (220, 352), (470, 366), (700, 380), (960, 392), (1250, 384), (1520, 396), (1760, 380), (2000, 362),
                (2200, 372), (2420, 360)]
    r = random.Random(19)
    with coarse():
        land.solid(treeline(mid_line, r, (4, 9)), '#FF2B1C38')
        land.linear(hills(mid_line, H), [(0, '#FF271831'), (1, '#FF1B1026')])
    groves = []
    for gx in (300, 640, 1120, 1600, 1880, 2140):
        for _ in range(10):
            x = gx + r.uniform(-55, 55)
            y = y_at(mid_line, x) + r.uniform(-2, 9)
            groves.append(ellipse(x, y, r.uniform(5, 10), r.uniform(4, 7)))
    land.solid(groves, '#FFA9507A', 0.5)

    # The lake on the right, with the moon's light trembling on it.
    water = 434
    with coarse():
        land.linear(poly([(880, water - 8), (W, water - 8), (W, H), (880, H)]), [(0, '#FF2F1C37'), (0.35, '#FF22152B'), (1, '#FF170E20')])
    land.glow(2010, 470, 150, 60, '#FFD49A', 0.16)
    shimmer = []
    r = random.Random(29)
    for k in range(24):
        y = water + 3 + k * 3.6
        w = 12 + k * 2.4 * r.uniform(0.6, 1.3)
        for _ in range(2):
            x = 2010 + r.uniform(-1, 1) * (8 + k * 2.4) - w / 2
            shimmer.append(poly([(x, y), (x + w, y), (x + w - 2, y + 1.2), (x + 2, y + 1.2)]))
    land.linear(shimmer, [(0, '#FFFFE2B8'), (1, '#33FFC98A')], 0.7)

    # The near hill on the left, with the pagoda on it.
    near_line = [(-20, 412), (180, 394), (420, 386), (640, 396), (840, 420), (980, 448), (1080, 470)]
    with coarse():
        land.linear(hills(near_line, H), [(0, '#FF1D1229'), (1, '#FF120A1A')])
    pagoda(land, 575, 392)
    torii(land, 2110, water + 30, water - 8)

    # Ripples across the water, over the reflections.
    ripples = []
    for k in range(46):
        y = water + r.uniform(3, 100)
        x = r.uniform(900, W)
        w = r.uniform(30, 120) * (0.6 + (y - water) / 100)
        ripples.append(poly([(x, y), (x + w, y), (x + w - 3, y + 1.1), (x + 3, y + 1.1)]))
    land.solid(ripples, '#FFE7A9C6', 0.16)
    cuts = []
    for k in range(18):
        y = water + 34 + k * 5.2 + r.uniform(-1, 1)
        x = 2110 - 170 + r.uniform(-20, 20)
        cuts.append(poly([(x, y), (x + 340, y), (x + 340, y + 1.6), (x, y + 1.6)]))
    land.solid(cuts, '#FF241730', 0.55)

    # Cherry trees: a large one on the left, one by the pagoda, one leaning over the water on the right.
    wood, deep, mid, light, petals = [], [], [], [], []
    for x, y, h, lean, seed, width in ((170, 480, 500, 0.1, 3, 1.05), (800, 436, 270, -0.06, 4, 1.0), (2530, 512, 480, 0.3, 8, 1.0),
                                      (1020, 452, 190, 0.05, 6, 0.95)):
        parts = sakura(x, y, h, random.Random(seed), lean, width=width)
        for bucket, figs in zip((wood, deep, mid, light, petals), parts):
            bucket += figs
    land.solid(deep, '#FF55203F')
    land.solid(wood, '#FF190C16')
    land.solid(light, '#FFC97BA1')
    land.solid(mid, '#FF8A4068')
    land.solid(petals, '#FFF2B6CE', 0.8)

    # Stone lanterns with their lights on.
    stone, edges, light_ = [], [], []
    for x, y, s in ((440, 474, 1.35), (880, 448, 1.05), (2330, 506, 1.25)):
        a, b, c = lantern(x, y, s)
        stone += a
        edges += b
        light_ += c
        land.glow(x, y - 48 * s, 50 * s, 50 * s, '#FFB866', 0.4)
    land.solid(stone, '#FF2C2235')
    land.solid(edges, '#FF6A5874', 0.8)
    land.solid(light_, '#FFFFC67A')

    # The near bank: dark grass, and petals fallen on it.
    bank_line = [(-20, 490), (300, 482), (620, 494), (940, 502), (1200, 514), (1500, 518), (1900, 508), (2200, 500), (2420, 496)]
    with coarse():
        land.linear(hills(bank_line, H), [(0, '#FF1C1124'), (1, '#FF0E0814')])
    grass = []
    r = random.Random(41)
    for _ in range(110):
        x = r.uniform(0, W)
        y = y_at(bank_line, x) + 2
        hgt = r.uniform(6, 16)
        grass.append(poly([(x - 1.4, y), (x + r.uniform(-4, 4), y - hgt), (x + 1.4, y)]))
    land.solid(grass, '#FF1C1124')
    fallen = []
    for _ in range(80):
        x = r.uniform(0, W)
        y = r.uniform(y_at(bank_line, x) + 6, 536)
        fallen.append(ellipse(x, y, r.uniform(1.4, 2.6), r.uniform(0.7, 1.2)))
    land.solid(fallen, '#FFE9A0BE', 0.45)
    return viewbox(land, W, H, 'bottom')


def japan():
    return japan_sky() + japan_moon() + japan_land()


# ---------------------------------------------------------------- File


def snow():
    return snow_sky() + snow_moon() + snow_land()


def main():
    Scene.count = 0
    j = japan()
    jc = Scene.count
    Scene.count = 0
    s = snow()
    sc = Scene.count
    xaml = f'''<?xml version="1.0" encoding="utf-8"?>
<!--
  The special looks' scenery, behind the panels (see SceneArt.xaml.cs),
  written by tools/scenes/build_scene_art.py; do not edit by hand.
  Japan: a moon behind drifting clouds, Mount Fuji, misty hills, a pagoda,
  blossoming cherry trees, stone lanterns and a torii in a lake.
  Snow: an aurora and stars, a crescent moon, snowy ranges, a frozen lake,
  spruces, a cabin with lit windows and drifts.
  Flat shapes and gradients only, so it is crisp at any size and costs
  nothing once drawn. Only the scene in use is in the tree.
-->
<UserControl
    x:Class="Resonate.App.Controls.SceneArt"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    IsHitTestVisible="False">
    <Grid>
        <!-- Made only while its scene shows (x:Load), so the other looks pay nothing. -->
        <Grid x:Name="JapanArt" x:Load="False">{j}</Grid>
        <Grid x:Name="SnowArt" x:Load="False">{s}</Grid>
    </Grid>
</UserControl>
'''
    open(OUT, 'w', encoding='utf-8', newline='\n').write(xaml)
    print(f'{len(xaml)} bytes; Japan {jc} elements, Snow {sc} elements')


main()
