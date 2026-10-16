"""Genera el fondo japonés de KizaTweaks (negro + sakura, siluetas originales muy difuminadas).
Uso: python3 tools/make_background.py [carpeta_salida]   (requiere Pillow y una fuente CJK opcional)"""
import math, random, sys, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1200
OUT = sys.argv[1] if len(sys.argv) > 1 else "src/KizaTweaks.App/Assets"
rnd = random.Random(7)

def layer():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))

def comp(base, lyr, blur=0):
    if blur: lyr = lyr.filter(ImageFilter.GaussianBlur(blur))
    return Image.alpha_composite(base, lyr)

# ---- base: negro cálido con un leve degradado
base = Image.new("RGBA", (W, H))
px = base.load()
for y in range(H):
    for x in range(0, W):
        t = y / H
        r = int(9 + 8 * t); g = int(6 + 3 * t); b = int(9 + 6 * t)
        px[x, y] = (r, g, b, 255)

# ---- luna y halo
l = layer(); d = ImageDraw.Draw(l)
mx, my, mr = 1480, 250, 190
for i in range(26, 0, -1):
    rr = mr + i * 18
    d.ellipse([mx - rr, my - rr, mx + rr, my + rr], fill=(240, 120, 165, 4))
base = comp(base, l, 30)
l = layer(); d = ImageDraw.Draw(l)
d.ellipse([mx - mr, my - mr, mx + mr, my + mr], fill=(255, 205, 222, 34))
base = comp(base, l, 4)

# ---- Fuji lejano (laderas cóncavas, muy tenue)
l = layer(); d = ImageDraw.Draw(l)
pts = []
for i in range(0, 101):
    t = i / 100
    x = -150 + t * 1250
    peak = 470
    dx = abs(x - peak) / 625
    y = 790 + 420 * (dx ** 1.6)
    if abs(x - peak) < 55: y = 790
    pts.append((x, y))
d.polygon(pts + [(1100, H), (-150, H)], fill=(110, 45, 75, 26))
d.polygon([(415, 790), (525, 790), (560, 830), (520, 815), (490, 840), (455, 812), (420, 835), (385, 828)], fill=(255, 215, 232, 22))
base = comp(base, l, 4)

# ---- kanji 桜 vertical, enorme y casi invisible
font_path = None
for p in ["/usr/share/fonts/opentype/noto/NotoSerifCJK-Bold.ttc", "/usr/share/fonts/opentype/noto/NotoSansCJK-Black.ttc"]:
    if os.path.exists(p): font_path = p; break
if font_path:
    l = layer(); d = ImageDraw.Draw(l)
    f = ImageFont.truetype(font_path, 520)
    d.text((1060, 560), "桜", font=f, fill=(255, 140, 185, 14))
    base = comp(base, l, 2)

# ---- siluetas originales de estilo anime (no son personajes existentes), muy difuminadas
def spline(points, n=12):
    """Curva cerrada suave (Catmull-Rom) por los puntos dados."""
    out = []
    m = len(points)
    for i in range(m):
        p0, p1, p2, p3 = points[i - 1], points[i], points[(i + 1) % m], points[(i + 2) % m]
        for k in range(n):
            t = k / n; t2 = t * t; t3 = t2 * t
            out.append(tuple(0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    return out

def place(points, ox, oy, s):
    return [(ox + x * s, oy + y * s) for x, y in points]

def girl(d, ox, oy, s, col):
    P = lambda pts: place(pts, ox, oy, s)
    # pelo largo al viento (detrás)
    d.polygon(P(spline([(140, 110), (150, 58), (200, 44), (258, 66), (275, 130), (305, 215), (352, 310), (420, 405),
                        (392, 420), (330, 372), (282, 330), (258, 360), (232, 300), (160, 330), (128, 250), (132, 160)])), fill=col)
    # cabeza, mechón rebelde y lazo
    d.ellipse(P([(140, 52), (260, 188)]), fill=col)
    d.polygon(P(spline([(196, 50), (214, 14), (238, 6), (222, 24), (212, 54)], 6)), fill=col)
    d.polygon(P([(150, 78), (102, 46), (110, 104)]), fill=col)
    d.polygon(P([(150, 78), (120, 120), (166, 112)]), fill=col)
    # cuello, blusa de marinera y brazos
    d.polygon(P([(185, 180), (215, 180), (218, 222), (182, 222)]), fill=col)
    d.polygon(P(spline([(132, 236), (200, 214), (268, 236), (258, 330), (248, 420), (152, 420), (142, 330)], 8)), fill=col)
    d.polygon(P([(134, 240), (112, 330), (106, 440), (124, 444), (140, 340)]), fill=col)
    d.polygon(P([(266, 240), (290, 330), (296, 440), (278, 444), (260, 340)]), fill=col)
    # falda con pliegues
    hem = [(108, 540), (130, 528), (150, 545), (172, 530), (194, 547), (216, 531), (238, 546), (260, 530), (282, 544), (300, 532)]
    d.polygon(P([(150, 410), (250, 410)] + hem[::-1]), fill=col)
    # piernas y zapatos
    d.polygon(P([(160, 530), (186, 530), (182, 770), (166, 770)]), fill=col)
    d.polygon(P([(222, 530), (248, 530), (246, 770), (230, 770)]), fill=col)
    d.ellipse(P([(150, 760), (194, 788)]), fill=col)
    d.ellipse(P([(222, 760), (266, 788)]), fill=col)

def swordsman(d, ox, oy, s, col):
    P = lambda pts: place(pts, ox, oy, s)
    # pelo en puntas
    spikes = []
    for i in range(15):
        a = math.pi * (0.95 + i / 14 * 1.1)
        r = 112 if i % 2 == 0 else 60
        spikes.append((200 + math.cos(a) * r, 140 + math.sin(a) * r))
    d.polygon(P(spikes + [(258, 170), (142, 170)]), fill=col)
    d.ellipse(P([(150, 84), (250, 204)]), fill=col)
    # bufanda al viento
    d.polygon(P(spline([(160, 200), (240, 200), (330, 186), (420, 210), (360, 232), (250, 236), (170, 238)], 6)), fill=col)
    # abrigo largo abierto
    d.polygon(P(spline([(130, 236), (270, 236), (300, 420), (318, 700), (226, 690), (200, 560), (176, 690), (84, 700), (102, 420)], 8)), fill=col)
    # piernas
    d.polygon(P([(150, 680), (186, 680), (180, 880), (152, 880)]), fill=col)
    d.polygon(P([(214, 680), (250, 680), (254, 880), (224, 880)]), fill=col)
    # katana al hombro
    d.line(P([(96, 360), (390, 24)]), fill=col, width=max(2, int(17 * s)))
    d.line(P([(96, 360), (60, 402)]), fill=col, width=max(3, int(18 * s)))
    d.line(P([(84, 330), (124, 368)]), fill=col, width=max(3, int(10 * s)))

l = layer(); d = ImageDraw.Draw(l)
girl(d, 1450, 330, 1.05, (255, 160, 200, 44))
base = comp(base, l, 9)
l = layer(); d = ImageDraw.Draw(l)
swordsman(d, 560, 250, 0.95, (215, 140, 190, 34))
base = comp(base, l, 11)

# ---- rama de sakura desde la esquina superior derecha
def branch(d, x, y, ang, length, width, depth, blossoms):
    if depth == 0 or length < 12: 
        blossoms.append((x, y)); return
    x2 = x + math.cos(ang) * length; y2 = y + math.sin(ang) * length
    d.line([(x, y), (x2, y2)], fill=(34, 18, 26, 255), width=max(1, int(width)))
    d.ellipse([x2 - width/2, y2 - width/2, x2 + width/2, y2 + width/2], fill=(34, 18, 26, 255))
    if depth <= 3: blossoms.append((x2, y2))
    for k in range(rnd.choice([2, 2, 3])):
        branch(d, x2, y2, ang + rnd.uniform(-0.65, 0.65), length * rnd.uniform(0.62, 0.8), width * 0.68, depth - 1, blossoms)

l = layer(); d = ImageDraw.Draw(l)
blossoms = []
branch(d, W + 20, -20, math.radians(155), 260, 26, 7, blossoms)
branch(d, W + 10, 120, math.radians(172), 200, 16, 6, blossoms)
base = comp(base, l, 1)

def flower(d, cx, cy, r, col, rot):
    for i in range(5):
        a = rot + i * 2 * math.pi / 5
        px_, py_ = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
        d.ellipse([px_ - r*0.5, py_ - r*0.5, px_ + r*0.5, py_ + r*0.5], fill=col)
    d.ellipse([cx - r*0.18, cy - r*0.18, cx + r*0.18, cy + r*0.18], fill=(255, 235, 240, col[3]))

pinks = [(255, 183, 206), (248, 150, 185), (240, 120, 165), (255, 205, 222)]
for blur, alpha, scale, chance in [(7, 80, 1.6, 0.10), (2, 130, 1.0, 0.22), (0, 170, 0.8, 0.18)]:
    l = layer(); d = ImageDraw.Draw(l)
    for (bx, by) in blossoms:
        if rnd.random() < chance:
            for _ in range(rnd.randint(1, 2)):
                c = rnd.choice(pinks)
                flower(d, bx + rnd.uniform(-22, 22), by + rnd.uniform(-22, 22), rnd.uniform(9, 17) * scale,
                       (*c, int(alpha * rnd.uniform(0.6, 1))), rnd.uniform(0, 6.28))
    base = comp(base, l, blur)

# ---- pétalos cayendo
def petal(d, cx, cy, r, col, rot):
    pts = []
    for i in range(24):
        t = i / 24 * 2 * math.pi
        x = math.cos(t) * r; y = math.sin(t) * r * 0.55
        if math.cos(t) > 0.85: x *= 0.92
        pts.append((cx + x * math.cos(rot) - y * math.sin(rot), cy + x * math.sin(rot) + y * math.cos(rot)))
    d.polygon(pts, fill=col)

for blur, n, alpha in [(5, 40, 70), (2, 60, 110), (0, 50, 150)]:
    l = layer(); d = ImageDraw.Draw(l)
    for _ in range(n):
        x = rnd.uniform(0, W); y = rnd.uniform(0, H)
        # más pétalos cerca de la rama (arriba a la derecha)
        if rnd.random() < 0.55: x = rnd.uniform(W * 0.45, W); y = rnd.uniform(0, H * 0.75)
        petal(d, x, y, rnd.uniform(5, 11) * (1.6 if blur > 3 else 1), (*rnd.choice(pinks), int(alpha * rnd.uniform(0.5, 1))), rnd.uniform(0, 6.28))
    base = comp(base, l, blur)

# ---- viñeta radial suave
vw, vh = W // 8, H // 8
v = Image.new("L", (vw, vh))
vp = v.load()
for y in range(vh):
    for x in range(vw):
        dx = (x / vw - 0.55) / 0.75; dy = (y / vh - 0.45) / 0.75
        vp[x, y] = int(max(0, min(1, (dx * dx + dy * dy) ** 0.5 - 0.35)) * 200)
v = v.resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(20))
black = Image.new("RGBA", (W, H), (0, 0, 0, 255))
black.putalpha(v)
base = Image.alpha_composite(base, black)

img = base.convert("RGB")
os.makedirs(OUT, exist_ok=True)
img.save(os.path.join(OUT, "background.jpg"), quality=88, optimize=True, progressive=True)
print("ok", os.path.getsize(os.path.join(OUT, "background.jpg")))
