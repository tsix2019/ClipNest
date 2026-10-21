"""Generates src/ClipNest.App/Assets/ClipNest.ico (multi-size, PNG-compressed) and Logo.png.

Design: three clipboard cards fanned out of one point, like a nest, on a flat indigo squircle. The card headers
use the same idea as the app (each card is tinted by its source). Small sizes get a simplified single card
so the tray and taskbar icons stay crisp.

Usage: python tools/make-icon.py   (needs Pillow)
"""
import io
import math
import struct
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "ClipNest.App" / "Assets"

INDIGO = (91, 69, 224)
AMBER = (255, 176, 32)
TEAL = (45, 212, 191)
PINK = (244, 114, 182)
LINE = (148, 156, 184)


def squircle_mask(size, inset, n=5.0):
    """Superellipse |x|^n + |y|^n = 1, the iOS/Windows 11 'squircle'."""
    mask = Image.new("L", (size, size), 0)
    r = size / 2 - inset
    c = size / 2
    pts = []
    for i in range(720):
        t = 2 * math.pi * i / 720
        ct, st = math.cos(t), math.sin(t)
        x = c + r * math.copysign(abs(ct) ** (2 / n), ct)
        y = c + r * math.copysign(abs(st) ** (2 / n), st)
        pts.append((x, y))
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    return mask


def background(size):
    mask = squircle_mask(size, size * 0.035)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(Image.new("RGBA", (size, size), INDIGO + (255,)), (0, 0), mask)
    return out, mask


def card(size, w, h, header, lines):
    """One card on a transparent layer of the full icon size, centered at the origin of its own box."""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x0, y0 = (size - w) / 2, (size - h) / 2
    r = w * 0.14
    d.rounded_rectangle((x0, y0, x0 + w, y0 + h), radius=r, fill=(255, 255, 255, 255))
    # Header band, clipped to the card's rounded top.
    band = Image.new("L", (size, size), 0)
    ImageDraw.Draw(band).rectangle((x0, y0, x0 + w, y0 + h * 0.27), fill=255)
    card_mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(card_mask).rounded_rectangle((x0, y0, x0 + w, y0 + h), radius=r, fill=255)
    band = ImageChops.multiply(band, card_mask)
    layer.paste(Image.new("RGBA", (size, size), header + (255,)), (0, 0), band)
    if lines:
        lh = h * 0.06
        for i, frac in enumerate((0.74, 0.74, 0.48)):
            ly = y0 + h * (0.40 + i * 0.15)
            d.rounded_rectangle((x0 + w * 0.13, ly, x0 + w * (0.13 + frac), ly + lh), radius=lh / 2, fill=LINE + (255,))
    return layer


def with_shadow(layer, size, blur, offset, alpha):
    a = layer.getchannel("A").point(lambda v: v * alpha // 255)
    shadow = Image.new("RGBA", (size, size), (16, 8, 60, 0))
    shadow.putalpha(a)
    shadow = shadow.filter(ImageFilter.GaussianBlur(blur))
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(shadow, (0, int(offset)))
    out.alpha_composite(layer)
    return out


def place(layer, size, angle, pivot, lift):
    """Rotates a centered card about a pivot below it (the 'nest'), then lifts it."""
    moved = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    moved.alpha_composite(layer, (0, int(-lift)))
    return moved.rotate(angle, resample=Image.BICUBIC, center=pivot)


def render(size):
    """Renders at 2x and downsamples for clean anti-aliasing."""
    s = size * 2
    bg, mask = background(s)
    icon = bg.copy()
    detailed = size >= 32

    if detailed:
        w, h = s * 0.34, s * 0.45
        pivot = (s / 2, s * 1.02)
        lift = s * 0.03
        for angle, header in ((16, TEAL), (-16, PINK)):
            c = card(s, w, h, header, lines=False)
            c = with_shadow(c, s, s * 0.015, s * 0.008, 90)
            icon.alpha_composite(place(c, s, angle, pivot, lift))
        front = card(s, w, h, AMBER, lines=size >= 48)
        front = with_shadow(front, s, s * 0.03, s * 0.02, 120)
        icon.alpha_composite(place(front, s, 0, pivot, lift))
    else:
        # One bold card: legible at 16-24 px.
        w, h = s * 0.56, s * 0.64
        c = card(s, w, h, AMBER, lines=False)
        icon.alpha_composite(c)

    # Keep everything inside the squircle.
    alpha = ImageChops.multiply(icon.getchannel("A"), mask)
    icon.putalpha(alpha)
    return icon.resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """ICO with PNG-compressed entries (supported since Windows Vista)."""
    blobs = []
    for im in images:
        buf = io.BytesIO()
        im.save(buf, "PNG", optimize=True)
        blobs.append(buf.getvalue())
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for im, blob in zip(images, blobs):
        w, h = im.size
        entries += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    path.write_bytes(header + entries + b"".join(blobs))


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    images = [render(s) for s in sizes]
    write_ico(ASSETS / "ClipNest.ico", images)
    render(256).save(ASSETS / "Logo.png", optimize=True)
    preview = Image.new("RGBA", (sum(sizes) + 10 * len(sizes), 256), (0, 0, 0, 0))
    x = 0
    for im in images:
        preview.alpha_composite(im, (x, 256 - im.size[1]))
        x += im.size[0] + 10
    return preview


if __name__ == "__main__":
    main()
    print("wrote", ASSETS / "ClipNest.ico", "and", ASSETS / "Logo.png")
