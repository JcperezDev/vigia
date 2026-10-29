using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Vigia;

public record TweakView(string Id, string Group, string Title, string Detail, string Risk, string Source, bool Reboot,
    bool Tested, string State, bool HasBackup, string? Blocked);
public record TweakResult(string Id, bool Ok, string State, string? Error);
public record TweakBatch(bool Ok, string? RestorePoint, TweakResult[] Results, bool NeedsReboot);

/// <summary>
/// Motor de ajustes reversibles. El catálogo vive en tweaks.json (datos, no código). Antes de cambiar nada guarda el valor
/// original de cada clave, servicio o tarea en %LOCALAPPDATA%\Vigia\tweaks-backup.json; Revertir restaura ese valor exacto.
/// </summary>
public static class Tweaks
{
    static readonly object Gate = new();
    static string BackupPath => Path.Combine(Settings.Dir, "tweaks-backup.json");
    static string LogPath => Path.Combine(Settings.Dir, "tweaks.log");

    /// <summary>Los ajustes sin probar en la VM solo se pueden aplicar en modo laboratorio.</summary>
    public static bool Lab => Environment.GetEnvironmentVariable("VIGIA_LAB") == "1" || File.Exists(Path.Combine(Settings.Dir, "lab.flag"));

    // ---------- catálogo ----------
    sealed record Def(string Id, string Group, string Title, string Detail, string Risk, string Source, bool Reboot, bool Tested, JsonArray Ops, bool Retired = false);

    static List<Def>? _all;
    internal static Func<List<JsonObject>>? CatalogOverride;   // para --selftest

    /// <summary>El título de un ajuste, como sale en Ajustes.</summary>
    public static string? TitleOf(string id) { try { return All().FirstOrDefault(d => d.Id == id)?.Title; } catch { return null; } }

    /// <summary>Los ajustes que se ofrecen. Los retirados (los que tenían riesgo) ya no salen ni se pueden aplicar.</summary>
    static List<Def> Catalog() => All().Where(d => !d.Retired).ToList();

    /// <summary>Todos, retirados incluidos: hacen falta para devolver a su valor original lo que Vigía puso en versiones anteriores.</summary>
    static List<Def> All()
    {
        if (CatalogOverride is not null) return CatalogOverride().Select(ToDef).ToList();
        if (_all is not null) return _all;
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tweaks.json")))!.AsArray();
        return _all = root.Select(n => ToDef(n!.AsObject())).ToList();
    }

    static Def ToDef(JsonObject o) => new((string)o["id"]!, (string)o["group"]!, (string)o["title"]!, (string)o["detail"]!,
        (string?)o["risk"] ?? "bajo", (string?)o["source"] ?? "", (bool?)o["reboot"] ?? false, (bool?)o["tested"] ?? false, o["ops"]!.AsArray(),
        (bool?)o["retired"] ?? false);

    /// <summary>
    /// Deshace los ajustes retirados que Vigía aplicó antes (solo esos: los que tienen copia del valor original).
    /// Hace copia de seguridad antes, como cualquier «Revertir». Si no hay nada que deshacer, no toca nada.
    /// </summary>
    public static TweakBatch? UndoRetired()
    {
        string[] ids;
        lock (Gate) { var bk = LoadBackup(); ids = All().Where(d => d.Retired && bk.ContainsKey(d.Id)).Select(d => d.Id).ToArray(); }
        if (ids.Length == 0) return null;
        var r = Revert(ids);
        Log($"retirados deshechos: {string.Join(", ", ids)} -> {(r.Ok ? "ok" : "con errores")}");
        return r;
    }

    public static List<TweakView> List()
    {
        lock (Gate)
        {
            var bk = LoadBackup();
            return Catalog().Select(d => new TweakView(d.Id, d.Group, d.Title, d.Detail, d.Risk, d.Source, d.Reboot, d.Tested,
                StateOf(d), bk.ContainsKey(d.Id), BlockedReason(d))).ToList();
        }
    }

    // ---------- estado ----------
    // applied | partial | off | na
    static string StateOf(Def d)
    {
        int match = 0, total = 0;
        foreach (var op in d.Ops.Select(x => x!.AsObject()))
        {
            if ((bool?)op["post"] == true) continue;
            var m = Matches(op);
            if (m is null) continue;   // no aplica en este equipo (p. ej. servicio inexistente)
            total++; if (m == true) match++;
        }
        return total == 0 ? "na" : match == total ? "applied" : match == 0 ? "off" : "partial";
    }

    static bool? Matches(JsonObject op)
    {
        if (op["reg"] is not null)
        {
            var (cur, _) = RegRead((string)op["reg"]!, (string)op["name"]!);
            if (op["kv"] is not null) return KvGet(cur as string, (string)op["kv"]!) == (string)op["value"]!;
            if ((bool?)op["delete"] == true) return cur is null;
            return cur is not null && Same(cur, Target(op));
        }
        if (op["svc"] is not null)
        {
            var (cur, _) = RegRead($@"HKLM\SYSTEM\CurrentControlSet\Services\{(string)op["svc"]!}", "Start");
            return cur is null ? null : Same(cur, (long)op["start"]!);
        }
        if (op["task"] is not null)
        {
            var t = Task((string)op["task"]!);
            return t is null ? null : (bool)t.Enabled == (bool)op["enabled"]!;
        }
        if (op["power"] is not null) return PowerAc(op) is { } v ? v == (uint)op["ac"]! : null;
        return (string?)op["special"] switch
        {
            "power-ultimate" => CpuAwake(),
            "hibernate-off" => RegRead(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled").value is int h ? h == 0 : null,
            "dynamictick" => Regex.IsMatch(Run("bcdedit.exe", "/enum {current}").output, @"disabledynamictick\s+(Yes|S[ií])", RegexOptions.IgnoreCase),
            _ => null,
        };
    }

    static string? BlockedReason(Def d)
    {
        // Riesgo bajo: un valor de registro, servicio o tarea con copia exacta del original. Lo demás, primero en la VM.
        if (d.Risk != "bajo" && !d.Tested && !Lab) return "Primero hay que probarlo en la máquina virtual.";
        if (d.Ops.Any(o => (string?)o!["special"] == "dynamictick") && BitLockerOn())
            return "BitLocker está activo en C:. Cambiar el arranque pediría la clave de recuperación.";
        return null;
    }

    /// <summary>Ajustes que Vigía aplicó (tienen copia del original) y que ya no están puestos: los deshizo Windows u otro programa.</summary>
    public static List<TweakView> Undone()
    {
        lock (Gate)
        {
            var bk = LoadBackup();
            return List().Where(t => bk.ContainsKey(t.Id) && (t.State == "off" || t.State == "partial")).ToList();
        }
    }

    // ---------- aplicar / revertir ----------
    public static TweakBatch Apply(IEnumerable<string> ids, bool restorePoint = true)
    {
        Backups.EnsureOriginal();   // antes del bloqueo: la copia original también lee los ajustes (mismo orden siempre)
        lock (Gate)
        {
            var defs = Catalog().Where(d => ids.Contains(d.Id)).ToList();
            var results = new List<TweakResult>();
            string? rp = null;
            var todo = defs.Where(d => BlockedReason(d) is null && StateOf(d) is not ("applied" or "na")).ToList();
            if (restorePoint && todo.Count > 0 && CatalogOverride is null)
            {
                var r = RestorePoint.Create("Vigía: antes de aplicar ajustes");
                rp = r.Message;
                if (!r.Ok) { Log($"punto de restauración: {r.Message}"); }
            }
            // copia de seguridad del estado de antes; sin copia no se cambia nada
            if (todo.Count > 0 && CatalogOverride is null &&
                Backups.Save("ajustes", todo.Count == 1 ? $"Aplicar: {todo[0].Title}" : $"Aplicar {todo.Count} ajustes",
                    todo.Select(SnapshotOf), rp) is null)
                return new(false, rp, todo.Select(d => new TweakResult(d.Id, false, StateOf(d), "No pude guardar la copia de seguridad; no cambié nada.")).ToArray(), false);
            var bk = LoadBackup();
            foreach (var d in defs)
            {
                if (BlockedReason(d) is string why) { results.Add(new(d.Id, false, StateOf(d), why)); continue; }
                if (StateOf(d) == "na") { results.Add(new(d.Id, true, "na", null)); continue; }   // no aplica en este PC: no se toca
                try
                {
                    if (!bk.ContainsKey(d.Id)) { bk[d.Id] = Capture(d); SaveBackup(bk); }   // el original, solo la primera vez
                    foreach (var op in d.Ops.Select(x => x!.AsObject())) ApplyOp(op, bk[d.Id]!.AsObject());
                    SaveBackup(bk);
                    var st = StateOf(d);
                    Log($"aplicar {d.Id}: {st}");
                    results.Add(new(d.Id, st is "applied" or "na", st, st is "applied" or "na" ? null : "Algunos valores no quedaron como se esperaba."));
                }
                catch (Exception e) { Log($"aplicar {d.Id}: ERROR {e.Message}"); results.Add(new(d.Id, false, StateOf(d), e.Message)); }
            }
            return new(results.All(r => r.Ok), rp, results.ToArray(), defs.Any(d => d.Reboot && results.Any(r => r.Id == d.Id && r.Ok)));
        }
    }

    public static TweakBatch Revert(IEnumerable<string> ids)
    {
        Backups.EnsureOriginal();
        lock (Gate)
        {
            var bk = LoadBackup();
            var undo = All().Where(d => ids.Contains(d.Id) && bk.ContainsKey(d.Id)).ToList();
            if (undo.Count > 0 && CatalogOverride is null &&
                Backups.Save("ajustes", undo.Count == 1 ? $"Revertir: {undo[0].Title}" : $"Revertir {undo.Count} ajustes", undo.Select(SnapshotOf)) is null)
                return new(false, null, undo.Select(d => new TweakResult(d.Id, false, StateOf(d), "No pude guardar la copia de seguridad; no cambié nada.")).ToArray(), false);
            var results = new List<TweakResult>();
            foreach (var d in All().Where(d => ids.Contains(d.Id)))
            {
                if (bk[d.Id] is not JsonObject saved)
                {
                    var now = StateOf(d);   // sin copia porque nunca se aplicó: si no aplica en este PC, no hay nada que revertir
                    results.Add(now is "na" ? new(d.Id, true, now, null) : new(d.Id, false, now, "No hay copia del valor original: Vigía no aplicó este ajuste."));
                    continue;
                }
                try
                {
                    var ops = d.Ops.Select(x => x!.AsObject()).ToList();
                    for (int i = ops.Count - 1; i >= 0; i--) if ((bool?)ops[i]["post"] != true) RevertOp(ops[i], saved, i);
                    for (int i = 0; i < ops.Count; i++) if ((bool?)ops[i]["post"] == true) RevertOp(ops[i], saved, i);
                    bk.Remove(d.Id); SaveBackup(bk);
                    Log($"revertir {d.Id}: {StateOf(d)}");
                    results.Add(new(d.Id, true, StateOf(d), null));
                }
                catch (Exception e) { Log($"revertir {d.Id}: ERROR {e.Message}"); results.Add(new(d.Id, false, StateOf(d), e.Message)); }
            }
            return new(results.All(r => r.Ok), null, results.ToArray(), All().Any(d => d.Reboot && ids.Contains(d.Id)));
        }
    }

    // ---------- para las copias de seguridad (Backups.cs) ----------
    internal static JsonObject SnapValue(string path, string name) => Snap(path, name);
    internal static void RestoreValue(string path, string name, JsonObject s) => Restore(path, name, s);

    internal static JsonObject? SnapshotItem(string id)
    {
        lock (Gate) return All().FirstOrDefault(d => d.Id == id) is { } d ? SnapshotOf(d) : null;
    }

    /// <summary>Estado actual de todos los ajustes del catálogo (para la copia «original»).</summary>
    internal static List<JsonObject> SnapshotAll()
    {
        lock (Gate) return Catalog().Select(SnapshotOf).ToList();
    }

    static JsonObject SnapshotOf(Def d) => new()
    {
        ["t"] = "tweak", ["id"] = d.Id, ["label"] = d.Title, ["saved"] = Capture(d),
        ["orig"] = LoadBackup()[d.Id]?.DeepClone(),   // el valor original de antes de Vigía, para que «Revertir» siga funcionando
    };

    /// <summary>Deja cada valor del ajuste como estaba en la copia (aplicado o no).</summary>
    internal static void RestoreSnapshot(JsonObject it)
    {
        lock (Gate)
        {
            var id = (string)it["id"]!;
            var d = All().FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Ese ajuste ya no está en el catálogo.");
            var saved = it["saved"]!.AsObject();
            var ops = d.Ops.Select(x => x!.AsObject()).ToList();
            for (int i = ops.Count - 1; i >= 0; i--)
            {
                var s = saved[i.ToString()] as JsonObject;
                switch ((string?)ops[i]["special"])
                {
                    // estos se restauran en los dos sentidos; RevertOp solo sabe volver al original
                    case "hibernate-off":
                        if (s is not null) Must(Run("powercfg.exe", (bool?)s["on"] == true ? "/hibernate on" : "/hibernate off"), "cambiar la hibernación");
                        break;
                    case "dynamictick":
                        if (s is not null) Must(Run("bcdedit.exe", (bool?)s["set"] == true ? "/set {current} disabledynamictick yes" : "/deletevalue {current} disabledynamictick"), "cambiar el arranque");
                        break;
                    case "power-ultimate":
                        if ((string?)s?["active"] is { Length: > 0 } prev) Must(Run("powercfg.exe", $"/setactive {prev}"), "volver al plan de energía de la copia");
                        break;
                    default: RevertOp(ops[i], saved, i); break;
                }
            }
            var bk = LoadBackup();
            if (StateOf(d) == "off") bk.Remove(id);
            else if (!bk.ContainsKey(id) && it["orig"] is JsonObject orig) bk[id] = orig.DeepClone();
            SaveBackup(bk);
            Log($"restaurar copia {id}: {StateOf(d)}");
        }
    }

    /// <summary>Guarda el valor actual de cada operación: {"0": {...}, "1": {...}, "power": {...}}.</summary>
    static JsonObject Capture(Def d)
    {
        var o = new JsonObject { ["at"] = DateTime.Now.ToString("s") };
        for (int i = 0; i < d.Ops.Count; i++)
        {
            var op = d.Ops[i]!.AsObject();
            if (op["reg"] is not null && op["kv"] is not null)
                o[i.ToString()] = new JsonObject { ["kvOld"] = KvGet(RegRead((string)op["reg"]!, (string)op["name"]!).value as string, (string)op["kv"]!) };
            else if (op["reg"] is not null) o[i.ToString()] = Snap((string)op["reg"]!, (string)op["name"]!);
            else if (op["svc"] is not null)
            {
                var k = $@"HKLM\SYSTEM\CurrentControlSet\Services\{(string)op["svc"]!}";
                o[i.ToString()] = new JsonObject { ["start"] = Snap(k, "Start"), ["delayed"] = Snap(k, "DelayedAutostart") };
            }
            else if (op["task"] is not null && Task((string)op["task"]!) is { } t) o[i.ToString()] = new JsonObject { ["enabled"] = (bool)t.Enabled };
            else if (op["power"] is not null && Active() is { } pg && PowerAc(op) is { } pv) o[i.ToString()] = new JsonObject { ["scheme"] = pg.ToString(), ["ac"] = pv };
            else if ((string?)op["special"] == "power-ultimate") o[i.ToString()] = new JsonObject { ["active"] = ActiveScheme() };
            else if ((string?)op["special"] == "hibernate-off") o[i.ToString()] = new JsonObject { ["on"] = Matches(op) == false };
            else if ((string?)op["special"] == "dynamictick") o[i.ToString()] = new JsonObject { ["set"] = Matches(op) == true };
        }
        return o;
    }

    static void ApplyOp(JsonObject op, JsonObject saved)
    {
        if (op["reg"] is not null)
        {
            if (op["kv"] is not null) KvWrite((string)op["reg"]!, (string)op["name"]!, (string)op["kv"]!, (string)op["value"]!);
            else if ((bool?)op["delete"] == true) RegDelete((string)op["reg"]!, (string)op["name"]!);
            else RegWrite((string)op["reg"]!, (string)op["name"]!, (string?)op["type"] ?? "dword", Target(op));
        }
        else if (op["svc"] is not null)
        {
            var k = $@"HKLM\SYSTEM\CurrentControlSet\Services\{(string)op["svc"]!}";
            if (RegRead(k, "Start").value is not null) SvcStart((string)op["svc"]!, (long)op["start"]!);
        }
        else if (op["task"] is not null && Task((string)op["task"]!) is { } t) t.Enabled = (bool)op["enabled"]!;
        else if (op["power"] is not null) { if (Active() is { } pg) PowerSetAc(pg, op, (uint)op["ac"]!); }
        else switch ((string?)op["special"])
        {
            case "power-ultimate":
                if (CpuAwake() == true) break;
                // Plan propio «Vigía»: copia de Máximo rendimiento (o de Alto rendimiento si Windows no la tiene) con el
                // procesador al 100 % enchufado. Así sirve aunque el usuario ya tenga un plan que se llame igual pero ahorre.
                var guid = FindVigia();
                if (guid is null)
                {
                    foreach (var src in new[] { "e9a42b02-d5df-448d-aa00-03f14749eb61", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "381b4222-f694-41f0-9685-ff5bb260df2e" })
                    {
                        guid = GuidRx.Match(Run("powercfg.exe", $"-duplicatescheme {src}").output).Value;
                        if (guid.Length > 0) break;
                    }
                    if (string.IsNullOrEmpty(guid)) throw new Exception("Windows no dejó crear el plan de energía de Vigía.");
                    saved["createdPlan"] = guid;
                    Run("powercfg.exe", $"-changename {guid} \"Vigía\" \"Plan de Vigía: enchufado, el procesador no baja de velocidad.\"");
                }
                Must(Run("powercfg.exe", $"/setacvalueindex {guid} 54533251-82be-4824-96c1-47b60b740d00 893dee8e-2bef-41e0-89c6-b55d0929964c 100"), "configurar el plan de energía");
                Must(Run("powercfg.exe", $"/setactive {guid}"), "activar el plan de energía");
                break;
            case "hibernate-off": Must(Run("powercfg.exe", "/hibernate off"), "apagar la hibernación"); break;
            case "dynamictick": Must(Run("bcdedit.exe", "/set {current} disabledynamictick yes"), "cambiar el arranque"); break;
            case "mouse-refresh": MouseRefresh(); break;
        }
    }

    static void RevertOp(JsonObject op, JsonObject saved, int i)
    {
        var s = saved[i.ToString()] as JsonObject;
        if (op["reg"] is not null && op["kv"] is not null && s is not null) KvWrite((string)op["reg"]!, (string)op["name"]!, (string)op["kv"]!, (string?)s["kvOld"]);
        else if (op["reg"] is not null && s is not null) Restore((string)op["reg"]!, (string)op["name"]!, s);
        else if (op["svc"] is not null && s is not null)
        {
            var k = $@"HKLM\SYSTEM\CurrentControlSet\Services\{(string)op["svc"]!}";
            var st = s["start"]!.AsObject();
            if (!JsonNode.DeepEquals(Snap(k, "Start"), st) && (bool?)st["existed"] == true && (string?)st["type"] == "dword")
                SvcStart((string)op["svc"]!, long.Parse((string)st["value"]!));
            try { Restore(k, "DelayedAutostart", s["delayed"]!.AsObject()); }
            catch (UnauthorizedAccessException)
            {
                if ((string?)s["delayed"]!["value"] == "1") Must(Run("sc.exe", $"config {(string)op["svc"]!} start= delayed-auto"), "restaurar el servicio");
            }
        }
        else if (op["task"] is not null && s is not null && Task((string)op["task"]!) is { } t) t.Enabled = (bool)s["enabled"]!;
        else if (op["power"] is not null && s is not null)
        {
            // al plan donde estaba; si ese plan ya no existe (p. ej. el de Vigía se borró), no hay nada que devolver
            if (Guid.TryParse((string?)s["scheme"], out var sg)) try { PowerSetAc(sg, op, (uint)s["ac"]!); } catch { }
        }
        else switch ((string?)op["special"])
        {
            case "power-ultimate":
                if ((string?)s?["active"] is { Length: > 0 } prev) Must(Run("powercfg.exe", $"/setactive {prev}"), "volver al plan anterior");
                if ((string?)saved["createdPlan"] is { Length: > 0 } made) Run("powercfg.exe", $"-delete {made}");
                break;
            case "hibernate-off": if ((bool?)s?["on"] == true) Must(Run("powercfg.exe", "/hibernate on"), "encender la hibernación"); break;
            case "dynamictick": if ((bool?)s?["set"] != true) Must(Run("bcdedit.exe", "/deletevalue {current} disabledynamictick"), "restaurar el arranque"); break;
            case "mouse-refresh": MouseRefresh(); break;
        }
    }

    // ---------- registro ----------
    static (RegistryKey root, string sub) Split(string path)
    {
        var i = path.IndexOf('\\');
        var root = path[..i] switch
        {
            "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
            "HKU" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
            var h => throw new ArgumentException($"Raíz de registro no admitida: {h}"),
        };
        return (root, path[(i + 1)..]);
    }

    static (object? value, RegistryValueKind kind) RegRead(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root) using (var k = root.OpenSubKey(sub))
        {
            if (k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } v) return (null, RegistryValueKind.Unknown);
            return (v, k.GetValueKind(name));
        }
    }

    static void RegWrite(string path, string name, string type, object value)
    {
        var (root, sub) = Split(path);
        using (root) using (var k = root.CreateSubKey(sub, true))
            switch (type)
            {
                case "dword": k.SetValue(name, unchecked((int)(uint)Convert.ToInt64(value)), RegistryValueKind.DWord); break;
                case "qword": k.SetValue(name, Convert.ToInt64(value), RegistryValueKind.QWord); break;
                case "string": k.SetValue(name, value.ToString()!, RegistryValueKind.String); break;
                case "binary": k.SetValue(name, Convert.FromHexString(value.ToString()!), RegistryValueKind.Binary); break;
                default: throw new ArgumentException($"Tipo no admitido: {type}");
            }
    }

    // Inicio de un servicio: primero por registro (copia exacta); si la clave está protegida (p. ej. TrkWks en 26H2),
    // por el Administrador de servicios, que es quien tiene permiso de verdad.
    static void SvcStart(string svc, long start)
    {
        try { RegWrite($@"HKLM\SYSTEM\CurrentControlSet\Services\{svc}", "Start", "dword", start); }
        catch (UnauthorizedAccessException)
        {
            var mode = start switch { 2 => "auto", 3 => "demand", 4 => "disabled", 1 => "system", 0 => "boot", _ => throw new Exception($"Inicio no admitido: {start}") };
            Must(Run("sc.exe", $"config {svc} start= {mode}"), $"cambiar el servicio {svc}");
        }
    }

    static void RegDelete(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root) using (var k = root.OpenSubKey(sub, true)) k?.DeleteValue(name, false);
    }

    static JsonObject Snap(string path, string name)
    {
        var (v, kind) = RegRead(path, name);
        if (v is null) return new JsonObject { ["existed"] = false };
        var type = kind switch { RegistryValueKind.DWord => "dword", RegistryValueKind.QWord => "qword", RegistryValueKind.Binary => "binary", _ => "string" };
        string val = v switch { int n => ((uint)n).ToString(), long n => n.ToString(), byte[] b => Convert.ToHexString(b), _ => v.ToString()! };
        // REG_EXPAND_SZ y REG_MULTI_SZ: se guardan tal cual para no perder el tipo
        if (kind is RegistryValueKind.ExpandString or RegistryValueKind.MultiString)
            return new JsonObject { ["existed"] = true, ["type"] = kind.ToString(), ["value"] = v is string[] a ? string.Join("\0", a) : val };
        return new JsonObject { ["existed"] = true, ["type"] = type, ["value"] = val };
    }

    static void Restore(string path, string name, JsonObject s)
    {
        // si ya está como estaba no se escribe: algunas claves (p. ej. servicios protegidos) niegan la escritura aunque no cambie nada
        if (JsonNode.DeepEquals(Snap(path, name), s)) return;
        if ((bool?)s["existed"] != true) { RegDelete(path, name); return; }
        var type = (string)s["type"]!; var val = (string)s["value"]!;
        if (type is "ExpandString" or "MultiString")
        {
            var (root, sub) = Split(path);
            using (root) using (var k = root.CreateSubKey(sub, true))
                k.SetValue(name, type == "MultiString" ? val.Split('\0') : val, type == "MultiString" ? RegistryValueKind.MultiString : RegistryValueKind.ExpandString);
            return;
        }
        RegWrite(path, name, type, type is "dword" or "qword" ? long.Parse(val) : val);
    }

    // ---------- listas «clave=valor;» (p. ej. DirectXUserGlobalSettings) ----------
    // Se cambia una sola clave y se respetan las demás: escribir la cadena entera borraría lo que puso Windows u otra app.
    static List<(string k, string v)> KvParse(string? s) => (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).Where(p => p[0].Trim().Length > 0).Select(p => (p[0].Trim(), p.Length > 1 ? p[1].Trim() : "")).ToList();

    internal static string? KvGet(string? s, string key) =>
        KvParse(s).Where(p => p.k.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => p.v).Cast<string?>().FirstOrDefault();

    internal static string KvSet(string? s, string key, string? value)
    {
        var list = KvParse(s);
        var i = list.FindIndex(p => p.k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (value is null) { if (i >= 0) list.RemoveAt(i); }
        else if (i >= 0) list[i] = (list[i].k, value);
        else list.Add((key, value));
        return string.Concat(list.Select(p => $"{p.k}={p.v};"));
    }

    static void KvWrite(string path, string name, string key, string? value)
    {
        var cur = RegRead(path, name).value as string;
        var next = KvSet(cur, key, value);
        if (next.Length == 0) RegDelete(path, name); else RegWrite(path, name, "string", next);
    }

    static object Target(JsonObject op)
    {
        var v = op["value"]!;
        if (v.GetValueKind() == JsonValueKind.String && (string)v! == "$ramKB")
            return (long)Math.Round(Memory.Status().TotalGb * 1024 * 1024);
        return v.GetValueKind() == JsonValueKind.Number ? (object)(long)v : (string)v!;
    }

    static bool Same(object cur, object target) => (cur, target) switch
    {
        (int c, long t) => (uint)c == (uint)t,
        (long c, long t) => c == t,
        (byte[] c, string t) => Convert.ToHexString(c).Equals(t, StringComparison.OrdinalIgnoreCase),
        (string c, string t) => c == t,
        (string c, long t) => c == t.ToString(),
        _ => false,
    };

    // ---------- tareas, energía, arranque ----------
    static dynamic? Task(string path)
    {
        try
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            svc.Connect();
            var i = path.LastIndexOf('\\');
            return svc.GetFolder(i <= 0 ? "\\" : path[..i]).GetTask(path[(i + 1)..]);
        }
        catch { return null; }
    }

    static readonly Regex GuidRx = new(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
    static readonly Regex UltimateName = new(@"M.ximo rendimiento|Ultimate Performance", RegexOptions.IgnoreCase);
    static string ActiveScheme() => GuidRx.Match(Run("powercfg.exe", "/getactivescheme").output).Value;
    // Lo que importa del plan no es el nombre: es que el procesador no baje del 100 % enchufado.
    // Así cuentan Alto rendimiento, Máximo rendimiento y los planes propios (de la placa o de otro programa).
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

    /// <summary>Valor enchufado de un ajuste del plan activo. «power»: "subgrupo/ajuste".</summary>
    static uint? PowerAc(JsonObject op)
    {
        if (Active() is not { } g) return null;
        var p = ((string)op["power"]!).Split('/');
        Guid sub = new(p[0]), set = new(p[1]);
        return PowerReadACValueIndex(IntPtr.Zero, ref g, ref sub, ref set, out var v) == 0 ? v : null;
    }
    static void PowerSetAc(Guid scheme, JsonObject op, uint value)
    {
        var p = ((string)op["power"]!).Split('/');
        Guid sub = new(p[0]), set = new(p[1]);
        var r = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, value);
        if (r != 0) throw new Exception("Windows no dejó cambiar el plan de energía.");
        // el cambio no cuenta hasta que el plan se vuelve a activar
        if (Active() is { } a && a == scheme) PowerSetActiveScheme(IntPtr.Zero, ref scheme);
    }
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[]? buf, ref uint size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    static Guid? Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(p); } finally { LocalFree(p); }
    }
    static bool? CpuAwake()
    {
        if (Active() is not { } g) return null;
        Guid sub = new("54533251-82be-4824-96c1-47b60b740d00"), min = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        return PowerReadACValueIndex(IntPtr.Zero, ref g, ref sub, ref min, out var v) == 0 ? v >= 100 : null;
    }
    /// <summary>Nombre del plan activo tal como lo muestra Windows («Equilibrado», «Alto rendimiento», …).</summary>
    public static string? PlanName()
    {
        if (Active() is not { } g) return null;
        uint n = 0; PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, null, ref n);
        if (n == 0) return null;
        var b = new byte[n];
        return PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, b, ref n) == 0 ? System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0') : null;
    }
    static readonly Regex VigiaName = new(@"\(Vig.a\)\s*\*?\s*$", RegexOptions.IgnoreCase);
    static string? FindVigia() => Run("powercfg.exe", "/list").output.Split('\n').Where(l => VigiaName.IsMatch(l.Trim()))
        .Select(l => GuidRx.Match(l).Value).FirstOrDefault(g => g.Length > 0);
    static string? FindUltimate() => Run("powercfg.exe", "/list").output.Split('\n').Where(l => UltimateName.IsMatch(l))
        .Select(l => GuidRx.Match(l).Value).FirstOrDefault(g => g.Length > 0);

    static bool BitLockerOn()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
                "SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='C:'");
            return s.Get().Cast<ManagementObject>().Any(v => Convert.ToInt32(v["ProtectionStatus"]) == 1);
        }
        catch { return false; }
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);
    /// <summary>Aplica la aceleración del ratón al momento, sin cerrar sesión.</summary>
    static void MouseRefresh()
    {
        int R(string n) => int.TryParse(RegRead(@"HKCU\Control Panel\Mouse", n).value?.ToString(), out var v) ? v : 0;
        SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, [R("MouseThreshold1"), R("MouseThreshold2"), R("MouseSpeed")], 0x3);
    }

    internal static (int exit, string output) Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60000)) { try { p.Kill(true); } catch { } return (-1, "tardó más de 60 s"); }
        System.Threading.Tasks.Task.WaitAll([so, se], 5000);
        return (p.ExitCode, (so.IsCompleted ? so.Result : "") + (se.IsCompleted ? se.Result : ""));
    }

    static void Must((int exit, string output) r, string what)
    {
        if (r.exit != 0) throw new Exception($"No se pudo {what}: {r.output.Trim()}");
    }

    // ---------- copia y registro ----------
    static JsonObject LoadBackup()
    {
        try { return JsonNode.Parse(File.ReadAllText(BackupPath))!.AsObject(); } catch { return new JsonObject(); }
    }

    static void SaveBackup(JsonObject o)
    {
        Directory.CreateDirectory(Settings.Dir);
        var tmp = BackupPath + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, BackupPath, true);   // escritura atómica: nunca queda una copia a medias
    }

    static void Log(string line)
    {
        try { Directory.CreateDirectory(Settings.Dir); File.AppendAllText(LogPath, $"{DateTime.Now:s} {line}{Environment.NewLine}"); } catch { }
    }

    // ---------- autoprueba ----------
    /// <summary>Prueba el motor sobre HKCU\Software\Vigia\SelfTest: crea, cambia y borra valores y comprueba que Revertir deja todo igual.</summary>
    public static string SelfTest()
    {
        const string K = @"HKCU\Software\Vigia\SelfTest";
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add($"{(ok ? "OK  " : "FAIL")} {what}");
        var realBackup = File.Exists(BackupPath) ? File.ReadAllText(BackupPath) : null;
        try
        {
            RegDelete(K, "nuevo"); RegWrite(K, "existente", "dword", 7L); RegWrite(K, "texto", "string", "original"); RegWrite(K, "borrar", "dword", 1L);
            RegWrite(K, "lista", "string", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;");
            CatalogOverride = () => [JsonNode.Parse($$"""
                {"id":"st","group":"t","title":"t","detail":"t","tested":true,"ops":[
                  {"reg":"{{K.Replace("\\", "\\\\")}}","name":"nuevo","type":"dword","value":4294967295},
                  {"reg":"{{K.Replace("\\", "\\\\")}}","name":"existente","type":"dword","value":0},
                  {"reg":"{{K.Replace("\\", "\\\\")}}","name":"texto","type":"string","value":"cambiado"},
                  {"reg":"{{K.Replace("\\", "\\\\")}}","name":"borrar","delete":true},
                  {"reg":"{{K.Replace("\\", "\\\\")}}","name":"lista","kv":"GpuPreference","value":"2"},
                  {"reg":"{{K.Replace("\\", "\\\\")}}","name":"lista","kv":"VRROptimizeEnable","value":"1"},
                  {"svc":"VigiaServicioQueNoExiste","start":4}]}
                """)!.AsObject()];
            Check(List()[0].State == "off", "estado inicial = off");
            var a = Apply(["st"]);
            Check(a.Ok && a.Results[0].State == "applied", $"aplicar -> {a.Results[0].State} {a.Results[0].Error}");
            Check(RegRead(K, "nuevo").value is int n && (uint)n == 0xFFFFFFFF, "dword 0xFFFFFFFF escrito");
            Check(RegRead(K, "borrar").value is null, "valor borrado");
            Check((string?)RegRead(K, "lista").value == "SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;GpuPreference=2;", $"lista: cambia una clave y respeta las demás ({RegRead(K, "lista").value})");
            var a2 = Apply(["st"]);
            Check(a2.Ok, "aplicar dos veces no pisa la copia original");
            var r = Revert(["st"]);
            Check(r.Ok && r.Results[0].State == "off", $"revertir -> {r.Results[0].State}");
            Check(RegRead(K, "nuevo").value is null, "valor nuevo eliminado al revertir");
            Check(RegRead(K, "existente").value is 7, "dword original restaurado (7)");
            Check((string?)RegRead(K, "texto").value == "original", "texto original restaurado");
            Check(RegRead(K, "borrar").value is 1, "valor borrado vuelve a existir");
            Check((string?)RegRead(K, "lista").value == "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;", $"lista restaurada ({RegRead(K, "lista").value})");
            Check(!LoadBackup().ContainsKey("st"), "copia eliminada tras revertir");
            Check(Revert(["st"]).Results[0].Error is not null, "revertir sin copia avisa en vez de inventar");

            // ajuste del plan de energía: «apagar la pantalla tras…» (inofensivo), +60 s y de vuelta
            var vop = new JsonObject { ["power"] = "7516b95f-f776-4464-8c53-06167f40cc99/3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", ["ac"] = 0 };
            if (PowerAc(vop) is { } was)
            {
                vop["ac"] = was + 60;
                CatalogOverride = () => [new JsonObject { ["id"] = "stp", ["group"] = "t", ["title"] = "t", ["detail"] = "t", ["tested"] = true, ["ops"] = new JsonArray(vop.DeepClone()) }];
                var pa = Apply(["stp"]);
                Check(pa.Ok && PowerAc(vop) == was + 60, $"plan de energía: aplicar ({PowerAc(vop)})");
                var pr = Revert(["stp"]);
                Check(pr.Ok && PowerAc(vop) == was, $"plan de energía: vuelve al valor de antes ({PowerAc(vop)} = {was})");
            }
            else Check(false, "plan de energía: no se pudo leer");
        }
        catch (Exception e) { Check(false, $"excepción: {e}"); }
        finally
        {
            CatalogOverride = null;
            if (realBackup is null) File.Delete(BackupPath); else File.WriteAllText(BackupPath, realBackup);
            var (root, sub) = Split(K);
            using (root) root.DeleteSubKeyTree(sub, false);
        }
        var pass = log.All(l => l.StartsWith("OK"));
        return string.Join(Environment.NewLine, log) + Environment.NewLine + (pass ? "PASS" : "FAIL");
    }
}

/// <summary>Puntos de restauración del sistema (WMI root\default:SystemRestore).</summary>
public static class RestorePoint
{
    public record Result(bool Ok, string Message, bool Off = false);

    /// <summary>Activa la protección del sistema en C: (lo mismo que «Configurar» en Propiedades del sistema).</summary>
    public static Result EnableC()
    {
        try
        {
            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            var p = cls.GetMethodParameters("Enable");
            p["Drive"] = @"C:\";
            var rv = Convert.ToUInt32(cls.InvokeMethod("Enable", p, null)["ReturnValue"]);
            return rv == 0 ? new(true, "Protección del sistema activada en C:.") : new(false, $"Windows no activó la protección del sistema (código {rv}).");
        }
        catch (Exception e) { return new(false, $"No se pudo activar la protección del sistema: {e.Message}"); }
    }

    public static int Count()
    {
        try { using var s = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber FROM SystemRestore"); return s.Get().Count; }
        catch { return -1; }
    }

    /// <summary>True si C: no tiene la protección del sistema activada (RPSessionInterval = 0 o sin configurar).</summary>
    public static bool ProtectionOff()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            if (k?.GetValue("RPSessionInterval") is int i && i == 0) return true;
            using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT * FROM Win32_ShadowStorage");
            foreach (ManagementObject o in s.Get())
                if (((string?)o["Volume"])?.Length > 0) return false;   // hay almacenamiento de instantáneas configurado
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Crea el punto con un límite de tiempo: en algunos equipos (VSS ocupado, disco lento) Windows se queda
    /// minutos sin responder. Pasado el límite seguimos: la copia propia de Vigía ya cubre «Volver a como estaba».
    /// </summary>
    public static Result Create(string description) => Create(description, TimeSpan.FromMinutes(3));

    public static Result Create(string description, TimeSpan limit)
    {
        var t = Task.Run(() => CreateNow(description));
        return t.Wait(limit) ? t.Result
            : new(false, "Windows tardó demasiado en crear el punto de restauración; seguimos con la copia de Vigía.");
    }

    static Result CreateNow(string description)
    {
        const string K = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        object? prevFreq = null;
        try
        {
            var before = Count();
            // Windows solo crea un punto cada 24 h salvo que se baje esta frecuencia; se restaura después.
            using (var k = Registry.LocalMachine.CreateSubKey(K, true)) { prevFreq = k.GetValue("SystemRestorePointCreationFrequency"); k.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord); }
            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            var p = cls.GetMethodParameters("CreateRestorePoint");
            p["Description"] = description; p["RestorePointType"] = 12; p["EventType"] = 100;
            var rv = Convert.ToUInt32(cls.InvokeMethod("CreateRestorePoint", p, null)["ReturnValue"]);
            if (rv == 1058) return new(false, "La protección del sistema está apagada en C:.", Off: true);
            if (rv != 0) return new(false, $"Windows no creó el punto de restauración (código {rv}).");
            // Windows tarda unos segundos en listar el punto nuevo
            for (var i = 0; i < 20 && Count() <= before; i++) System.Threading.Thread.Sleep(1000);
            if (Count() > before) return new(true, "Punto de restauración creado.");
            // con la protección apagada Windows a veces responde 0 sin crear nada
            return ProtectionOff() ? new(false, "La protección del sistema está apagada en C:.", Off: true)
                                   : new(false, "Windows aceptó la orden pero no apareció un punto nuevo.");
        }
        catch (Exception e)
        {
            if (ProtectionOff()) return new(false, "La protección del sistema está apagada en C:.", Off: true);
            return new(false, $"No se pudo crear el punto de restauración: {(string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message)}");
        }
        finally
        {
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey(K, true);
                if (prevFreq is null) k.DeleteValue("SystemRestorePointCreationFrequency", false); else k.SetValue("SystemRestorePointCreationFrequency", prevFreq, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
