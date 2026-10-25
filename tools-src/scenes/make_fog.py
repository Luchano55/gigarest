"""Текстура тумана для эффекта «Туман» — бесшовная по горизонтали, своя (CC0).
Запуск: python3 tools-src/scenes/make_fog.py assets/effects/fog.png"""
import random, sys
from PIL import Image, ImageFilter, ImageChops

W, H = 2048, 512
rnd = random.Random(3)

def noise(cell):
    """Сглаженный шум: случайная сетка, растянутая и размытая; по горизонтали замыкаем в кольцо."""
    gw, gh = W // cell, H // cell + 2
    small = Image.new("L", (gw, gh))
    small.putdata([rnd.randint(0, 255) for _ in range(gw * gh)])
    wide = Image.new("L", (gw * 3, gh))
    for i in range(3): wide.paste(small, (i * gw, 0))
    big = wide.resize((W * 3, (H // cell + 2) * cell), Image.BICUBIC).filter(ImageFilter.GaussianBlur(cell / 2))
    return big.crop((W, cell, W * 2, cell + H))

base = Image.new("L", (W, H), 0)
for cell, weight in [(256, 0.55), (128, 0.3), (48, 0.15)]:
    n = noise(cell).point(lambda v, w=weight: int(v * w))
    base = ImageChops.add(base, n)
# Плотнее внизу, прозрачно вверху и по краям по высоте.
mask = Image.linear_gradient("L").resize((W, H))
alpha = ImageChops.multiply(base, mask).point(lambda v: max(0, min(255, int((v - 40) * 1.6))))
alpha = alpha.filter(ImageFilter.GaussianBlur(6))
fog = Image.merge("RGBA", (Image.new("L", (W, H), 224), Image.new("L", (W, H), 228), Image.new("L", (W, H), 240), alpha))
fog.save(sys.argv[1] if len(sys.argv) > 1 else "fog.png", optimize=True)
print("ok", fog.size)
