import math, pathlib, io
from logo_gen import squircle, arc_seg, defs, ring
here = pathlib.Path(__file__).parent

def logo_svg(size):
    small = size <= 32
    # Petites tailles : la tuile remplit le carre (pas d'ombre), anneau plus epais, ecarts plus larges.
    r = 124 if small else 112
    cy = 128 if small else 124
    ro, ri, gap, dot = (90, 40, 13, 18) if small else (82, 44, 9, 15)
    shadow = '' if small else ' filter="url(#sh)"'
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="{size}" height="{size}">{defs}
<g{shadow}><path d="{squircle(128,cy,r)}" fill="url(#blue)"/></g>
<path d="{squircle(128,cy,r)}" fill="url(#sheen)"/>
{ring(128,cy,ro,ri,6,gap,["url(#cyan)"]+["#FFFFFF"]*5)}
<circle cx="128" cy="{cy}" r="{dot}" fill="#FFFFFF"/></svg>'''

sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
out = here / "png"; out.mkdir(exist_ok=True)
html = "<html><body style='margin:0;background:transparent'>" + "".join(
    f"<div id='s{s}' style='width:{s}px;height:{s}px'>{logo_svg(s)}</div>" for s in sizes) + "</body></html>"
(here / "icons.html").write_text(html)
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b = p.chromium.launch(); pg = b.new_page(device_scale_factor=1, viewport={"width": 400, "height": 1200})
    pg.goto(f"file://{here/'icons.html'}")
    for s in sizes:
        pg.locator(f"#s{s}").screenshot(path=str(out / f"{s}.png"), omit_background=True)
    b.close()
from PIL import Image
imgs = [Image.open(out / f"{s}.png").convert("RGBA") for s in sizes]
imgs[-1].save(here / "filewheel.ico", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save(here / "filewheel.png")
(here / "filewheel.svg").write_text(logo_svg(256))
print("ok")
