"""生成 TaskbarTimer 定稿图标：石墨灰圆角方块 + 白表盘细线时钟（方案 A），输出多尺寸 .ico。"""
import math
from PIL import Image, ImageDraw

S = 256
CX = CY = S / 2
TOP = (55, 65, 81)      # #374151 石墨灰（顶）
BOTTOM = (31, 41, 55)   # #1F2937 石墨灰（底）
DARK = (31, 41, 55)     # 指针/刻度
WHITE = (255, 255, 255, 255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# 背景：圆角方块 + 垂直渐变
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=56, fill=255)
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    c = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)) + (255,)
    gd.line([(0, y), (S, y)], fill=c)
img.paste(grad, (0, 0), mask)

d = ImageDraw.Draw(img)

# 白色表盘
R = 80
d.ellipse([CX - R, CY - R, CX + R, CY + R], fill=WHITE)

# 主刻度（12/3/6/9，细）
for ang in (0, 90, 180, 270):
    a = math.radians(ang)
    ix, iy = math.sin(a), -math.cos(a)
    x1, y1 = CX + ix * (R - 10), CY + iy * (R - 10)
    x2, y2 = CX + ix * (R - 26), CY + iy * (R - 26)
    d.line([(x1, y1), (x2, y2)], fill=DARK, width=5)

# 次刻度（细）
for ang in range(0, 360, 30):
    if ang % 90 == 0:
        continue
    a = math.radians(ang)
    ix, iy = math.sin(a), -math.cos(a)
    x1, y1 = CX + ix * (R - 12), CY + iy * (R - 12)
    x2, y2 = CX + ix * (R - 18), CY + iy * (R - 18)
    d.line([(x1, y1), (x2, y2)], fill=DARK, width=2)

def hand(angle_deg, length, width):
    a = math.radians(angle_deg)
    ex, ey = CX + math.sin(a) * length, CY - math.cos(a) * length
    d.line([(CX, CY), (ex, ey)], fill=DARK, width=width)
    r = width / 2
    d.ellipse([ex - r, ey - r, ex + r, ey + r], fill=DARK)
    d.ellipse([CX - r, CY - r, CX + r, CY + r], fill=DARK)

hand(-60, 48, 8)   # 时针
hand(60, 68, 5)    # 分针

# 中心轴点（石墨灰）
d.ellipse([CX - 6, CY - 6, CX + 6, CY + 6], fill=TOP)

out_ico = r"D:\wb\demo\TaskbarTimer\app.ico"
img.save(out_ico, sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
img.save(r"D:\wb\demo\TaskbarTimer\tools\icon_preview.png")
print("saved:", out_ico)
