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
assert 'if (cur == m.Version && !CloseVigia(waitPid))' in s, 'repair must not move a folder the service still uses'
assert 'static bool StopSensors()' in sv and 'SvcState(' in sv, 'stop waits for STOPPED by state number, not by localized label'
assert 'if (!StopSensors())' in sv, 'service is not repointed while the old copy still runs'
assert 'Layout.Under(exe, Layout.Root)' in sv, 'service may only point at the protected install'
assert '"obj=", "LocalSystem"' in sv and '--sensor-service' in sv, 'service command shape'
assert 'SensorHost.ReadCpu()' in t and '!Elevated' in t, 'unprivileged Vigía must read the service'
assert 'IsSystem) return 3' in h and 'InstallTrust.Problem(AppContext.BaseDirectory)' in h, 'service entrypoint must refuse non-SYSTEM/untrusted folders'
assert 'TrustedOwner(h)' in h and '(A;;GR;;;AU)' in h and 'GA;;;SY' in h, 'shared memory: SYSTEM writes, users only read'
assert 'Pipe' not in h and 'Socket' not in h, 'sensor service must not accept commands'
print('OK: installer/monitor sensor contract')
