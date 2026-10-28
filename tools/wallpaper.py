# Genera el wallpaper de Vigía como SVG (curvas de nivel + haz de vigía), sin assets de terceros.
import math, random, sys
W, H = 3840, 2160
random.seed(7)

def noise_field():
    # suma de "colinas" gaussianas: relieve suave y determinista
    hills = [(random.uniform(-0.2, 1.2) * W, random.uniform(-0.2, 1.2) * H,
              random.uniform(350, 900), random.uniform(-0.8, 0.8)) for _ in range(12)]
    hills += [(W * 0.5, H * 0.5, 430, 1.9), (W * 0.52 + 300, H * 0.5 + 120, 340, 0.9),
              (W * 0.48 - 260, H * 0.5 - 160, 300, 0.7), (W * 0.5 + 80, H * 0.5 - 260, 260, -0.5)]
    def f(x, y):
        return sum(a * math.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * s * s)) for cx, cy, s, a in hills)
    return f

f = noise_field()
step = 24
nx, ny = W // step + 2, H // step + 2
grid = [[f(i * step, j * step) for i in range(nx)] for j in range(ny)]

def contours(level):
    segs = []
    for j in range(ny - 1):
        for i in range(nx - 1):
            v = [grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]]
            p = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            pts = []
            for k in range(4):
                a, b = v[k], v[(k + 1) % 4]
                if (a < level) != (b < level):
                    t = (level - a) / (b - a)
                    pa, pb = p[k], p[(k + 1) % 4]
                    pts.append(((pa[0] + t * (pb[0] - pa[0])) * step, (pa[1] + t * (pb[1] - pa[1])) * step))
            if len(pts) >= 2:
                segs.append((pts[0], pts[1]))
                if len(pts) == 4:
                    segs.append((pts[2], pts[3]))
    return segs

lo = min(min(r) for r in grid); hi = max(max(r) for r in grid)
out = []
out.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">')
out.append('''<defs>
<radialGradient id="bg" cx="0.5" cy="0.5" r="0.9">
  <stop offset="0" stop-color="#1b2230"/><stop offset="0.55" stop-color="#12161e"/><stop offset="1" stop-color="#0b0d12"/>
</radialGradient>
<radialGradient id="glow" cx="0.5" cy="0.5" r="0.5">
  <stop offset="0" stop-color="#6fa8ff" stop-opacity="0.10"/><stop offset="1" stop-color="#6fa8ff" stop-opacity="0"/>
</radialGradient>
<linearGradient id="beam" x1="0" y1="0" x2="1" y2="0">
  <stop offset="0" stop-color="#9cc3ff" stop-opacity="0.22"/><stop offset="1" stop-color="#9cc3ff" stop-opacity="0"/>
</linearGradient>
<mask id="fade"><rect width="100%" height="100%" fill="url(#m)"/></mask>
<radialGradient id="m" cx="0.5" cy="0.5" r="0.75">
  <stop offset="0" stop-color="#fff"/><stop offset="0.6" stop-color="#fff" stop-opacity="0.55"/><stop offset="1" stop-color="#fff" stop-opacity="0.18"/>
</radialGradient>
<linearGradient id="beamfade" gradientUnits="userSpaceOnUse" x1="CX" y1="CY" x2="BX" y2="BY">
  <stop offset="0" stop-color="#a8c8ff" stop-opacity="0.26"/><stop offset="0.45" stop-color="#8fb4e8" stop-opacity="0.08"/><stop offset="1" stop-color="#8fb4e8" stop-opacity="0"/>
</linearGradient>
<radialGradient id="hole"><stop offset="0" stop-color="#0c1016" stop-opacity=".82"/><stop offset=".45" stop-color="#0c1016" stop-opacity=".6"/><stop offset=".75" stop-color="#0c1016" stop-opacity=".22"/><stop offset="1" stop-color="#0c1016" stop-opacity="0"/></radialGradient>
<filter id="soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="14"/></filter>
<filter id="grain"><feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="3"/><feColorMatrix values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 0.075 0"/></filter>
</defs>''')
out.append(f'<rect width="{W}" height="{H}" fill="url(#bg)"/>')
out.append('<g mask="url(#fade)" fill="none" stroke-linecap="round" stroke-linejoin="round">')
n = 34
AMB = 22
for k in range(1, n):
    lvl = lo + (hi - lo) * k / n
    major = k % 5 == 0
    col = '#e9b45a' if k == AMB else ('#8fb4e8' if major else '#5d6c84')
    op = 0.45 if k == AMB else (0.30 if major else 0.16)
    sw = 2.4 if k == AMB else (2.0 if major else 1.1)
    d = ''.join(f'M{a[0]:.0f} {a[1]:.0f}L{b[0]:.0f} {b[1]:.0f}' for a, b in contours(lvl))
    out.append(f'<path d="{d}" stroke="{col}" stroke-opacity="{op}" stroke-width="{sw}"/>')
out.append('</g>')
# haz de vigía: cono suave desde el punto de observación
best = max(((grid[j][i], i, j) for j in range(ny) for i in range(nx)
            if abs(i * step - W * 0.5) < 500 and abs(j * step - H * 0.5) < 400))
cx, cy = best[1] * step, best[2] * step
# centro visual de la cima: centroide de las curvas más altas que rodean el máximo
for kk in range(n - 1, 0, -1):
    lv = lo + (hi - lo) * kk / n
    if lv > best[0]: continue
    pts = [q for a, b in contours(lv) for q in (a, b) if abs(q[0] - cx) < 700 and abs(q[1] - cy) < 600]
    if len(pts) > 40:
        cx = int(sum(q[0] for q in pts) / len(pts)); cy = int(sum(q[1] for q in pts) / len(pts)); break
out[1] = out[1].replace('cx="0.5" cy="0.5"', f'cx="{cx / W:.3f}" cy="{cy / H:.3f}"')
out.append(f'<rect width="{W}" height="{H}" fill="url(#glow)"/>')
cx, cy = W // 2, H // 2
# hueco oscuro detrás del logo para que las curvas no crucen las letras
out.append(f'<ellipse cx="{cx}" cy="{cy + 20}" rx="1000" ry="420" fill="url(#hole)"/>')
out.append(f'<rect width="{W}" height="{H}" filter="url(#grain)"/>')
ty = cy + 20
out.append(f'<text x="{cx}" y="{ty}" text-anchor="middle" font-family="Segoe UI Variable Display, Segoe UI, sans-serif" font-size="132" font-weight="650" letter-spacing="-2" fill="#f1f4f9">Vigía<tspan fill="#a9c9ff" font-weight="400">OS</tspan></text>')
# línea sin punto y lema corridos a la derecha: la línea nace antes del logo y acaba bajo sus
# primeras letras; el lema arranca ahí y pasa del borde derecho (proporciones del ejemplo)
TW = 471                              # ancho medido del título a 132 px
L = cx - TW / 2
gy = ty + 74
DX = 0.5 * TW                         # todo el bloque corrido medio título a la derecha
x1, x2 = L - 0.25 * TW + DX, L + 0.21 * TW + DX
tx, TL = L + 0.26 * TW + DX, 1.1 * TW
out.append(f'<line x1="{x1:.0f}" y1="{gy - 12}" x2="{x2:.0f}" y2="{gy - 12}" stroke="#f1f4f9" stroke-opacity=".9" stroke-width="2.5"/>')
out.append(f'<text x="{tx:.0f}" y="{gy}" textLength="{TL:.0f}" lengthAdjust="spacing" font-family="Segoe UI Variable Text, Segoe UI, sans-serif" font-size="35" font-weight="400" fill="#eef2f8">MAXIMUM PERFORMANCE</text>')
out.append('</svg>')
open(sys.argv[1], 'w', encoding='utf-8').write('\n'.join(out))
print('ok')
