"""Встроенные пейзажи GigaRest — рисуются программно, без чужих прав (CC0).
Запуск: python3 tools-src/scenes/make_scenes.py assets/scenes"""
import math, random, sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter

W, H = 1920, 1080

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def sky(colors):
    """Вертикальный градиент по списку (позиция, цвет)."""
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    for y in range(H):
        t = y / (H - 1)
        for (p0, c0), (p1, c1) in zip(colors, colors[1:]):
            if p0 <= t <= p1:
                d.line([(0, y), (W, y)], fill=lerp(c0, c1, (t - p0) / max(p1 - p0, 1e-6)))
                break
    return img

def glow(img, x, y, r, color, strength):
    """Мягкое свечение — «экраном», без тёмного ореола."""
    layer = Image.new("RGB", (W, H), (0, 0, 0))
    c = tuple(int(v * strength) for v in color)
    ImageDraw.Draw(layer).ellipse([x - r * 2, y - r * 2, x + r * 2, y + r * 2], fill=tuple(v // 3 for v in c))
    ImageDraw.Draw(layer).ellipse([x - r, y - r, x + r, y + r], fill=c)
    layer = layer.filter(ImageFilter.GaussianBlur(r))
    return ImageChops.screen(img, layer)

def ridge(rnd, base, amp, roughness, seed_shift=0):
    pts, y = [], base
    for x in range(0, W + 40, 40):
        y += rnd.uniform(-roughness, roughness)
        y = max(base - amp, min(base + amp, y))
        pts.append((x, y + math.sin((x + seed_shift) / 260) * amp * 0.4))
    return pts

def fill_ridge(img, pts, color, blur=0):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(layer).polygon(pts + [(W, H), (0, H)], fill=color + (255,))
    if blur: layer = layer.filter(ImageFilter.GaussianBlur(blur))
    img.paste(layer, (0, 0), layer)

def stars(img, rnd, count, top=0.6):
    d = ImageDraw.Draw(img)
    for _ in range(count):
        x, y = rnd.randrange(W), rnd.randrange(int(H * top))
        v = rnd.randint(150, 255)
        d.point((x, y), fill=(v, v, min(255, v + 10)))

def lake():
    rnd = random.Random(7)
    img = sky([(0, (10, 14, 38)), (0.45, (32, 40, 86)), (0.55, (52, 58, 110)), (1, (8, 10, 24))])
    stars(img, rnd, 900, 0.5)
    img = glow(img, 1350, 260, 70, (255, 244, 214), 1.0)
    ImageDraw.Draw(img).ellipse([1310, 220, 1390, 300], fill=(250, 244, 226))
    for i, c in enumerate([(30, 36, 70), (20, 24, 50), (12, 14, 32)]):
        fill_ridge(img, ridge(rnd, 560 + i * 30, 50 - i * 10, 18, i * 300), c)
    # вода и лунная дорожка: короткие блики, к берегу шире и тусклее
    water = Image.new("RGB", (W, H - 610), (12, 16, 38))
    shine = Image.new("RGB", (W, H - 610), (0, 0, 0))
    d = ImageDraw.Draw(shine)
    for _ in range(900):
        y = int(rnd.random() ** 1.4 * (H - 610))
        spread = 30 + y * 0.8
        x = 1350 + rnd.gauss(0, spread / 2.2)
        w = rnd.randint(8, 40) + y // 12
        v = int(max(0, 230 - y * 0.45) * rnd.uniform(0.5, 1))
        d.line([(x - w / 2, y), (x + w / 2, y)], fill=(v, v, int(v * 0.85)), width=2)
    shine = shine.filter(ImageFilter.GaussianBlur(1.2))
    img.paste(ImageChops.screen(water, shine), (0, 610))
    fill_ridge(img, [(0, 1000), (300, 960), (700, 990), (1100, 970), (1500, 1000), (1920, 980)], (6, 7, 16))
    return img

def winter():
    rnd = random.Random(11)
    img = sky([(0, (36, 52, 98)), (0.5, (110, 132, 180)), (0.62, (196, 186, 206)), (1, (210, 220, 236))])
    img = glow(img, 600, 600, 160, (255, 214, 190), 0.8)
    layers = [(176, 188, 214), (150, 166, 200), (196, 206, 226), (226, 232, 244)]
    for i, c in enumerate(layers):
        pts = ridge(rnd, 600 + i * 90, 60, 22, i * 500)
        fill_ridge(img, pts, c, blur=1)
        if i == 1:  # ёлки на дальнем склоне — между дальними и ближними холмами
            d = ImageDraw.Draw(img)
            for _ in range(90):
                x = rnd.randrange(W)
                ground = min(p[1] for p in pts if abs(p[0] - x) < 40) + rnd.randint(5, 60)
                h = rnd.randint(26, 70)
                d.polygon([(x, ground - h), (x - h // 3, ground), (x + h // 3, ground)], fill=(64, 80, 112))
    return img

def dunes():
    rnd = random.Random(5)
    img = sky([(0, (64, 44, 92)), (0.4, (214, 120, 106)), (0.58, (250, 186, 128)), (1, (120, 64, 52))])
    img = glow(img, 1200, 560, 120, (255, 226, 170), 1.0)
    ImageDraw.Draw(img).ellipse([1150, 510, 1250, 610], fill=(255, 236, 196))
    for i, c in enumerate([(196, 112, 82), (170, 90, 66), (140, 70, 54), (104, 50, 42)]):
        pts = [(x, 640 + i * 110 + math.sin(x / (300 + i * 80) + i) * (40 + i * 10)) for x in range(0, W + 20, 20)]
        fill_ridge(img, pts, c, blur=1)
    return img

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    for name, fn in [("lake", lake), ("winter", winter), ("dunes", dunes)]:
        fn().filter(ImageFilter.GaussianBlur(0.6)).save(f"{out}/{name}.jpg", quality=88)
        print("ok", name)
