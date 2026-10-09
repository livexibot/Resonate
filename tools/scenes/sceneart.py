"""Shared helpers for tools/scenes: XAML paths, gradients and layers for the special looks' scenery.

The scenes themselves are in build_scene_art.py (Japan and Snow) and in one
module each for the others; build_scene_art.py writes them all into
src/Resonate.App/Controls/SceneArt.xaml.
"""
import math
import random

# ---------------------------------------------------------------- Paths
#
# Coordinates are rounded to tenths first and written relative to the point
# before (l, c), which keeps the file small without drifting. Every figure is
# wound the same way and drawn with the nonzero rule (F1), so overlapping
# figures in one path merge instead of cutting holes.


def _t(n):
    s = f'{n / 10:.1f}'
    if s.endswith('.0'):
        s = s[:-2]
    return '0' if s == '-0' else s


_STEP = [1]


class coarse:
    """Within it, figures are rounded to whole units: plenty for big shapes, and shorter."""

    def __enter__(self):
        _STEP[0] = 10

    def __exit__(self, *_):
        _STEP[0] = 1


def rounded(fn):
    """Builds a function's figures in whole units (see coarse)."""

    def run(*args, **kwargs):
        with coarse():
            return fn(*args, **kwargs)

    return run


def _q(p):
    s = _STEP[0]
    return round(p[0] * 10 / s) * s, round(p[1] * 10 / s) * s


def _area(points):
    return sum(x1 * y2 - x2 * y1 for (x1, y1), (x2, y2) in zip(points, points[1:] + points[:1])) / 2


def shape(points, smooth=None, closed=True):
    """One figure through points; smooth[i] makes the line through point i a curve (Catmull-Rom)."""
    pts = list(points)
    sm = list(smooth) if isinstance(smooth, (list, tuple)) else [bool(smooth)] * len(pts)
    if closed and _area(pts) < 0:
        pts.reverse()
        sm.reverse()
    n = len(pts)
    out = []
    cur = _q(pts[0])
    out.append(f'M {_t(cur[0])},{_t(cur[1])}')
    last = ''
    count = n if closed else n - 1
    for i in range(count):
        a, b = pts[i], pts[(i + 1) % n]
        if not (sm[i] or sm[(i + 1) % n]):
            q = _q(b)
            if q == cur:
                continue
            cmd = '' if last == 'l' else 'l '
            out.append(f'{cmd}{_t(q[0] - cur[0])},{_t(q[1] - cur[1])}')
            cur, last = q, 'l'
            continue
        prev = pts[i - 1] if (closed or i > 0) else a
        nxt = pts[(i + 2) % n] if (closed or i + 2 < n) else b
        c1 = (a[0] + (b[0] - prev[0]) / 6, a[1] + (b[1] - prev[1]) / 6) if sm[i] else a
        c2 = (b[0] - (nxt[0] - a[0]) / 6, b[1] - (nxt[1] - a[1]) / 6) if sm[(i + 1) % n] else b
        q1, q2, q3 = _q(c1), _q(c2), _q(b)
        out.append(f'c {_t(q1[0] - cur[0])},{_t(q1[1] - cur[1])} {_t(q2[0] - cur[0])},{_t(q2[1] - cur[1])} '
                   f'{_t(q3[0] - cur[0])},{_t(q3[1] - cur[1])}')
        cur, last = q3, 'c'
    if closed:
        out.append('z')
    return ' '.join(out)


def ellipse(cx, cy, rx, ry=None):
    ry = rx if ry is None else ry
    x0 = _q((cx - rx, cy))
    d = round(rx * 20)
    rxs, rys = _t(round(rx * 10)), _t(round(ry * 10))
    return f'M {_t(x0[0])},{_t(x0[1])} a {rxs},{rys} 0 1 1 {_t(d)},0 {rxs},{rys} 0 1 1 {_t(-d)},0 z'


def circle(cx, cy, r):
    return ellipse(cx, cy, r)


def dot(cx, cy, r):
    """A tiny diamond: as good as a circle at a pixel or two, and shorter."""
    x, y = _q((cx - r, cy))
    d = round(r * 10)
    return f'M {_t(x)},{_t(y)} l {_t(d)},{_t(-d)} {_t(d)},{_t(d)} {_t(-d)},{_t(d)} z'


def poly(points):
    return shape(points, False)


def blob(points):
    return shape(points, True)


def band(top, bottom, smooth_top=True, smooth_bottom=True):
    """The area between two lines, both given left to right."""
    pts = list(top) + list(reversed(bottom))
    sm = [smooth_top] * len(top) + [smooth_bottom] * len(bottom)
    sm[0] = sm[len(top) - 1] = sm[len(top)] = sm[-1] = False
    return shape(pts, sm)


def lerp(a, b, t):
    return a + (b - a) * t


def along(points, u):
    """The point at fraction u of the way along a polyline."""
    lengths = [math.dist(a, b) for a, b in zip(points, points[1:])]
    target = u * sum(lengths)
    for (a, b), length in zip(zip(points, points[1:]), lengths):
        if target <= length or length == 0:
            t = target / length if length else 0
            return lerp(a[0], b[0], t), lerp(a[1], b[1], t)
        target -= length
    return points[-1]


# ---------------------------------------------------------------- Elements


class Scene:
    """Collects a layer's XAML elements and counts them.

    An element given a motion moves in the app: it gets a name (the scene's
    prefix, M and a number, see SceneArt.xaml.cs) and the motion as its Tag,
    a kind and its values (see Resonate.Themes/SceneMotion.cs). The app sets
    a moving element's opacity itself, so its opacity goes in the Tag (o=).
    """

    count = 0

    # The scene being written, and how many of its elements move.
    prefix = ''
    moving = 0

    def __init__(self):
        self.parts = []
        self._groups = []

    def add(self, xml):
        (self._groups[-1][1] if self._groups else self.parts).append(xml)
        Scene.count += 1

    @staticmethod
    def _attrs(opacity, motion):
        if not motion:
            return _op(opacity)
        name = f'{Scene.prefix}M{Scene.moving}'
        Scene.moving += 1
        tag = motion if opacity >= 1 else f'{motion} o={opacity:.3f}'
        return f' x:Name="{name}" Tag="{tag}"'

    def group(self, motion=None, opacity=1.0):
        """Elements added within it move together: with scene.group('drift ...'): scene.solid(...)."""
        scene = self

        class _Group:
            def __enter__(self):
                scene._groups.append((motion, []))
                return scene

            def __exit__(self, *_):
                m, parts = scene._groups.pop()
                if parts:
                    scene.add(f'<Canvas{Scene._attrs(opacity, m)}>{"".join(parts)}</Canvas>')

        return _Group()

    def solid(self, figures, color, opacity=1.0, motion=None):
        data = ' '.join(figures) if isinstance(figures, list) else figures
        if data:
            self.add(f'<Path Data="F1 {data}" Fill="{color}"{self._attrs(opacity, motion)} />')

    def linear(self, figures, stops, opacity=1.0, start=(0, 0), end=(0, 1), motion=None):
        data = ' '.join(figures) if isinstance(figures, list) else figures
        if data:
            self.add(f'<Path Data="F1 {data}"{self._attrs(opacity, motion)}><Path.Fill><LinearGradientBrush StartPoint="{start[0]},{start[1]}" '
                     f'EndPoint="{end[0]},{end[1]}">{_stops(stops)}</LinearGradientBrush></Path.Fill></Path>')

    def radial(self, figures, stops, opacity=1.0, origin=None, radius=None, motion=None):
        data = ' '.join(figures) if isinstance(figures, list) else figures
        o = f' GradientOrigin="{origin[0]},{origin[1]}"' if origin else ''
        if radius:
            o += f' RadiusX="{radius[0]}" RadiusY="{radius[1]}"'
        self.add(f'<Path Data="F1 {data}"{self._attrs(opacity, motion)}><Path.Fill><RadialGradientBrush{o}>{_stops(stops)}'
                 f'</RadialGradientBrush></Path.Fill></Path>')

    def stroke(self, figures, color, width, opacity=1.0, motion=None, cap='Round'):
        """Lines through figures (open ones from shape(..., closed=False)), width units wide."""
        data = ' '.join(figures) if isinstance(figures, list) else figures
        if data:
            self.add(f'<Path Data="F1 {data}" Stroke="{color}" StrokeThickness="{width:g}" StrokeLineJoin="Round" '
                     f'StrokeStartLineCap="{cap}" StrokeEndLineCap="{cap}"{self._attrs(opacity, motion)} />')

    def stroke_linear(self, figures, stops, width, opacity=1.0, start=(0, 0), end=(0, 1), motion=None, cap='Round'):
        data = ' '.join(figures) if isinstance(figures, list) else figures
        if data:
            self.add(f'<Path Data="F1 {data}" StrokeThickness="{width:g}" StrokeLineJoin="Round" StrokeStartLineCap="{cap}" '
                     f'StrokeEndLineCap="{cap}"{self._attrs(opacity, motion)}><Path.Stroke><LinearGradientBrush StartPoint="{start[0]},{start[1]}" '
                     f'EndPoint="{end[0]},{end[1]}">{_stops(stops)}</LinearGradientBrush></Path.Stroke></Path>')

    def haze(self, cx, cy, rx, ry, color, peak, motion=None):
        """A soft streak of cloud or mist: an ellipse fading out all round."""
        self.radial(ellipse(cx, cy, rx, ry), [(0, argb(color, peak)), (0.45, argb(color, peak * 0.75)), (0.8, argb(color, peak * 0.25)),
                                               (1, argb(color, 0))], motion=motion)

    def glow(self, cx, cy, rx, ry, color, peak, opacity=1.0, motion=None):
        """A soft round light: the colour at peak alpha in the middle, fading out."""
        self.radial(ellipse(cx, cy, rx, ry), [(0, argb(color, peak)), (0.35, argb(color, peak * 0.55)),
                                               (0.7, argb(color, peak * 0.16)), (1, argb(color, 0))], opacity, motion=motion)

    def xml(self):
        return ''.join(self.parts)


def line(*points):
    """An open line through points (for Scene.stroke)."""
    return shape(points, False, closed=False)


def rect(x, y, w, h):
    return poly([(x, y), (x + w, y), (x + w, y + h), (x, y + h)])


def _op(opacity):
    return '' if opacity >= 1 else f' Opacity="{opacity:.2f}"'


def _stops(stops):
    return ''.join(f'<GradientStop Offset="{off}" Color="{c}" />' for off, c in stops)


def argb(color, a=1.0):
    return f'#{round(max(0, min(1, a)) * 255):02X}{color.lstrip("#")[-6:]}'


def mix(c1, c2, t):
    a = [int(c1.lstrip('#')[i:i + 2], 16) for i in (0, 2, 4)]
    b = [int(c2.lstrip('#')[i:i + 2], 16) for i in (0, 2, 4)]
    return '#' + ''.join(f'{round(lerp(x, y, t)):02X}' for x, y in zip(a, b))


def viewbox(canvas, w, h, where):
    """A canvas scaled to the window: 'bottom' fits the width at the bottom, 'top' fills the top."""
    if where == 'bottom':
        head = '<Viewbox HorizontalAlignment="Stretch" VerticalAlignment="Bottom" Stretch="Uniform">'
    else:
        head = '<Viewbox HorizontalAlignment="Stretch" VerticalAlignment="Top" Stretch="UniformToFill" MaxHeight="900">'
    return f'{head}<Canvas Width="{w}" Height="{h}">{canvas.xml()}</Canvas></Viewbox>'


def fixed(canvas, w, h, margin):
    """A canvas of a fixed size in the window's top right corner (the moon)."""
    return (f'<Viewbox HorizontalAlignment="Right" VerticalAlignment="Top" Margin="{margin}" Width="{w}">'
            f'<Canvas Width="{w}" Height="{h}">{canvas.xml()}</Canvas></Viewbox>')


# ---------------------------------------------------------------- Noise


def noise(rnd, length, step, octaves):
    """Smooth 1D noise sampled every step over [0, length]: (wavelength, amplitude) octaves."""
    n = int(length / step) + 1
    out = [0.0] * n
    for wave, amp in octaves:
        knots = [rnd.uniform(-1, 1) for _ in range(int(length / wave) + 3)]
        for i in range(n):
            u = i * step / wave
            k = int(u)
            t = u - k
            t = t * t * (3 - 2 * t)
            out[i] += lerp(knots[k], knots[k + 1], t) * amp
    return out
