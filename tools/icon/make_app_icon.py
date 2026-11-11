"""Build the app icon from the supplied artwork: full art at large sizes, a legible
"P" mark at 16-32px, plus a thin outline so it reads on light and dark taskbars."""
from PIL import Image, ImageDraw, ImageFont
import os

SRC = r"D:\2 new\docs\icon_src\source.webp"
OUT = r"D:\2 new\docs\icons"
os.makedirs(OUT, exist_ok=True)

BLUE_TOP = (86, 138, 224)
BLUE_BOT = (58, 96, 178)
OUTLINE = (74, 123, 208)


def squircle(size, radius_ratio=0.225):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1],
                                           radius=int(size * radius_ratio), fill=255)
    return mask


def art(size, outline=True):
    """Downscaled artwork, optionally with a hairline outline."""
    src = Image.open(SRC).convert("RGBA")
    img = src.resize((size, size), Image.LANCZOS)
    img.putalpha(Image.composite(img.getchannel("A"), Image.new("L", (size, size), 0), squircle(size)))
    if outline:
        w = max(1, int(size * 0.035))
        d = ImageDraw.Draw(img)
        d.rounded_rectangle([w / 2, w / 2, size - 1 - w / 2, size - 1 - w / 2],
                            radius=int(size * 0.225), outline=OUTLINE, width=w)
    return img


def mark(size):
    """Simplified mark for 16-32px: blue squircle + white P."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    grad = Image.new("RGB", (1, size))
    px = grad.load()
    for y in range(size):
        t = y / max(1, size - 1)
        px[0, y] = tuple(int(BLUE_TOP[i] + (BLUE_BOT[i] - BLUE_TOP[i]) * t) for i in range(3))
    grad = grad.resize((size, size)).convert("RGBA")
    mask = squircle(size, 0.26)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    font = None
    for candidate in (
        r"C:\Windows\Fonts\segoeuib.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
    ):
        if os.path.exists(candidate):
            font = ImageFont.truetype(candidate, int(size * 0.72))
            break
    if font is not None:
        box = d.textbbox((0, 0), "P", font=font)
        tx = (size - (box[2] - box[0])) / 2 - box[0]
        ty = (size - (box[3] - box[1])) / 2 - box[1] - size * 0.02
        d.text((tx, ty), "P", font=font, fill=(255, 255, 255, 255))
    return img


SIZES = [256, 128, 64, 48, 32, 24, 16]
frames = []
for px in SIZES:
    image = art(px) if px >= 48 else mark(px)
    frames.append(image)
    image.save(os.path.join(OUT, f"pixivShelf_{px}.png"))

# multi-size ico (Pillow keeps the per-size frames we hand it)
ico_path = r"D:\2 new\docs\icon_src\app.ico"
frames[0].save(ico_path, format="ICO", sizes=[(s, s) for s in SIZES])

# comparison sheet
sheet = Image.new("RGB", (880, 300), (28, 30, 36))
for i, (px, img) in enumerate(zip(SIZES, frames)):
    big = img.resize((110, 110), Image.LANCZOS) if px >= 48 else img
    x = 30 + i * 118
    sheet.paste(big, (x, 40), big)
    for k, bg in enumerate([(28, 30, 36), (243, 243, 243)]):
        canvas = Image.new("RGB", img.size, bg)
        canvas.paste(img, (0, 0), img)
        sheet.paste(canvas, (x + 6, 175 + k * 46))
sheet.save(os.path.join(OUT, "pixivShelf_sheet.png"))
print("ico bytes:", os.path.getsize(ico_path))
