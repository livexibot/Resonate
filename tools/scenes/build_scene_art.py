"""Writes src/Resonate.App/Controls/SceneArt.xaml: the special looks' scenery as XAML paths."""
import math
import random

OUT = 'src/Resonate.App/Controls/SceneArt.xaml'


def f(v):
    s = f'{v:.1f}'
    return s[:-2] if s.endswith('.0') else s


def pts(points):
    return ' '.join(f'{f(x)},{f(y)}' for x, y in points)


def circle(cx, cy, r):
    return f'M {f(cx - r)},{f(cy)} A {f(r)},{f(r)} 0 1 0 {f(cx + r)},{f(cy)} A {f(r)},{f(r)} 0 1 0 {f(cx - r)},{f(cy)} Z'


def path(data, fill, opacity=1.0, extra=''):
    o = '' if opacity >= 1 else f' Opacity="{opacity:.2f}"'
    return f'<Path Data="{data}" Fill="{fill}"{o}{extra} />'


def grad_path(data, stops, opacity=1.0, vertical=True):
    o = '' if opacity >= 1 else f' Opacity="{opacity:.2f}"'
    end = '0,1' if vertical else '1,0'
    s = ''.join(f'<GradientStop Offset="{off}" Color="{c}" />' for off, c in stops)
    return (f'<Path Data="{data}"{o}><Path.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="{end}">'
            f'{s}</LinearGradientBrush></Path.Fill></Path>')


# ---------------------------------------------------------------- Japan

def pagoda(cx, base, tiers=5):
    """A five-storey pagoda: a body and an upturned roof per storey, a spire on top."""
    roofs, walls, windows = [], [], []
    y = base
    widths = [150 - k * 18 for k in range(tiers)]
    storey = 52
    for k in range(tiers):
        w = widths[k]
        body = w * 0.56
        wall_top = y - storey + 16
        walls.append(f'M {f(cx - body / 2)},{f(y)} L {f(cx - body / 2)},{f(wall_top)} L {f(cx + body / 2)},{f(wall_top)} L {f(cx + body / 2)},{f(y)} Z')
        # A lit window in the middle of each storey.
        ww, wh = body * 0.22, 12
        windows.append(f'M {f(cx - ww / 2)},{f(y - 10 - wh)} h {f(ww)} v {f(wh)} h {f(-ww)} Z')
        ry = wall_top
        half = w / 2
        roofs.append(
            f'M {f(cx - half - 14)},{f(ry - 14)} '
            f'Q {f(cx - half + 6)},{f(ry - 2)} {f(cx - half + 26)},{f(ry - 6)} '
            f'L {f(cx - body / 2 - 4)},{f(ry - 18)} L {f(cx + body / 2 + 4)},{f(ry - 18)} '
            f'L {f(cx + half - 26)},{f(ry - 6)} Q {f(cx + half - 6)},{f(ry - 2)} {f(cx + half + 14)},{f(ry - 14)} '
            f'L {f(cx + half - 4)},{f(ry + 2)} L {f(cx - half + 4)},{f(ry + 2)} Z')
        y = ry - 18
    spire = f'M {f(cx - 3)},{f(y)} L {f(cx - 2)},{f(y - 70)} L {f(cx + 2)},{f(y - 70)} L {f(cx + 3)},{f(y)} Z'
    rings = ' '.join(f'M {f(cx - 7)},{f(y - 14 - i * 11)} h 14 v 4 h -14 Z' for i in range(5))
    return ' '.join(roofs + walls) + ' ' + spire + ' ' + rings, ' '.join(windows)


def torii(cx, base, width=320, height=250):
    """A torii gate: two pillars, the nuki beam, and the kasagi on top with upturned ends."""
    half = width / 2
    pillar = 18
    left = cx - half * 0.62
    right = cx + half * 0.62
    pillars = (
        f'M {f(left - pillar / 2 - 3)},{f(base)} L {f(left - pillar / 2)},{f(base - height + 40)} L {f(left + pillar / 2)},{f(base - height + 40)} L {f(left + pillar / 2 + 3)},{f(base)} Z '
        f'M {f(right - pillar / 2 - 3)},{f(base)} L {f(right - pillar / 2)},{f(base - height + 40)} L {f(right + pillar / 2)},{f(base - height + 40)} L {f(right + pillar / 2 + 3)},{f(base)} Z')
    nuki_y = base - height + 92
    nuki = f'M {f(cx - half * 0.86)},{f(nuki_y)} h {f(width * 0.86)} v 14 h {f(-width * 0.86)} Z'
    shimaki_y = base - height + 46
    shimaki = f'M {f(cx - half * 0.94)},{f(shimaki_y)} h {f(width * 0.94)} v 14 h {f(-width * 0.94)} Z'
    gakuzuka = f'M {f(cx - 7)},{f(shimaki_y + 14)} h 14 v {f(nuki_y - shimaki_y - 14)} h -14 Z'
    top = base - height + 24
    kasagi = (f'M {f(cx - half - 22)},{f(top - 26)} Q {f(cx - half + 20)},{f(top)} {f(cx)},{f(top + 2)} '
              f'Q {f(cx + half - 20)},{f(top)} {f(cx + half + 22)},{f(top - 26)} '
              f'L {f(cx + half + 10)},{f(top + 20)} Q {f(cx + half - 30)},{f(top + 22)} {f(cx)},{f(top + 22)} '
              f'Q {f(cx - half + 30)},{f(top + 22)} {f(cx - half - 10)},{f(top + 20)} Z')
    bases = (f'M {f(left - pillar / 2 - 6)},{f(base)} v -16 h {f(pillar + 12)} v 16 Z '
             f'M {f(right - pillar / 2 - 6)},{f(base)} v -16 h {f(pillar + 12)} v 16 Z')
    return pillars + ' ' + nuki + ' ' + shimaki + ' ' + gakuzuka, kasagi + ' ' + bases


def japan():
    rnd = random.Random(7)
    parts = []

    # The moon, top right, with its halo.
    moon = (
        '<Grid HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,-60,70,0" Width="460" Height="460">'
        '<Ellipse><Ellipse.Fill><RadialGradientBrush>'
        '<GradientStop Offset="0" Color="#40FFE6C4" /><GradientStop Offset="0.45" Color="#14FFD9A8" /><GradientStop Offset="1" Color="#00FFD9A8" />'
        '</RadialGradientBrush></Ellipse.Fill></Ellipse>'
        '<Ellipse Width="150" Height="150" Opacity="0.82"><Ellipse.Fill><RadialGradientBrush GradientOrigin="0.38,0.35">'
        '<GradientStop Offset="0" Color="#FFFFF6E6" /><GradientStop Offset="0.7" Color="#FFF6DDB8" /><GradientStop Offset="1" Color="#FFE9C79A" />'
        '</RadialGradientBrush></Ellipse.Fill></Ellipse>'
        '</Grid>')

    # The landscape along the bottom.
    W, H = 2400, 500
    far = ('M 0,500 L 0,330 C 160,300 300,318 460,296 C 640,272 760,300 900,312 C 1040,322 1150,290 1280,300 '
           'C 1420,310 1520,280 1700,292 C 1880,304 2050,280 2200,296 C 2300,306 2360,300 2400,296 L 2400,500 Z')
    fuji = 'M 1060,500 C 1270,340 1410,196 1476,132 L 1530,130 C 1596,192 1736,336 1950,500 Z'
    cap = ('M 1476,132 L 1530,130 C 1554,152 1576,172 1602,198 L 1584,194 L 1566,214 L 1548,198 L 1530,224 L 1512,202 '
           'L 1494,228 L 1478,206 L 1458,222 L 1442,200 L 1420,208 C 1436,180 1456,154 1476,132 Z')
    hills = ('M 0,500 L 0,402 C 260,356 520,372 760,398 C 980,422 1160,372 1400,392 C 1640,412 1860,360 2100,376 '
             'C 2250,386 2350,368 2400,360 L 2400,500 Z')
    trees = []
    for x in [560, 640, 700, 1180, 1260, 1700, 1780, 1850]:
        y = 402 if x < 900 else 392 if x < 1500 else 380
        r = rnd.uniform(22, 34)
        trees.append(circle(x, y - r * 0.6, r))
        trees.append(circle(x + r * 0.7, y - r * 0.3, r * 0.75))
        trees.append(circle(x - r * 0.7, y - r * 0.25, r * 0.7))
    pagoda_body, pagoda_windows = pagoda(380, 476)
    torii_red, torii_black = torii(2060, 484)
    ground = 'M 0,500 L 0,470 C 400,462 800,476 1200,468 C 1600,460 2000,474 2400,466 L 2400,500 Z'
    mist = 'M 0,440 C 600,420 1200,452 1800,430 C 2100,420 2300,430 2400,426 L 2400,470 L 0,470 Z'

    land = [f'<Viewbox HorizontalAlignment="Stretch" VerticalAlignment="Bottom" Stretch="Uniform"><Canvas Width="{W}" Height="{H}">',
            path(far, '#FF2A2347', 0.6),
            grad_path(fuji, [(0, '#FF3A2D55'), (1, '#FF241B38')], 0.94),
            grad_path(cap, [(0, '#FFF6EEF8'), (1, '#FFCDBFDD')], 0.62),
            path(hills, '#FF1C152C', 0.97),
            path(' '.join(trees), '#FF3A1A33', 0.85),
            grad_path(mist, [(0, '#00FFFFFF'), (0.5, '#12FFFFFF'), (1, '#00FFFFFF')]),
            path(pagoda_body, '#FF0F0B17'),
            path(pagoda_windows, '#FFFFB45C', 0.75),
            path(torii_red, '#FFC4382C', 0.95),
            path(torii_black, '#FF17090D', 0.97),
            path(ground, '#FF0B0810'),
            '</Canvas></Viewbox>']
    return moon + ''.join(land)


# ---------------------------------------------------------------- Snow

def peaks(rnd, start_y, count, width, height_range, base_y):
    """A jagged mountain ridge and a snow cap on each peak."""
    ridge = [(0, base_y)]
    caps = []
    x = -60
    step = width / count
    for i in range(count + 2):
        px = x + step * rnd.uniform(0.85, 1.15)
        py = start_y - rnd.uniform(*height_range)
        vx = px + step * rnd.uniform(0.4, 0.6)
        vy = start_y - rnd.uniform(0, height_range[0] * 0.5)
        ridge += [(px - step * 0.25, (py + vy) / 2 + rnd.uniform(-10, 10)), (px, py), (vx, vy)]
        # The cap: from the peak down a little, with a ragged lower edge.
        depth = (vy - py) * rnd.uniform(0.32, 0.45)
        lx, rx = px - depth * 1.0, px + depth * 0.9
        edge = []
        for k in range(1, 6):
            t = k / 6
            ex = lx + (rx - lx) * t
            ey = py + depth + rnd.uniform(-depth * 0.35, depth * 0.1) - (abs(t - 0.5) * depth * 0.6)
            edge.append((ex, ey))
        caps.append(f'M {f(px)},{f(py)} L {f(rx)},{f(py + depth)} L ' + pts(list(reversed(edge))) + f' L {f(lx)},{f(py + depth)} Z')
        x = vx
    ridge += [(width + 60, base_y)]
    rough = [ridge[0]]
    for (x1, y1), (x2, y2) in zip(ridge[1:-2], ridge[2:-1]):
        for k in (1, 2):
            t = k / 3
            rough.append((x1 + (x2 - x1) * t + rnd.uniform(-6, 6), y1 + (y2 - y1) * t + rnd.uniform(-9, 9)))
        rough.append((x2, y2))
    rough.append(ridge[-1])
    return 'M ' + pts([ridge[1]] + rough[1:]) + ' L ' + pts([ridge[-1], ridge[0]]) + ' Z', ' '.join(caps)


def pine(cx, base, height, rnd):
    """A pine: three stacked tiers and a trunk; snow on each tier's shoulders."""
    tiers, snow = [], []
    width = height * 0.46
    for k in range(3):
        top = base - height + k * height * 0.24
        bottom = base - height * 0.18 + k * height * 0.02 - (2 - k) * height * 0.18
        w = width * (0.55 + k * 0.22)
        tiers.append(f'M {f(cx)},{f(top)} L {f(cx + w / 2)},{f(bottom)} L {f(cx - w / 2)},{f(bottom)} Z')
        sh = (bottom - top) * 0.32
        snow.append(f'M {f(cx)},{f(top)} L {f(cx + w * 0.2)},{f(top + sh)} L {f(cx + w * 0.08)},{f(top + sh * 0.8)} '
                    f'L {f(cx)},{f(top + sh * 1.1)} L {f(cx - w * 0.1)},{f(top + sh * 0.85)} L {f(cx - w * 0.22)},{f(top + sh)} Z')
    trunk = f'M {f(cx - 3)},{f(base)} L {f(cx - 3)},{f(base - height * 0.18)} L {f(cx + 3)},{f(base - height * 0.18)} L {f(cx + 3)},{f(base)} Z'
    return ' '.join(tiers) + ' ' + trunk, ' '.join(snow)


def snow():
    rnd = random.Random(11)
    # Stars in the top half.
    stars = {0.85: [], 0.55: [], 0.3: []}
    for _ in range(150):
        x, y = rnd.uniform(0, 2400), rnd.uniform(0, 520) ** 1.08
        r = rnd.choice([0.8, 1.1, 1.4, 1.9])
        stars[rnd.choice(list(stars))].append(circle(x, y, r))
    sky = ['<Viewbox HorizontalAlignment="Stretch" VerticalAlignment="Top" Stretch="UniformToFill" MaxHeight="900"><Canvas Width="2400" Height="900">']
    for alpha, figures in stars.items():
        sky.append(path(' '.join(figures), '#FFFFFFFF', alpha))
    sky.append('</Canvas></Viewbox>')

    moon = ('<Grid HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,40,220,0" Width="320" Height="320">'
            '<Ellipse><Ellipse.Fill><RadialGradientBrush>'
            '<GradientStop Offset="0" Color="#33D8ECFF" /><GradientStop Offset="0.5" Color="#10BFD9F2" /><GradientStop Offset="1" Color="#00BFD9F2" />'
            '</RadialGradientBrush></Ellipse.Fill></Ellipse>'
            '<Path Opacity="0.9" Fill="#FFEFF6FF" HorizontalAlignment="Center" VerticalAlignment="Center" '
            'Data="M 60,0 A 60,60 0 1 0 60,120 A 46,46 0 1 1 60,0 Z" />'
            '</Grid>')

    far, far_caps = peaks(rnd, 300, 9, 2400, (120, 220), 500)
    mid, mid_caps = peaks(rnd, 370, 12, 2400, (60, 130), 500)
    trees, tree_snow = [], []
    x = -20
    while x < 2440:
        h = rnd.uniform(70, 150)
        base = 470 + rnd.uniform(-6, 8)
        body, s = pine(x, base, h, rnd)
        trees.append(body)
        tree_snow.append(s)
        x += rnd.uniform(26, 58)
    back_drift = 'M 0,500 L 0,452 C 300,436 620,462 940,446 C 1260,430 1560,458 1880,440 C 2100,430 2300,446 2400,440 L 2400,500 Z'
    drift = 'M 0,500 L 0,470 C 320,452 640,484 960,466 C 1280,450 1580,480 1900,462 C 2120,452 2300,470 2400,462 L 2400,500 Z'
    land = ['<Viewbox HorizontalAlignment="Stretch" VerticalAlignment="Bottom" Stretch="Uniform"><Canvas Width="2400" Height="500">',
            grad_path(far, [(0, '#FF22395A'), (1, '#FF162A44')], 0.9),
            grad_path(far_caps, [(0, '#FFF4F9FF'), (1, '#FFBCD0E6')], 0.55),
            grad_path(mid, [(0, '#FF15283F'), (1, '#FF0E1D31')], 0.97),
            grad_path(mid_caps, [(0, '#FFEAF3FC'), (1, '#FFAFC6DD')], 0.42),
            path(' '.join(trees), '#FF081321'),
            path(' '.join(tree_snow), '#FFE6F0FA', 0.72),
            grad_path(back_drift, [(0, '#FFCFE0F0'), (1, '#FF9DB6CF')], 0.55),
            grad_path(drift, [(0, '#FFF2F7FC'), (1, '#FFC4D6E8')], 0.95),
            '</Canvas></Viewbox>']
    return ''.join(sky) + moon + ''.join(land)


xaml = f'''<?xml version="1.0" encoding="utf-8"?>
<!--
  The special looks' scenery, behind the panels (see SceneArt.xaml.cs).
  Drawn as XAML paths: written by a script from simple shapes (pagoda,
  torii, trees, peaks, pines), so it is crisp at any size and costs
  nothing once drawn. Only the scene in use is in the tree.
-->
<UserControl
    x:Class="Resonate.App.Controls.SceneArt"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    IsHitTestVisible="False">
    <Grid>
        <!-- Made only while its scene shows (x:Load), so the other looks pay nothing. -->
        <Grid x:Name="JapanArt" x:Load="False">{japan()}</Grid>
        <Grid x:Name="SnowArt" x:Load="False">{snow()}</Grid>
    </Grid>
</UserControl>
'''
open(OUT, 'w', encoding='utf-8', newline='\n').write(xaml)
print(len(xaml))
