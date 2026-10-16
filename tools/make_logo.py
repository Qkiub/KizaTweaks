"""Genera el logo de KizaTweaks: monograma "KT" minimalista con "ケーティー" (KT en japonés) debajo.
Salidas: logo.png (con texto japonés), logo-mark.png (solo KT, para tamaños chicos) y kizatweaks.ico.
Uso: python3 tools/make_logo.py [carpeta]   (requiere Pillow y Noto Sans CJK para el texto japonés)"""
import os, sys
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "src/KizaTweaks.App/Assets"
S = 4                      # supermuestreo
N = 256 * S
TILE = (13, 9, 12, 255)    # negro con un toque ciruela
BORDER = (58, 38, 50, 255)
SAKURA = (244, 143, 177, 255)
SOFT = (194, 171, 183, 255)
JP_FONT = "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"


def render(with_text):
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([8 * S, 8 * S, 248 * S, 248 * S], radius=56 * S, fill=TILE, outline=BORDER, width=3 * S)

    # Monograma: con texto queda un poco más arriba
    oy = -14 if with_text else 0
    w = 17 * S

    def seg(a, b):
        ax, ay = a[0] * S, (a[1] + oy) * S
        bx, by = b[0] * S, (b[1] + oy) * S
        d.line([ax, ay, bx, by], fill=SAKURA, width=w)
        for x, y in ((ax, ay), (bx, by)):
            d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=SAKURA)

    # K
    seg((66, 78), (66, 178))
    seg((76, 128), (120, 78))
    seg((84, 120), (120, 178))
    # T
    seg((140, 78), (198, 78))
    seg((169, 78), (169, 178))

    if with_text and os.path.exists(JP_FONT):
        f = ImageFont.truetype(JP_FONT, 25 * S, index=0)  # índice 0 = Noto Sans CJK JP
        text = "ケーティー"
        tw = d.textlength(text, font=f)
        d.text(((N - tw) / 2, 196 * S), text, font=f, fill=SOFT)
    return img.resize((256, 256), Image.LANCZOS)


full = render(True)
mark = render(False)
os.makedirs(OUT, exist_ok=True)
full.save(os.path.join(OUT, "logo.png"))
mark.save(os.path.join(OUT, "logo-mark.png"))
# Ícono: en 16–48 px solo "KT" (el japonés no se leería); desde 64 px con el texto.
small = [mark.resize((s, s), Image.LANCZOS) for s in (16, 24, 32, 48)]
large = [full.resize((s, s), Image.LANCZOS) for s in (64, 128)]
full.save(os.path.join(OUT, "kizatweaks.ico"), format="ICO",
          sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
          append_images=small + large)

# Vista previa (no se usa en la app)
prev = Image.new("RGBA", (256 + 256 + 48 + 32 + 16 + 60, 256), (10, 7, 9, 255))
prev.paste(full, (0, 0), full)
prev.paste(mark, (266, 0), mark)
x = 532
for s in (48, 32, 16):
    sm = mark.resize((s, s), Image.LANCZOS)
    prev.paste(sm, (x, 10), sm)
    x += s + 6
prev.save(os.path.join(os.environ.get("TMPDIR", "/tmp"), "kizatweaks-logo-preview.png"))
print("ok")
