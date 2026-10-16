"""Erzeugt src/HasiZip.App/Assets/hasizip.ico (16–256 px). Aufruf: python3 scripts/make-icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw

NAVY = (12, 25, 41, 255)
BLUE = (77, 188, 232, 255)
BLUE_DARK = (47, 159, 204, 255)
WHITE = (255, 255, 255, 255)


def draw(size: int) -> Image.Image:
    s = 1024  # groß zeichnen, dann verkleinern (sauberes Antialiasing)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([40, 40, s - 40, s - 40], radius=210, fill=NAVY)
    # Paket/Archiv
    d.rounded_rectangle([210, 250, s - 210, s - 190], radius=70, fill=BLUE)
    d.rounded_rectangle([180, 200, s - 180, 360], radius=60, fill=BLUE_DARK)
    # Reißverschluss in der Mitte
    cx = s // 2
    tooth_w, tooth_h, gap = 64, 34, 18
    y = 380
    left = True
    while y < s - 330:
        x0 = cx - tooth_w if left else cx
        d.rounded_rectangle([x0, y, x0 + tooth_w, y + tooth_h], radius=10, fill=WHITE)
        y += tooth_h + gap
        left = not left
    # Zieher
    d.rounded_rectangle([cx - 70, s - 330, cx + 70, s - 230], radius=30, fill=WHITE)
    d.rounded_rectangle([cx - 28, s - 300, cx + 28, s - 258], radius=14, fill=BLUE)
    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    out = Path(__file__).resolve().parent.parent / "src" / "HasiZip.App" / "Assets" / "hasizip.ico"
    out.parent.mkdir(parents=True, exist_ok=True)
    sizes = [16, 24, 32, 48, 64, 128, 256]
    big = draw(256)
    big.save(out, format="ICO", sizes=[(n, n) for n in sizes])
    draw(512).save(out.with_suffix(".png"))
    print(out)


if __name__ == "__main__":
    main()
