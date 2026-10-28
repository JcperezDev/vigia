"""Genera vigia.ico (logo Prisma: cubo con tapa ámbar sobre fondo oscuro) de 16 a 256 px.

Uso:  python -I tools/make-icon.py <carpeta-de-trabajo>
Dibuja en SVG a 8x con Chrome headless y reduce con Pillow; en 16-48 px el trazo es más
grueso y el cubo más grande para que se lea en la barra de tareas.
"""
import io, json, os, struct, subprocess, sys
from PIL import Image

CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
SIZES = [16, 24, 32, 48, 64, 128, 256]
SC = 8


def tile(s):
    sw = {16: 2.6, 24: 2.3, 32: 2.0, 48: 1.8}.get(s, 1.6)
    k = 0.625 if s >= 64 else (0.72 if s >= 32 else 0.82)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{s*SC}" height="{s*SC}" viewBox="0 0 24 24">
<defs><linearGradient id="g{s}" x1=".33" y1="0" x2=".67" y2="1"><stop offset="0" stop-color="#232a35"/><stop offset="1" stop-color="#12151a"/></linearGradient></defs>
<rect x=".2" y=".2" width="23.6" height="23.6" rx="5.5" fill="url(#g{s})" stroke="#ffffff" stroke-opacity=".07" stroke-width=".4"/>
<g transform="translate(12 12) scale({k}) translate(-12 -12)" fill="none" stroke="#f1f4f9" stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round">
<path d="M12 3 20 7.5v9L12 21l-8-4.5v-9Z"/><path d="M4 7.5 12 12l8-4.5M12 12v9"/>
<path d="M12 3 20 7.5 12 12 4 7.5Z" fill="#e9b45a" fill-opacity=".9" stroke="none"/>
</g></svg>'''


def main(work):
    os.makedirs(work, exist_ok=True)
    html, x, pos = '<!doctype html><html><body style="margin:0;background:transparent">', 0, []
    for s in SIZES:
        html += f'<div style="position:absolute;left:{x}px;top:0;line-height:0">{tile(s)}</div>'
        pos.append((s, x)); x += s * SC + 10
    page = os.path.join(work, "tiles.html"); shot = os.path.join(work, "tiles.png")
    open(page, "w", encoding="utf-8").write(html + "</body></html>")
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
                    f"--window-size={x + 20},{256 * SC}", f"--screenshot={shot}", "file:///" + page.replace("\\", "/")],
                   check=True, capture_output=True)
    im = Image.open(shot).convert("RGBA")
    pngs = []
    for s, px in pos:
        t = im.crop((px, 0, px + s * SC, s * SC)).resize((s, s), Image.LANCZOS)
        t.save(os.path.join(work, f"icon{s}.png"))
        b = io.BytesIO(); t.save(b, "PNG"); pngs.append((s, b.getvalue()))
    out = os.path.join(work, "vigia.ico")
    with open(out, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(pngs))); off = 6 + 16 * len(pngs)
        for s, d in pngs:
            f.write(struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(d), off)); off += len(d)
        for _, d in pngs:
            f.write(d)
    print("ok", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
