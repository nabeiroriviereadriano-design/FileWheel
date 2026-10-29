import math, pathlib
here = pathlib.Path(__file__).parent

def squircle(cx, cy, r, n=5, pts=160):
    # superellipse |x|^n + |y|^n = r^n (forme d'icone macOS)
    d = []
    for i in range(pts):
        t = 2*math.pi*i/pts
        c, s = math.cos(t), math.sin(t)
        x = cx + r*math.copysign(abs(c)**(2/n), c)
        y = cy + r*math.copysign(abs(s)**(2/n), s)
        d.append(f"{'M' if i==0 else 'L'}{x:.2f},{y:.2f}")
    return " ".join(d) + "Z"

def arc_seg(cx, cy, ro, ri, a0, a1):
    # secteur d'anneau, angles en degres (0 = haut, sens horaire)
    def p(r, a):
        a = math.radians(a - 90)
        return cx + r*math.cos(a), cy + r*math.sin(a)
    x0,y0 = p(ro,a0); x1,y1 = p(ro,a1); x2,y2 = p(ri,a1); x3,y3 = p(ri,a0)
    large = 1 if (a1-a0) > 180 else 0
    return (f"M{x0:.2f},{y0:.2f} A{ro},{ro} 0 {large} 1 {x1:.2f},{y1:.2f} "
            f"L{x2:.2f},{y2:.2f} A{ri},{ri} 0 {large} 0 {x3:.2f},{y3:.2f} Z")

def doc(cx, cy, w, h, fill, fold_fill):
    x0, y0 = cx-w/2, cy-h/2
    f = w*0.32
    body = f"M{x0},{y0+3} Q{x0},{y0} {x0+3},{y0} L{x0+w-f},{y0} L{x0+w},{y0+f} L{x0+w},{y0+h-3} Q{x0+w},{y0+h} {x0+w-3},{y0+h} L{x0+3},{y0+h} Q{x0},{y0+h} {x0},{y0+h-3} Z"
    fold = f"M{x0+w-f},{y0} L{x0+w-f},{y0+f-2} Q{x0+w-f},{y0+f} {x0+w-f+2},{y0+f} L{x0+w},{y0+f} Z"
    return f'<path d="{body}" fill="{fill}"/><path d="{fold}" fill="{fold_fill}"/>'

defs = '''<defs>
<linearGradient id="blue" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3A9BEF"/><stop offset="1" stop-color="#0063B8"/></linearGradient>
<linearGradient id="sheen" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".35"/><stop offset=".5" stop-color="#fff" stop-opacity="0"/></linearGradient>
<linearGradient id="light" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#E9EAEF"/></linearGradient>
<linearGradient id="cyan" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#9BE0FF"/><stop offset="1" stop-color="#4CC2FF"/></linearGradient>
<linearGradient id="q1" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7CD0FF"/><stop offset="1" stop-color="#2AA0F0"/></linearGradient>
<linearGradient id="q2" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#2B8FE8"/><stop offset="1" stop-color="#0A6CC8"/></linearGradient>
<linearGradient id="q3" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#0F5FB0"/><stop offset="1" stop-color="#084C93"/></linearGradient>
<linearGradient id="q4" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#1A78D6"/><stop offset="1" stop-color="#0B5CAD"/></linearGradient>
<filter id="sh" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="5" stdDeviation="6" flood-color="#000" flood-opacity=".22"/></filter>
</defs>'''

def ring(cx, cy, ro, ri, n, gap, fills):
    out = []
    step = 360/n
    for i in range(n):
        a0 = -step/2 + i*step + gap/2
        a1 = a0 + step - gap
        out.append(f'<path d="{arc_seg(cx,cy,ro,ri,a0,a1)}" fill="{fills[i % len(fills)]}"/>')
    return "".join(out)

# A : tuile bleue (squircle macOS) + roue blanche a 6 secteurs, secteur du haut en cyan (accent Windows clair)
A = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">{defs}
<g filter="url(#sh)"><path d="{squircle(128,124,112)}" fill="url(#blue)"/></g>
<path d="{squircle(128,124,112)}" fill="url(#sheen)"/>
{ring(128,124,82,44,6,9,["url(#cyan)"]+["#FFFFFF"]*5)}
<circle cx="128" cy="124" r="15" fill="#FFFFFF"/></svg>'''

# B : tuile claire (macOS) + roue bleue, fichier au centre
B = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">{defs}
<g filter="url(#sh)"><path d="{squircle(128,124,112)}" fill="url(#light)"/></g>
<path d="{squircle(128,124,111.5)}" fill="none" stroke="#000" stroke-opacity=".08" stroke-width="1.5"/>
{ring(128,124,86,58,6,8,["#4CC2FF"]+["#0078D4"]*5)}
{doc(128,124,40,52,"#0078D4","#7FC6F5")}</svg>'''

# C : disque Fluent sans tuile (forme Windows) : anneau en 4 quadrants de bleus, fichier blanc au centre
C = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">{defs}
<g filter="url(#sh)">
<path d="{arc_seg(128,124,110,52,0+3,90-3)}" fill="url(#q1)"/>
<path d="{arc_seg(128,124,110,52,90+3,180-3)}" fill="url(#q4)"/>
<path d="{arc_seg(128,124,110,52,180+3,270-3)}" fill="url(#q3)"/>
<path d="{arc_seg(128,124,110,52,270+3,360-3)}" fill="url(#q2)"/></g>
{doc(128,124,46,60,"#0078D4","#7FC6F5")}</svg>'''

# Variantes monochromes pour la zone de notification (style icones systeme Windows 11)
def tray(color):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
{''.join(f'<path d="{arc_seg(16,16,14,8,-30+i*60+5,-30+i*60+55)}" fill="{color}" opacity="{1 if i==0 else .9}"/>' for i in range(6))}
<circle cx="16" cy="16" r="3" fill="{color}"/></svg>'''

for name, svg in [("A", A), ("B", B), ("C", C), ("tray-white", tray("#FFFFFF")), ("tray-black", tray("#1D1D1F"))]:
    (here/f"logo-{name}.svg").write_text(svg)
print("ok")
