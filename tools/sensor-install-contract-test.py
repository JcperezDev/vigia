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
print('OK: installer/monitor sensor contract')
