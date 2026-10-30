"""Contract regression: setup owns the sensor dependency, never Monitor."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
p = (root / 'app/Vigia/Program.cs').read_text(encoding='utf-8-sig')
s = (root / 'app/Puente/Setup.cs').read_text(encoding='utf-8-sig')
assert '"--setup-sensors"' in p, 'missing installer-only sensor entrypoint'
assert '"--setup-sensors"' in s, 'Puente must invoke sensor setup'
assert 'Send("pawnAsk"' not in p, 'Monitor still asks to install a driver'
assert 'if (_snapPath is null) _ = PawnAsync();' not in p, 'Monitor still installs drivers'
assert '_tel.CpuDriverReady();' in p[p.index('case "_pawn":'):p.index('case "autoStartRepaired":')], 'broker success must refresh CPU'
sv = (root / 'app/Puente/SensorService.cs').read_text(encoding='utf-8-sig')
t = (root / 'app/Vigia/Telemetry.cs').read_text(encoding='utf-8-sig')
h = (root / 'app/Vigia/SensorHost.cs').read_text(encoding='utf-8-sig')
prep = s[s.index('static void PrepareSensors'):s.index('const string ListName')]
assert 'InstallSensors(exe);' in prep, 'install/update must register the read-only sensor service'
un = s[s.index('static int UninstallFlow'):]
assert un.index('RemoveSensors();') < un.index('CloseVigia(null);'), 'uninstall must remove the service before deleting files'
close = s[s.index('static bool CloseVigia'):]
assert close.index('StopSensors();') < close.index('GetProcessesByName'), 'service must release version files before replacing them'
assert 'return sensorsStopped;' in close and 'SessionId == 0' in close, 'CloseVigia reports a stuck service and never kills it'
rep = s[s.index('var aside = Aside(target);'):s.index('Journal.Write("install", m.Version, cur, "verificando");')]
assert 'if (!CloseVigia(waitPid))' in rep and rep.index('CloseVigia(waitPid)') < rep.index('MoveDir(target, aside)'), 'repair must not move a folder the service still uses'
assert 'StartSensors();' in rep[rep.index('catch'):], 'a failed repair must bring the sensor service back'
assert 'static bool StopSensors()' in sv and 'SvcState(' in sv, 'stop waits for STOPPED by state number, not by localized label'
assert 'if (!StopSensors())' in sv, 'service is not repointed while the old copy still runs'
assert 'Layout.Under(exe, Layout.Root)' in sv, 'service may only point at the protected install'
assert '"obj=", "LocalSystem"' in sv and '--sensor-service' in sv, 'service command shape'
assert 'SensorHost.ReadCpu()' in t and '!Elevated' in t, 'unprivileged Vigía must read the service'
assert 'IsSystem) return 3' in h and 'InstallTrust.Problem(AppContext.BaseDirectory)' in h, 'service entrypoint must refuse non-SYSTEM/untrusted folders'
assert 'TrustedOwner(h)' in h and '(A;;GR;;;AU)' in h and 'GA;;;SY' in h, 'shared memory: SYSTEM writes, users only read'
assert 'Pipe' not in h and 'Socket' not in h, 'sensor service must not accept commands'
# Versión pública sin el servicio SYSTEM (revisión externa pendiente): solo el laboratorio lo activa.
for proj in ('app/Vigia/Vigia.csproj', 'app/Puente/Puente.csproj'):
    x = (root / proj).read_text(encoding='utf-8-sig')
    assert "Condition=\"'$(SensorService)' == 'true'\"" in x and 'SENSOR_SERVICE' in x, proj + ': the service must be opt-in'
    assert x.count(';SENSOR_SERVICE<') == 1 and x.index(';SENSOR_SERVICE<') > x.index("Condition=\"'$(SensorService)' == 'true'\""), proj + ': SENSOR_SERVICE defined outside the opt-in group'
pub = prep.index('#if !SENSOR_SERVICE')
assert pub < prep.index('Ui.Show(') and 'RemoveSensors();' in prep[pub:prep.index('#endif', pub)], 'public setup must always remove a lab service, before anything can fail'
assert '#if SENSOR_SERVICE\n            InstallSensors(exe);' in prep and prep.count('InstallSensors(') == 1, 'only lab builds register the service'
assert 'SensorHost.Enabled' in t and 'SensorHost.ReadCpu()' in t[t.index('SensorHost.Enabled') - 80:t.index('SensorHost.Enabled') + 120], 'public Vigía must not read the shared memory'
assert '#if SENSOR_SERVICE\n    public static readonly bool Enabled = true;' in h, 'SensorHost.Enabled follows the build flag'
assert 'SensorHost.Enabled ? SensorHost.RunService() : 2' in p, '--sensor-service is an unknown command in the public build'
rel = (root / 'tools/release.sh').read_text(encoding='utf-8')
assert 'unset SensorService' in rel and rel.count('-p:SensorService=false') == 2 and '[ "$SVC_RC" = 2 ]' in rel, 'release build pins the service off and checks it'
assert 'SensorService=true' not in rel, 'release must never enable the service'
# Sin lectura fiable: siempre «Temperatura de CPU no disponible», nunca un número.
assert 'NoTempAdmin' in t and 'NoTempCpu' in t and 'v > 1 && v < 150' in t, 'no reading must be explained, never 0 °C'
js = (root / 'app/Vigia/wwwroot/app.js').read_text(encoding='utf-8')
assert "$('cpu-temp-na').hidden = realTemp(c.temp) != null;" in js and 'v > 1 && v < 150' in js and 'unit.hidden = v == null' in js, 'UI shows «no disponible» and hides °C without a reading'
assert 'id="cpu-temp-na"' in (root / 'app/Vigia/wwwroot/index.html').read_text(encoding='utf-8')
# Ayudante: lo automático nunca abre uno nuevo (no saca UAC sin que la persona pulse nada)
assert 'Elevation.Background.Contains(cmd) ? Elevation.Broker.Existing : await Elevation.Broker.EnsureAsync()' in p, 'background commands must not start the helper'
# El permiso de laboratorio para seguir con un juego abierto no existe en el producto
import os
for d in ('app', 'tools'):
    for dp, _, fs in os.walk(root / d):
        if any(x in dp for x in ('\\bin', '/bin', '\\obj', '/obj', 'node_modules')): continue
        for f in fs:
            fp = Path(dp) / f
            if fp.suffix in ('.cs', '.js', '.ps1', '.sh', '.py', '.csproj', '.json') and fp.name not in ('sensor-install-contract-test.py', 'labguard-host-test.js'):  # la prueba lo pone para demostrar que no salta nada
                assert 'LAB_ALLOW_GAME' not in fp.read_text(encoding='utf-8', errors='ignore'), f'{fp}: lab-only switch leaked into the product'
lg = (root / 'tools/labguard-host-test.js').read_text(encoding='utf-8')
assert lg.count("LAB_ALLOW_GAME: '1'") == 1 and lg.count('LAB_ALLOW_GAME') == 2, 'the host test only sets it, to prove it bypasses nothing'
print('OK: installer/monitor sensor contract')
