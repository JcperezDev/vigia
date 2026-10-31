// Regresión de privacidad: Vigía no debe pedir ubicación (aviso de Windows «Location has been turned off»).
// Desde Windows 11 24H2, las APIs de Wi-Fi que dan el BSSID piden permiso de ubicación (Microsoft Learn,
// «Changes to API behavior for Wi-Fi access and location»). 0.20.17 llamaba a WlanGetNetworkBssList y a
// WlanQueryInterface(wlan_intf_opcode_current_connection) para las Sugerencias de red.
//   node tools/location-test.js                 revisa el código
//   node tools/location-test.js <Vigia.exe>     además abre la interfaz en un WebView2 oculto y pide la ubicación
// Sale con 1 si algo falla. No lee ni escribe ninguna ubicación.
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
let ok = 0, fail = 0;
const check = (cond, name) => { cond ? ok++ : fail++; console.log((cond ? 'OK   ' : 'FAIL ') + name); };

function walk(dir, exts, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['bin', 'obj', 'node_modules', 'dist'].includes(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, exts, out); else if (exts.some((x) => e.name.endsWith(x))) out.push(p);
  }
  return out;
}
const rel = (p) => path.relative(ROOT, p).replace(/\\/g, '/');
const read = (p) => fs.readFileSync(p, 'utf8');
const code = walk(path.join(ROOT, 'app'), ['.cs', '.csproj', '.manifest']);
const web = walk(path.join(ROOT, 'app', 'Vigia', 'wwwroot'), ['.js', '.html']);
const strip = (s) => s.replace(/\/\/\/?[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');   // fuera los comentarios

// 1) Ninguna de las APIs que Microsoft lista como sujetas a ubicación
const banned = [
  ['WlanGetNetworkBssList', /WlanGetNetworkBssList/], ['WlanGetAvailableNetworkList', /WlanGetAvailableNetworkList/],
  ['WlanScan', /\bWlanScan\b/], ['WlanRegisterNotification', /WlanRegisterNotification/],
  ['Windows.Devices.WiFi', /Windows\.Devices\.WiFi|WiFiAdapter/], ['GetLanIdentifiers', /GetLanIdentifiers/],
  ['Geolocator / Windows.Devices.Geolocation', /Geolocator|Devices\.Geolocation/],
  ['capacidad wiFiControl / location', /wiFiControl|Capability\s+Name="location"/i],
  ['netsh wlan', /netsh[^"\n]*wlan/i],
];
for (const [name, rx] of banned) {
  const hits = code.filter((f) => rx.test(strip(read(f)))).map(rel);
  check(hits.length === 0, `sin ${name}${hits.length ? ' → ' + hits.join(', ') : ''}`);
}

// 2) WlanQueryInterface solo con consultas que no dan BSSID (nunca current_connection = 7)
const sug = read(path.join(ROOT, 'app', 'Vigia', 'Suggestions.cs'));
const allowed = /Allowed\s*=\s*\[([^\]]*)\]/.exec(sug);
check(!!allowed && !/Current|\b7\b/.test(allowed[1]), 'Wlan.Allowed no incluye current_connection');
const ops = /OpChannel\s*=\s*(\d+),\s*OpRealtime\s*=\s*(\d+),\s*OpRssi\s*=\s*(0x[0-9A-Fa-f]+)/.exec(sug);
check(!!ops && ops[1] === '8' && ops[2] === '19' && ops[3].toLowerCase() === '0x10000102', 'opcodes: canal 8, tiempo real 19, rssi 0x10000102');
const wq = code.filter((f) => /WlanQueryInterface\s*\(/.test(strip(read(f)))).map(rel);
check(wq.length === 1 && wq[0] === 'app/Vigia/Suggestions.cs', 'WlanQueryInterface solo en Suggestions.cs (pasa por Wlan.Allowed)');
check(/Array\.IndexOf\(Allowed, op\) < 0\) throw/.test(sug), 'la consulta fuera de la lista lanza');
check(/SignalOn\s*=>\s*Environment\.GetEnvironmentVariable\("VIGIA_WIFI_SIGNAL"\)\s*==\s*"1"/.test(sug), 'la señal Wi-Fi viene apagada (solo VIGIA_WIFI_SIGNAL=1 la enciende)');
{ const reads = strip(sug).split('\n').filter((l) => /Wlan\.Read\(/.test(l));
  check(reads.length === 1 && /if \(Wlan\.SignalOn\) wifi = Wlan\.Read\(/.test(reads[0]), 'las Sugerencias solo leen Wi-Fi con la señal encendida'); }
{ const lg = strip(read(path.join(ROOT, 'app', 'Vigia', 'LocationGuard.cs')));
  check(/if \(!Suggestions\.Wlan\.SignalOn\)[^\n]*continue;/.test(lg), '--wifi-check tampoco consulta WLAN con la señal apagada'); }
check(!/\bSsid\b|SameChannel|Bssid/i.test(strip(sug).replace(/BSSID\)?/g, '')), 'WifiFacts sin nombre de red, BSSID ni redes vecinas');

// 3) Toda WebView2 deniega los permisos de página
for (const f of code.filter((f) => /EnsureCoreWebView2Async/.test(read(f)))) {
  const s = strip(read(f));
  check(/LocationGuard\.Harden\(core,|\bHarden\(core,/.test(s), `${rel(f)}: WebView2 con LocationGuard.Harden`);
}
const gp = path.join(ROOT, 'app', 'Vigia', 'LocationGuard.cs');
check(fs.existsSync(gp), 'existe LocationGuard.cs');
const guard = fs.existsSync(gp) ? strip(read(gp)) : '';
check(/e\.State\s*=\s*CoreWebView2PermissionState\.Deny/.test(guard), 'Harden pone Deny');
check(!/Latitude|Longitude|Coordinate|Bssid|Ssid/i.test(guard), 'las trazas no tocan coordenadas, red ni BSSID');
const hits = web.filter((f) => /navigator\.geolocation|getCurrentPosition|watchPosition/.test(read(f))).map(rel);
check(hits.length === 0, `la interfaz no pide geolocalización${hits.length ? ' → ' + hits.join(', ') : ''}`);

// 4) Prueba real (opcional): la interfaz pide la ubicación y WebView2 la niega sin preguntar
const exe = process.argv[2];
if (exe) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'vigia-loc-'));
  const out = path.join(tmp, 'geo.json');
  const r = spawnSync(exe, ['--geo-probe', out], { env: { ...process.env, VIGIA_DATA: path.join(tmp, 'datos') }, timeout: 120000, windowsHide: true });
  let j = {};
  try { j = JSON.parse(fs.readFileSync(out, 'utf8')); } catch { }
  check(r.status === 0 && j.ok === true && j.geolocation === 'error 1', `--geo-probe: navigator.geolocation denegado (rc=${r.status}, ${j.geolocation ?? j.error ?? '-'})`);
  const log = path.join(tmp, 'datos', 'privacidad.log');
  const txt = fs.existsSync(log) ? read(log) : '';
  check(!/-?\d{1,3}\.\d{4,}|[0-9a-f]{2}(:[0-9a-f]{2}){5}|GHz|dBm/i.test(txt), 'privacidad.log sin coordenadas, BSSID, frecuencia ni señal');
  const w = path.join(tmp, 'wifi.txt');
  const env0 = { ...process.env, VIGIA_DATA: path.join(tmp, 'datos') }; delete env0.VIGIA_WIFI_SIGNAL;
  const r0 = spawnSync(exe, ['--wifi-check', w], { env: env0, timeout: 60000, windowsHide: true });
  const w0 = fs.existsSync(w) ? read(w) : '';
  const l0 = fs.existsSync(log) ? read(log) : '';
  check(r0.status === 0 && !/datos=/.test(w0) && (!/estado=/.test(w0) || /señal=apagada/.test(w0)) && !/wifi: lectura sin ubicación/.test(l0),
    'por defecto --wifi-check no hace ninguna consulta WLAN (señal apagada)');
  const r2 = spawnSync(exe, ['--wifi-check', w], { env: { ...env0, VIGIA_WIFI_SIGNAL: '1' }, timeout: 60000, windowsHide: true });
  const wl = fs.existsSync(log) ? read(log) : '';
  check(r2.status === 0 && !/rc=5\b/.test(wl), 'Wi-Fi: ninguna consulta devolvió acceso denegado (rc=5)');
  fs.rmSync(tmp, { recursive: true, force: true });
}

console.log(`\n${ok} OK / ${fail} FAIL`);
process.exit(fail ? 1 : 0);
