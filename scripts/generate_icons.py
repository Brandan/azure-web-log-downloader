import math
import os
import struct
import zlib


def save_png(path, width, height, rgb_fn):
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # no filter
        for x in range(width):
            r, g, b = rgb_fn(x, y, width, height)
            raw.extend((max(0, min(255, int(r))), max(0, min(255, int(g))), max(0, min(255, int(b)))))

    def chunk(chunk_type, data):
        crc = zlib.crc32(chunk_type)
        crc = zlib.crc32(data, crc) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    idat = zlib.compress(bytes(raw), level=9)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", ihdr))
        f.write(chunk(b"IDAT", idat))
        f.write(chunk(b"IEND", b""))


def rounded_rect_mask(x, y, w, h, radius):
    cx = min(max(x, radius), w - radius - 1)
    cy = min(max(y, radius), h - radius - 1)
    dx = x - cx
    dy = y - cy
    return (dx * dx + dy * dy) <= radius * radius


def icon_variant_1(x, y, w, h):
    if not rounded_rect_mask(x, y, w, h, 180):
        return (0, 0, 0)
    t = y / (h - 1)
    r = 20 + 10 * (1 - t)
    g = 85 + 60 * (1 - t)
    b = 180 + 55 * (1 - t)

    # Cloud shape
    for cx, cy, rad in [(390, 470, 95), (510, 430, 120), (635, 475, 95), (515, 515, 135)]:
        if (x - cx) ** 2 + (y - cy) ** 2 < rad ** 2:
            r, g, b = 230, 244, 255

    # Arrow down
    if 500 <= x <= 540 and 390 <= y <= 650:
        r, g, b = 30, 120, 220
    if 430 <= y <= 530 and abs(x - 520) <= (y - 430):
        r, g, b = 30, 120, 220

    return (r, g, b)


def icon_variant_2(x, y, w, h):
    if not rounded_rect_mask(x, y, w, h, 180):
        return (0, 0, 0)
    t = y / (h - 1)
    r = 12 + 15 * (1 - t)
    g = 30 + 35 * (1 - t)
    b = 68 + 70 * (1 - t)

    # Storage box
    if 230 <= x <= 790 and 260 <= y <= 700:
        r, g, b = 45, 95, 170
    if 230 <= x <= 790 and 260 <= y <= 340:
        r, g, b = 65, 130, 215

    # Log lines
    if 320 <= x <= 700:
        for yy in [430, 490, 550, 610]:
            if yy <= y <= yy + 12:
                r, g, b = 200, 240, 255

    # Download line
    if 760 <= x <= 860 and 350 <= y <= 640:
        r, g, b = 120, 240, 255
    if 620 <= y <= 760 and abs(x - 810) <= (y - 620):
        r, g, b = 120, 240, 255

    return (r, g, b)


def icon_variant_3(x, y, w, h):
    if not rounded_rect_mask(x, y, w, h, 180):
        return (0, 0, 0)
    t = y / (h - 1)
    r = 45 + 25 * (1 - t)
    g = 95 + 65 * (1 - t)
    b = 180 + 60 * (1 - t)

    # Clock ring
    dx = x - 512
    dy = y - 512
    d = math.sqrt(dx * dx + dy * dy)
    if 300 <= d <= 340:
        r, g, b = 220, 245, 255

    # Clock hands
    if 500 <= x <= 524 and 380 <= y <= 530:
        r, g, b = 220, 245, 255
    if 512 <= x <= 660 and 500 <= y <= 524:
        r, g, b = 220, 245, 255

    # Center cloud
    for cx, cy, rad in [(430, 560, 70), (510, 530, 95), (595, 560, 70), (510, 600, 95)]:
        if (x - cx) ** 2 + (y - cy) ** 2 < rad ** 2:
            r, g, b = 235, 248, 255

    return (r, g, b)


if __name__ == "__main__":
    root = "/Users/brandan/Sync/default/Projects/AzureWebLogDownloaderCli/assets/icons/examples"
    os.makedirs(root, exist_ok=True)
    save_png(os.path.join(root, "icon-example-1-1024.png"), 1024, 1024, icon_variant_1)
    save_png(os.path.join(root, "icon-example-2-1024.png"), 1024, 1024, icon_variant_2)
    save_png(os.path.join(root, "icon-example-3-1024.png"), 1024, 1024, icon_variant_3)
