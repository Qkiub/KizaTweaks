"""Genera el logo de KizaTweaks (logo.png + kizatweaks.ico). Uso: python3 tools/make_logo.py [carpeta]"""
import os, sys
from PIL import Image, ImageDraw
S=4; N=256*S
def lerp(a,b,t): return tuple(int(a[i]+(b[i]-a[i])*t) for i in range(3))
c0=(0xFF,0xA6,0xC9); c1=(0xF0,0x62,0x92); c2=(0xC2,0x18,0x5B)
grad=Image.new('RGB',(N,N))
px=grad.load()
for y in range(N):
    for x in range(N):
        t=(x+y)/(2*N)
        px[x,y]= lerp(c0,c1,t/0.55) if t<0.55 else lerp(c1,c2,(t-0.55)/0.45)
mask=Image.new('L',(N,N),0)
ImageDraw.Draw(mask).rounded_rectangle([8*S,8*S,248*S,248*S],radius=58*S,fill=255)
img=Image.new('RGBA',(N,N),(0,0,0,0)); img.paste(grad,(0,0),mask)
d=ImageDraw.Draw(img); w=30*S; W=(255,255,255,255)
def seg(a,b):
    d.line([a[0]*S,a[1]*S,b[0]*S,b[1]*S],fill=W,width=w)
    for p in (a,b): d.ellipse([p[0]*S-w/2,p[1]*S-w/2,p[0]*S+w/2,p[1]*S+w/2],fill=W)
seg((80,66),(80,190)); seg((176,66),(114,128)); seg((114,128),(176,190))
r=13*S; d.ellipse([196*S-r,128*S-r,196*S+r,128*S+r],fill=W)
big=img.resize((256,256),Image.LANCZOS)
out=sys.argv[1] if len(sys.argv) > 1 else 'src/KizaTweaks.App/Assets'
big.save(out+'/logo.png')
big.save(out+'/kizatweaks.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
prev=Image.new('RGBA',(256+48+32+16+40,256),(13,17,26,255))
prev.paste(big,(0,0),big); x=266
for s in (48,32,16):
    sm=img.resize((s,s),Image.LANCZOS); prev.paste(sm,(x,10),sm); x+=s+6
# vista previa (no se usa en la app)
prev.save(os.path.join(os.environ.get('TMPDIR', '/tmp'), 'kizatweaks-logo-preview.png'))
