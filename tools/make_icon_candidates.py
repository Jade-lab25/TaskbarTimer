"""生成多个图标候选预览（不写 app.ico，等用户选定后再定稿）。"""
import math
from PIL import Image, ImageDraw, ImageFont

S = 256
CX = CY = S / 2

def rounded_rect_bg(size, radius, top, bottom):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    grad = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(grad)
    for y in range(size):
        t = y / (size - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (size, y)], fill=c)
    img.paste(grad, (0, 0), mask)
    return img

def draw_clock(top, bottom, dark, size=S):
    img = rounded_rect_bg(size, 56, top, bottom)
    d = ImageDraw.Draw(img)
    R = 80
    d.ellipse([CX - R, CY - R, CX + R, CY + R], fill=(255, 255, 255, 255))
    # 主刻度（细）
    tw, tl = 5, 16
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        ix, iy = math.sin(a), -math.cos(a)
        x1, y1 = CX + ix * (R - 10), CY + iy * (R - 10)
        x2, y2 = CX + ix * (R - 10 - tl), CY + iy * (R - 10 - tl)
        d.line([(x1, y1), (x2, y2)], fill=dark, width=tw)
    # 次刻度（细点，12 个，除主刻度）
    for ang in range(0, 360, 30):
        if ang % 90 == 0:
            continue
        a = math.radians(ang)
        ix, iy = math.sin(a), -math.cos(a)
        x1, y1 = CX + ix * (R - 12), CY + iy * (R - 12)
        x2, y2 = CX + ix * (R - 18), CY + iy * (R - 18)
        d.line([(x1, y1), (x2, y2)], fill=dark, width=2)
    def hand(angle_deg, length, width):
        a = math.radians(angle_deg)
        ex, ey = CX + math.sin(a) * length, CY - math.cos(a) * length
        d.line([(CX, CY), (ex, ey)], fill=dark, width=width)
        r = width / 2
        d.ellipse([ex - r, ey - r, ex + r, ey + r], fill=dark)
        d.ellipse([CX - r, CY - r, CX + r, CY + r], fill=dark)
    hand(-60, 48, 8)   # 时针（细）
    hand(60, 68, 5)    # 分针（细）
    d.ellipse([CX - 6, CY - 6, CX + 6, CY + 6], fill=top)  # 中心轴点用主题色
    return img

def draw_hourglass(top, bottom, dark, size=S):
    img = rounded_rect_bg(size, 56, top, bottom)
    d = ImageDraw.Draw(img)
    # 沙漏外框：上下对称，细腰
    w, h_glass, neck = 104, 96, 18
    up = [(CX - w/2, CY - h_glass), (CX + w/2, CY - h_glass), (CX + neck/2, CY), (CX - neck/2, CY)]
    low = [(CX - neck/2, CY), (CX + neck/2, CY), (CX + w/2, CY + h_glass), (CX - w/2, CY + h_glass)]
    d.polygon(up, outline=(255, 255, 255, 255), width=6)
    d.polygon(low, outline=(255, 255, 255, 255), width=6)
    # 沙子（上半瓶填充一部分）
    sand_h = 34
    d.polygon([(CX - (w/2 - 8), CY - h_glass + 6),
               (CX + (w/2 - 8), CY - h_glass + 6),
               (CX + (neck/2 - 3), CY - h_glass + 6 + sand_h),
               (CX - (neck/2 - 3), CY - h_glass + 6 + sand_h)], fill=(255, 255, 255, 255))
    # 顶部/底部横梁
    for dy in (-h_glass, h_glass):
        d.rounded_rectangle([CX - w/2 - 4, CY + dy - 5, CX + w/2 + 4, CY + dy + 5], radius=4, fill=(255, 255, 255, 255))
    # 中腰细缝
    d.line([(CX - neck/2, CY), (CX + neck/2, CY)], fill=(255, 255, 255, 255), width=6)
    return img

CANDIDATES = {
    "A": ("石墨灰", (55, 65, 81), (31, 41, 55), (31, 41, 55), "clock"),
    "B": ("墨绿", (15, 118, 110), (17, 94, 89), (31, 41, 55), "clock"),
    "C": ("琥珀橙", (234, 88, 12), (194, 65, 12), (31, 41, 55), "clock"),
    "D": ("炭黑", (17, 24, 39), (3, 7, 18), (17, 24, 39), "clock"),
    "E": ("墨绿·沙漏", (15, 118, 110), (17, 94, 89), (31, 41, 55), "hourglass"),
}

try:
    font = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 52)
except Exception:
    font = ImageFont.load_default()

images = []
for key, (name, top, bottom, dark, kind) in CANDIDATES.items():
    img = draw_clock(top, bottom, dark) if kind == "clock" else draw_hourglass(top, bottom, dark)
    d = ImageDraw.Draw(img)
    d.text((14, 10), key, font=font, fill=(255, 255, 255, 255))
    images.append((key, name, img))
    img.save(rf"D:\wb\demo\TaskbarTimer\tools\icon_candidate_{key}.png")

# 拼接预览
gap = 16
cell = S
w = cell * len(images) + gap * (len(images) - 1)
h = cell
canvas = Image.new("RGB", (w, h), (255, 255, 255))
x = 0
for key, name, img in images:
    canvas.paste(img, (x, 0), img)
    x += cell + gap
canvas.save(r"D:\wb\demo\TaskbarTimer\tools\icon_candidates_preview.png")
print("done:", ", ".join(f"{k}={n}" for k, n, _ in images))
