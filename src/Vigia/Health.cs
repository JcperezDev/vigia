using System.Management;

namespace Vigia;

public sealed record SmartAttr(int Id, string Name, long Raw, string Level, string Hint);
public sealed record DiskHealth(
    int Number, string Model, string Media, string Bus, double SizeGb, IReadOnlyList<string> Letters,
    string Level, string Verdict, IReadOnlyList<string> Issues,
    double? Temp, double? WarnTemp, double? LifePct, double? AvailableSpare, double? SpareThreshold,
    double? PowerOnHours, double? PowerCycles, double? WrittenTb, double? ReadTb,
    IReadOnlyList<SmartAttr> Attributes);

/// <summary>Salud de discos (solo lectura): Storage API de Windows + SMART NVMe (LHM) + atributos SMART ATA (WMI root\wmi).</summary>
public static class Health
{
    // Atributos ATA que importan para el veredicto. Cualquier valor > 0 en 5/197/198 indica sectores dañados.
    static readonly (int id, string name, string level, string hint)[] AtaWatch =
    {
        (5,   "Sectores reasignados",        "warn", "El disco ya reemplazó sectores dañados. Si sube, haz copia de seguridad."),
        (197, "Sectores pendientes",         "warn", "Sectores ilegibles esperando reasignación. Copia lo importante."),
        (198, "Sectores irrecuperables",     "bad",  "Hay datos que el disco no pudo leer. Copia de seguridad ya."),
        (199, "Errores de cable (CRC)",      "info", "Errores de transmisión SATA. Si aumentan, cambia o reconecta el cable."),
        (10,  "Reintentos de arranque",      "warn", "El motor tuvo que reintentar girar."),
        (187, "Errores no corregidos",       "warn", "Errores que el ECC no pudo corregir."),
        (188, "Timeouts de comando",         "info", "Comandos que tardaron demasiado; suele ser cable o fuente."),
    };

    static List<ManagementBaseObject> Wmi(string scope, string q)
    {
        var list = new List<ManagementBaseObject>();
        try { using var s = new ManagementObjectSearcher(scope, q); foreach (var o in s.Get()) list.Add(o); } catch { }
        return list;
    }

    const string StorageNs = @"root\Microsoft\Windows\Storage";

    /// <summary>Número de disco -> letras de unidad.</summary>
    public static Dictionary<int, List<string>> LettersByDisk()
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var p in Wmi(StorageNs, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
        {
            var c = p["DriveLetter"] switch { char ch => ch, ushort u => (char)u, _ => '\0' };
            if (!char.IsLetter(c)) continue;
            var n = Convert.ToInt32(p["DiskNumber"]);
            (map.TryGetValue(n, out var l) ? l : map[n] = new()).Add($"{c}:");
        }
        return map;
    }

    /// <summary>Número de disco -> tipo de medio ("SSD"/"HDD"/…).</summary>
    public static Dictionary<int, string> MediaByDisk() =>
        Wmi(StorageNs, "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk")
            .Where(d => int.TryParse(d["DeviceId"]?.ToString(), out _))
            .ToDictionary(d => int.Parse(d["DeviceId"].ToString()!), d => MediaName(Convert.ToInt32(d["MediaType"] ?? 0)));

    static string MediaName(int m) => m switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Desconocido" };
    static string BusName(int b) => b switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", _ => "Otro" };

    /// <summary>Atributos SMART ATA por fragmento de modelo (InstanceName trae "Prod_&lt;modelo truncado&gt;").</summary>
    static List<(string prod, bool predict, Dictionary<int, long> raw)> AtaSmart()
    {
        var predict = new Dictionary<string, bool>();
        foreach (var s in Wmi(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus"))
            predict[(string)s["InstanceName"]] = s["PredictFailure"] is true;
        var res = new List<(string, bool, Dictionary<int, long>)>();
        foreach (var d in Wmi(@"root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData"))
        {
            var inst = (string)d["InstanceName"];
            if (d["VendorSpecific"] is not byte[] v) continue;
            var raw = new Dictionary<int, long>();
            // Tabla SMART: 30 entradas de 12 bytes desde el offset 2: id, flags(2), actual, peor, raw(6), reservado
            for (var i = 2; i + 11 < v.Length && i < 362; i += 12)
            {
                if (v[i] == 0) continue;
                long r = 0;
                for (var b = 5; b >= 0; b--) r = (r << 8) | v[i + 5 + b];
                raw[v[i]] = r;
            }
            var prod = inst.Contains("Prod_") ? inst[(inst.IndexOf("Prod_") + 5)..].Split('\\', '&')[0] : inst;
            res.Add((Telemetry.NormKey(prod), predict.GetValueOrDefault(inst), raw));
        }
        return res;
    }

    public static List<DiskHealth> Scan(Telemetry tel)
    {
        var letters = LettersByDisk();
        var sensors = tel.StorageSensors();
        var ata = AtaSmart();
        var list = new List<DiskHealth>();

        foreach (var d in Wmi(StorageNs, "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, OperationalStatus, Size FROM MSFT_PhysicalDisk"))
        {
            var num = int.Parse(d["DeviceId"].ToString()!);
            var model = ((string?)d["FriendlyName"])?.Trim() ?? $"Disco {num}";
            var media = MediaName(Convert.ToInt32(d["MediaType"] ?? 0));
            var bus = BusName(Convert.ToInt32(d["BusType"] ?? 0));
            var hs = Convert.ToInt32(d["HealthStatus"] ?? 5);
            var issues = new List<string>();
            var level = hs switch { 0 => "ok", 1 => "warn", 2 => "bad", _ => "info" };
            if (hs == 1) issues.Add("Windows marca el disco con advertencia de salud.");
            if (hs == 2) issues.Add("Windows marca el disco como en mal estado.");

            // Sensores LHM por modelo
            var key = Telemetry.NormKey(model);
            var s = sensors.FirstOrDefault(kv => Telemetry.NormKey(kv.Key) == key).Value
                    ?? sensors.FirstOrDefault(kv => Telemetry.NormKey(kv.Key).Contains(key) || key.Contains(Telemetry.NormKey(kv.Key))).Value
                    ?? new();
            double? G(string k) => s.TryGetValue(k, out var v) ? Math.Round(v, 1) : null;
            var temp = G("Temperature|Composite Temperature") ?? G("Temperature|Temperature");
            var warnT = G("Temperature|Warning Temperature");
            var life = G("Level|Life");
            var spare = G("Level|Available Spare");
            var spareTh = G("Level|Available Spare Threshold");
            var hours = G("Factor|Power On Hours");
            var cycles = G("Factor|Power On Count");
            var wr = G("Data|Data Written") is double w ? Math.Round(w / 1000, 2) : (double?)null;   // LHM lo da en GB
            var rd = G("Data|Data Read") is double r ? Math.Round(r / 1000, 2) : (double?)null;

            if (life is double lp && lp <= 10) { issues.Add($"Le queda {lp:0} % de vida útil."); level = Worst(level, lp <= 3 ? "bad" : "warn"); }
            if (spare is double sp && spareTh is double th && sp <= th) { issues.Add("Bloques de reserva por debajo del umbral."); level = Worst(level, "bad"); }
            if (temp is double tc && warnT is double wt && tc >= wt) { issues.Add($"Temperatura {tc:0} °C por encima del aviso ({wt:0} °C)."); level = Worst(level, "warn"); }
            else if (temp is double tc2 && warnT is null && media == "HDD" && tc2 >= 55) { issues.Add($"Disco mecánico a {tc2:0} °C; lo ideal es menos de 50 °C."); level = Worst(level, "warn"); }

            // SMART ATA (discos SATA)
            var attrs = new List<SmartAttr>();
            var a = ata.FirstOrDefault(x => x.prod.Length >= 6 && key.StartsWith(x.prod));
            if (a.raw is not null)
            {
                hours ??= a.raw.TryGetValue(9, out var h9) ? h9 & 0xFFFFFFFF : null;
                cycles ??= a.raw.TryGetValue(12, out var c12) ? c12 & 0xFFFFFFFF : null;
                if (a.predict) { issues.Add("El firmware del disco predice una falla (SMART)."); level = Worst(level, "bad"); }
                foreach (var (id, name, lv, hint) in AtaWatch)
                {
                    if (!a.raw.TryGetValue(id, out var val)) continue;
                    val &= 0xFFFFFFFF;
                    var l = val > 0 ? lv : "ok";
                    attrs.Add(new SmartAttr(id, name, val, l, val > 0 ? hint : ""));
                    if (val > 0)
                    {
                        if (lv != "info") issues.Add($"{name}: {val}.");
                        level = Worst(level, lv);
                    }
                }
            }

            var verdict = level switch
            {
                "ok" => "Saludable",
                "info" => "Saludable, con detalles",
                "warn" => "Vigilar",
                _ => "En riesgo",
            };
            list.Add(new DiskHealth(num, model, media, bus, Math.Round(Convert.ToDouble(d["Size"] ?? 0) / 1e9, 0),
                letters.GetValueOrDefault(num) ?? new(), level, verdict, issues,
                temp, warnT, life, spare, spareTh, hours, cycles, wr, rd, attrs));
        }
        return list.OrderBy(x => x.Letters.FirstOrDefault() ?? "Z").ToList();
    }

    static readonly string[] Order = { "ok", "info", "warn", "bad" };
    internal static string Worst(string a, string b) => Array.IndexOf(Order, b) > Array.IndexOf(Order, a) ? b : a;
}
