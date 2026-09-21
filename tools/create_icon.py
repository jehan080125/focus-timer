"""Draw a minimal iOS-inspired countdown icon (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw
import math

root = Path(__file__).resolve().parents[1] / "FocusTimer" / "Assets"
root.mkdir(parents=True, exist_ok=True)
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)
def box(bounds):
    return tuple(round(v * scale) for v in bounds)

# Continuous squircle corners, with a restrained blue gradient.
mask = Image.new("L", image.size)
outline = []
for step in range(1440):
    angle = step * math.tau / 1440
    c, s = math.cos(angle), math.sin(angle)
    outline.append(box((128 + 120 * math.copysign(abs(c) ** .48, c),
                        128 + 120 * math.copysign(abs(s) ** .48, s))))
ImageDraw.Draw(mask).polygon(outline, fill=255)
for y in range(image.height):
    t = y / (image.height - 1)
    color = tuple(round(a * (1-t) + b * t) for a, b in zip((62, 169, 255), (12, 94, 233)))
    draw.line((0, y, image.width, y), fill=(*color, 255))
image.putalpha(mask)
draw = ImageDraw.Draw(image)

# A single open countdown ring and hand, with rounded ends and no ornament.
cx, cy, radius, stroke = 128, 130, 65, 13
draw.arc(box((cx-radius, cy-radius, cx+radius, cy+radius)),
         start=-90, end=210, fill="white", width=stroke * scale)
for angle in (-90, 210):
    a = math.radians(angle)
    x, y = cx + (radius-stroke/2)*math.cos(a), cy + (radius-stroke/2)*math.sin(a)
    draw.ellipse(box((x-stroke/2, y-stroke/2, x+stroke/2, y+stroke/2)), fill="white")
draw.line([box((cx, cy)), box((151, 102))], fill="white", width=12 * scale)
for x, y in ((cx, cy), (151, 102)):
    draw.ellipse(box((x-6, y-6, x+6, y+6)), fill="white")

image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(root / "Timer.png")
image.save(root / "Timer.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
