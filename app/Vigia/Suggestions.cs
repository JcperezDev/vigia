using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>Una sugerencia: qué pasa (con lo medido), por qué importa y qué hacer. Go = sección de Vigía que ayuda.</summary>
/// Why y Do van frase a frase: la interfaz traduce cada frase por separado.
public sealed record Suggestion(string Id, string Level, string Title, IReadOnlyList<string> Why, IReadOnlyList<string> Do, string? Go, IReadOnlyList<string> Facts);
public sealed record PingStats(string Target, int Sent, int Lost, double? Avg, double? Min, double? Max, double? Jitter);
public sealed record WifiFacts(string Ssid, int Quality, int? Rssi, double? FreqGhz, string Band, string Phy, double RxMbps, int SameChannel);
public sealed record NetFacts(string Kind, string Adapter, long LinkMbps, WifiFacts? Wifi, PingStats? Gateway, PingStats? Internet, IReadOnlyList<string> SpareEthernet);
public sealed record SuggestReport(IReadOnlyList<Suggestion> Items, NetFacts? Net, long Ts);

/// <summary>
/// Sugerencias de solo lectura. Mira la conexión, la pantalla, la RAM, los discos y el plan de energía y propone
/// cambios con su motivo. No cambia nada ni corre en segundo plano: solo cuando se abre la sección o se pide.
/// </summary>
public static class Suggestions
{
    static string Ms(double? v) => v is null ? "—" : v < 1 ? "<1 ms" : $"{Math.Round(v.Value)} ms";
    static string P(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    public static SuggestReport Run(Telemetry tel)
    {
        var items = new List<Suggestion>();
        NetFacts? net = null;
        try { net = Environment.GetEnvironmentVariable("VIGIA_FAKE_NET") == "wifi" ? FakeWifi() : ReadNet(); items.AddRange(Network(net)); } catch { }
        try { items.AddRange(Screen()); } catch { }
        try { items.AddRange(RamSpeed()); } catch { }
        try { items.AddRange(Disks(tel)); } catch { }
        try { items.AddRange(Power()); } catch { }
        try { items.AddRange(Battery()); } catch { }
        try { items.AddRange(Usage(tel.Read())); } catch { }
        try { items.AddRange(InputLag()); } catch { }
        var order = new Dictionary<string, int> { ["bad"] = 0, ["warn"] = 1, ["info"] = 2, ["ok"] = 3 };
        return new(items.OrderBy(i => order.GetValueOrDefault(i.Level, 9)).ToList(), net, DateTimeOffset.Now.ToUnixTimeMilliseconds());
    }

    // ======================= red =======================

    /// <summary>Decide qué sugerir con lo medido. Separado de la medición para poder probarlo con datos inventados.</summary>
    public static List<Suggestion> Network(NetFacts n)
    {
        var res = new List<Suggestion>();
        if (n.Kind == "none") return res;
        var gw = n.Gateway; var inet = n.Internet;
        bool LostAny(PingStats? p) => p is { Sent: > 0 } && p.Lost > 0;
        double LossPct(PingStats? p) => p is { Sent: > 0 } ? 100.0 * p.Lost / p.Sent : 0;

        var facts = new List<string>();
        if (n.Wifi is { } wf)
        {
            facts.Add($"Señal Wi-Fi: {wf.Quality} %" + (wf.Rssi is { } r ? $" ({r} dBm)" : ""));
            if (wf.Band.Length > 0) facts.Add($"Banda: {wf.Band}");
            if (wf.RxMbps > 0) facts.Add($"Velocidad del enlace: {Math.Round(wf.RxMbps)} Mbps");
        }
        else if (n.LinkMbps > 0) facts.Add($"Velocidad del enlace: {(n.LinkMbps >= 1000 ? $"{n.LinkMbps / 1000.0:0.#} Gbps" : $"{n.LinkMbps} Mbps")}");
        if (gw is { Sent: > 0 }) facts.Add($"Al router: {gw.Sent - gw.Lost} de {gw.Sent} respuestas, media {Ms(gw.Avg)}, picos de {Ms(gw.Max)}, variación {Ms(gw.Jitter)}");
        if (inet is { Sent: > 0 }) facts.Add($"A internet: {inet.Sent - inet.Lost} de {inet.Sent} respuestas, media {Ms(inet.Avg)}, picos de {Ms(inet.Max)}, variación {Ms(inet.Jitter)}");

        if (n.Kind == "wifi")
        {
            var w = n.Wifi;
            var weak = w is not null && (w.Quality < 60 || w.Rssi is < -70);
            var gwBad = LostAny(gw) || gw?.Jitter > 5 || gw?.Max > 40 || gw?.Avg > 10;
            var netBad = LossPct(inet) >= 5 || inet?.Jitter > 15;

            if (gwBad || weak)
            {
                var why = new List<string>();
                if (LostAny(gw)) why.Add($"Entre tu PC y el router se perdieron {gw!.Lost} de {gw.Sent} respuestas.");
                if (gw?.Max > 40 || gw?.Jitter > 5) why.Add($"La espera hasta el router salta de {Ms(gw!.Min)} a {Ms(gw.Max)}; por cable se queda por debajo de 1 ms y casi no varía.");
                if (weak) why.Add($"La señal llega al {w!.Quality} %: con paredes o distancia, el Wi-Fi reenvía paquetes y eso se nota como tirones.");
                why.Add("En juegos en línea eso es lag y saltos aunque tengas muchos FPS.");
                var todo = new List<string> { "Conecta el PC al router con un cable de red (Cat 6 sirve).", "Si el router está lejos, un adaptador por el enchufe (Powerline) o un repetidor con puerto de red cerca del PC." };
                if (n.SpareEthernet.Count > 0) todo.Add($"Tu PC ya tiene puerto de red: {n.SpareEthernet[0]}.");
                res.Add(new("net-cable", "warn", "Tu Wi-Fi no es estable: usa cable de red", why, todo, null, facts));
            }
            else if (netBad)
                res.Add(new("net-isp", "info", "La conexión falla después del router",
                    [$"Tu Wi-Fi responde bien hasta el router, pero hacia internet se perdió el {Math.Round(LossPct(inet))} % o la espera varía {Ms(inet!.Jitter)}.", "El cable no lo arregla: el problema está en el router o en tu proveedor."],
                    ["Reinicia el router, revisa si alguien en casa está descargando o viendo vídeo en alta calidad y, si sigue, llama a tu proveedor con estos datos."], null, facts));
            else
                res.Add(new("net-ok", "ok", "Tu Wi-Fi está estable",
                    ["En la prueba no se perdió nada y la espera casi no varió."],
                    ["Para jugar en línea el cable sigue siendo lo más fiable, pero ahora mismo no te hace falta."], null, facts));

            if (w is { Band: "2,4 GHz" })
                res.Add(new("net-5ghz", "info", "Estás en la banda de 2,4 GHz",
                    w.SameChannel > 0
                        ? ["Es la banda más llena: la comparten las redes vecinas, el Bluetooth y hasta el microondas.", $"Redes vecinas en tu mismo canal ahora mismo: {w.SameChannel}.", "La de 5 GHz da más velocidad y menos espera."]
                        : ["Es la banda más llena: la comparten las redes vecinas, el Bluetooth y hasta el microondas.", "La de 5 GHz da más velocidad y menos espera."],
                    ["Si tu router tiene 5 GHz, conéctate a esa red (a veces se llama igual con «5G» al final).", "Llega menos lejos, así que sirve si estás cerca del router."], null,
                    [$"Banda: {w.Band}", $"Redes en el mismo canal: {w.SameChannel}"]));
        }
        else if (n.Kind == "ethernet")
        {
            var gwBad = LostAny(gw) || gw?.Jitter > 2 || gw?.Avg > 3;
            var netBad = LossPct(inet) >= 5 || inet?.Jitter > 15;
            if (n.LinkMbps is > 0 and < 1000)
                res.Add(new("net-link", "warn", $"Tu cable de red va a {n.LinkMbps} Mbps",
                    ["Tu tarjeta y casi todos los routers van a 1000 Mbps o más.", "Cuando el enlace se queda en 100, casi siempre es un cable viejo o dañado (o un conector mal puesto) que solo usa la mitad de sus hilos."],
                    ["Cambia el cable por uno Cat 5e o Cat 6 y prueba otro puerto del router."], null, facts));
            if (gwBad)
                res.Add(new("net-lan", "warn", "Tu red de casa pierde paquetes",
                    [LostAny(gw) ? $"Aun por cable, entre tu PC y el router se perdieron {gw!.Lost} de {gw.Sent} respuestas." : $"Aun por cable, la espera hasta el router varía {Ms(gw!.Jitter)}.", "Por cable eso no debería pasar."],
                    ["Revisa que el cable esté bien encajado en los dos lados, prueba otro cable y otro puerto del router, y reinicia el router."], null, facts));
            else if (netBad)
                res.Add(new("net-isp", "info", "La conexión falla después del router",
                    [$"Hasta el router todo va bien, pero hacia internet se perdió el {Math.Round(LossPct(inet))} % o la espera varía {Ms(inet!.Jitter)}.", "El problema está en el router o en tu proveedor, no en tu PC."],
                    ["Reinicia el router, revisa si alguien en casa está descargando y, si sigue, llama a tu proveedor con estos datos."], null, facts));
            else if (n.LinkMbps is 0 or >= 1000)
                res.Add(new("net-ok", "ok", "Conexión por cable estable", ["En la prueba no se perdió nada y la espera casi no varió."], ["Nada que hacer."], null, facts));
        }
        return res;
    }

    static NetFacts ReadNet()
    {
        var all = NetworkInterface.GetAllNetworkInterfaces();
        static bool Virtual(NetworkInterface i) => Regex.IsMatch(i.Description + " " + i.Name,
            "Hyper-V|Virtual|VMware|VirtualBox|VPN|TAP-|WireGuard|Tailscale|ZeroTier|Loopback|Bluetooth|WAN Miniport|Npcap", RegexOptions.IgnoreCase);
        var real = all.Where(i => !Virtual(i) && i.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet).ToList();

        NetworkInterface? main = null;
        if (GetBestInterface(BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0), out var best) == 0)
            main = all.FirstOrDefault(i => { try { return i.GetIPProperties().GetIPv4Properties()?.Index == best; } catch { return false; } });
        main ??= real.FirstOrDefault(i => i.OperationalStatus == OperationalStatus.Up && i.GetIPProperties().GatewayAddresses.Any());
        if (main is null || main.OperationalStatus != OperationalStatus.Up) return new("none", "", 0, null, null, null, []);

        var kind = main.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "wifi" : Virtual(main) ? "other" : "ethernet";
        var gwIp = main.GetIPProperties().GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        var spare = kind == "wifi"
            ? real.Where(i => i.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 && i.OperationalStatus != OperationalStatus.Up).Select(i => i.Description).ToList()
            : new List<string>();

        var tGw = gwIp is null ? Task.FromResult<PingStats?>(null) : Task.Run<PingStats?>(() => PingMany(gwIp.ToString(), 20));
        var tNet = Task.Run<PingStats?>(() => { var p = PingMany("1.1.1.1", 20); return p.Lost == p.Sent ? PingMany("8.8.8.8", 20) : p; });
        WifiFacts? wifi = kind == "wifi" ? Wlan.Read(main.Id) : null;
        Task.WaitAll(tGw, tNet);
        var link = main.Speed > 0 ? main.Speed / 1_000_000 : 0;
        return new(kind, main.Description, link, wifi, tGw.Result, tNet.Result, spare);
    }

    static PingStats PingMany(string host, int n)
    {
        var rtts = new List<double?>();
        using var ping = new Ping();
        var buf = new byte[32];
        for (var i = 0; i < n; i++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var r = ping.Send(host, 1000, buf);
                var el = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                // RoundtripTime viene en ms enteros (0 en red local); por debajo de 1 ms vale más el reloj
                rtts.Add(r.Status != IPStatus.Success ? null : r.RoundtripTime > 0 ? r.RoundtripTime : Math.Min(el, 1));
            }
            catch { rtts.Add(null); }
            Thread.Sleep(50);
        }
        var ok = rtts.Where(x => x is not null).Select(x => x!.Value).ToList();
        double? jitter = null;
        var pairs = rtts.Zip(rtts.Skip(1)).Where(p => p.First is not null && p.Second is not null).Select(p => Math.Abs(p.First!.Value - p.Second!.Value)).ToList();
        if (pairs.Count > 0) jitter = pairs.Average();
        return new(host, n, n - ok.Count, ok.Count > 0 ? ok.Average() : null, ok.Count > 0 ? ok.Min() : null, ok.Count > 0 ? ok.Max() : null, jitter);
    }

    [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    /// <summary>Señal, banda y redes vecinas con la API nativa de Wi-Fi (no depende del idioma de Windows, a diferencia de netsh).</summary>
    static class Wlan
    {
        [DllImport("wlanapi.dll")] static extern uint WlanOpenHandle(uint ver, IntPtr res, out uint neg, out IntPtr h);
        [DllImport("wlanapi.dll")] static extern uint WlanCloseHandle(IntPtr h, IntPtr res);
        [DllImport("wlanapi.dll")] static extern uint WlanQueryInterface(IntPtr h, ref Guid g, int op, IntPtr res, out uint size, out IntPtr data, out int type);
        [DllImport("wlanapi.dll")] static extern uint WlanGetNetworkBssList(IntPtr h, ref Guid g, IntPtr ssid, int bssType, [MarshalAs(UnmanagedType.Bool)] bool sec, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        const int OpCurrentConnection = 7, OpRssi = 0x10000102;

        public static WifiFacts? Read(string ifaceId)
        {
            if (!Guid.TryParse(ifaceId, out var g)) return null;
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return null;
            try
            {
                if (WlanQueryInterface(h, ref g, OpCurrentConnection, IntPtr.Zero, out _, out var p, out _) != 0) return null;
                string ssid; int quality, phy; uint rx; byte[] bssid = new byte[6];
                try
                {
                    // WLAN_CONNECTION_ATTRIBUTES: estado(4) modo(4) perfil(512) | SSID: largo(4) bytes(32) | tipo(4) BSSID(6)+2 | phy(4) idx(4) calidad(4) rx(4) tx(4)
                    var len = Math.Min(32, Marshal.ReadInt32(p, 520));
                    var sb = new byte[len]; Marshal.Copy(p + 524, sb, 0, len);
                    ssid = System.Text.Encoding.UTF8.GetString(sb);
                    Marshal.Copy(p + 560, bssid, 0, 6);
                    phy = Marshal.ReadInt32(p, 568);
                    quality = Marshal.ReadInt32(p, 576);
                    rx = (uint)Marshal.ReadInt32(p, 580);
                }
                finally { WlanFreeMemory(p); }

                int? rssi = null;
                if (WlanQueryInterface(h, ref g, OpRssi, IntPtr.Zero, out _, out var pr, out _) == 0) { rssi = Marshal.ReadInt32(pr); WlanFreeMemory(pr); }

                double? freq = null; var same = 0;
                if (WlanGetNetworkBssList(h, ref g, IntPtr.Zero, 3, false, IntPtr.Zero, out var pl) == 0)
                {
                    try
                    {
                        // WLAN_BSS_LIST: tamaño(4) n(4), luego WLAN_BSS_ENTRY de 360 bytes; BSSID en +40, frecuencia (kHz) en +92
                        var count = Marshal.ReadInt32(pl, 4);
                        var entries = new List<(byte[] b, int f)>();
                        for (var i = 0; i < count; i++)
                        {
                            var e = pl + 8 + i * 360;
                            var b = new byte[6]; Marshal.Copy(e + 40, b, 0, 6);
                            entries.Add((b, Marshal.ReadInt32(e, 92)));
                        }
                        var mine = entries.FirstOrDefault(x => x.b.SequenceEqual(bssid));
                        if (mine.b is not null && mine.f > 0)
                        {
                            freq = mine.f / 1_000_000.0;
                            same = entries.Count(x => x.f == mine.f && !x.b.SequenceEqual(bssid));
                        }
                    }
                    finally { WlanFreeMemory(pl); }
                }
                var band = freq is null ? "" : freq < 3 ? "2,4 GHz" : freq < 5.925 ? "5 GHz" : "6 GHz";
                var phyName = phy switch { 7 => "Wi-Fi 4", 8 => "Wi-Fi 5", 10 => "Wi-Fi 6", 11 => "Wi-Fi 7", _ => "" };
                return new(ssid, quality, rssi, freq, band, phyName, rx / 1000.0, same);
            }
            catch { return null; }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }
    }

    // ======================= pantalla =======================

    static IEnumerable<Suggestion> Screen()
    {
        var cur = Display.Status().Current;
        if (cur is null || cur.Hz <= 1) yield break;   // fuera del escritorio real no hay pantalla de verdad
        var max = Display.MaxHz(cur.Width, cur.Height);
        if (max > cur.Hz + 1)
            yield return new("screen-hz", "warn", $"Tu pantalla va a {cur.Hz} Hz y puede ir a {max} Hz",
                [$"Windows la tiene en {cur.Hz} Hz, así que aunque el juego dé más FPS solo ves {cur.Hz} imágenes por segundo.", $"A {max} Hz todo se ve más fluido y responde antes."],
                [$"Configuración de Windows › Sistema › Pantalla › Pantalla avanzada › Elegir frecuencia de actualización: {max} Hz.", "Si no aparece, usa cable DisplayPort o HDMI 2.1."],
                null, [$"Resolución: {cur.Width} × {cur.Height}", $"Frecuencia ahora: {cur.Hz} Hz", $"Frecuencia máxima: {max} Hz"]);
    }

    // ======================= RAM =======================

    static IEnumerable<Suggestion> RamSpeed()
    {
        var mods = new List<(string part, int conf)>();
        using (var s = new ManagementObjectSearcher("SELECT PartNumber, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
            foreach (var o in s.Get())
                mods.Add((((string?)o["PartNumber"])?.Trim() ?? "", Convert.ToInt32(o["ConfiguredClockSpeed"] ?? 0)));
        if (mods.Count == 0) yield break;
        // el nombre del módulo suele llevar la velocidad para la que está hecho: UD5-6000, F5-6000J3038…, CMK32GX5M2B5600C36
        var rated = mods.Select(m => Regex.Matches(m.part, @"(?<!\d)(2[4-9]|3\d|4[0-8]|5\d|6\d|7\d|8[0-4])00(?!\d)").Select(x => int.Parse(x.Value)).DefaultIfEmpty(0).Max()).Min();
        var conf = mods.Min(m => m.conf);
        if (rated > 0 && conf > 0 && rated >= conf + 200)
            yield return new("ram-expo", "warn", $"Tu RAM va a {conf} MT/s y está hecha para {rated}",
                [$"El nombre de tus módulos ({mods[0].part}) indica {rated} MT/s, pero corren a {conf}.", "Viene así de fábrica: la velocidad completa se activa en la BIOS.", "Más velocidad de RAM ayuda sobre todo cuando el juego depende de la CPU."],
                ["Entra en la BIOS al encender y activa EXPO (AMD) o XMP (Intel) con el perfil 1.", "Vigía no toca la BIOS.", "Si el PC no arranca después, la BIOS vuelve sola a lo anterior tras unos intentos o con el botón de borrar CMOS."],
                null, [$"Módulos: {mods.Count} × {mods[0].part}", $"Velocidad ahora: {conf} MT/s", $"Velocidad del módulo: {rated} MT/s"]);
    }

    // ======================= discos =======================

    static IEnumerable<Suggestion> Disks(Telemetry tel)
    {
        var res = new List<Suggestion>();
        foreach (var d in tel.Read().Drives)
        {
            if (d.TotalGb < 20) continue;
            var pct = d.FreeGb / d.TotalGb * 100;
            var isC = d.Letter.StartsWith("C", StringComparison.OrdinalIgnoreCase);
            if (pct < 10 || (isC && d.FreeGb < 20))
                res.Add(new($"disk-space-{d.Letter[0]}", isC ? "warn" : "info", $"Queda poco espacio en {d.Letter}",
                    [$"Quedan {d.FreeGb:0.#} GB libres de {d.TotalGb:0} GB ({pct:0} %).", isC ? "Windows necesita espacio libre en C: para actualizaciones, la memoria virtual y los archivos temporales; sin él todo va más lento." : "Los SSD casi llenos escriben más despacio."],
                    ["Abre Almacenamiento y limpia lo que sobra, o mueve juegos a otro disco."], "storage",
                    [$"Libre: {d.FreeGb:0.#} GB", $"Total: {d.TotalGb:0} GB"]));
        }
        List<DiskHealth> health = [];
        try { health = Health.Scan(tel); } catch { }
        foreach (var h in health)
        {
            var where = h.Letters.Count > 0 ? string.Join(" ", h.Letters) : h.Model;
            if (h.Level is "bad" or "warn")
                res.Add(new($"disk-health-{h.Number}", h.Level, $"Revisa el disco {where}",
                    [h.Verdict, .. h.Issues.Take(1)],
                    ["Copia lo importante de ese disco a otro sitio.", "Los detalles están en Salud de discos."], "health", [$"Disco: {h.Model}"]));
            var crc = h.Attributes.FirstOrDefault(a => a.Id == 199);
            if (crc is { Raw: > 0 } && h.Level is not "bad")
                res.Add(new($"disk-crc-{h.Number}", "info", $"El cable SATA del disco {where} ha dado errores",
                    [$"El disco registra {crc.Raw} errores de transmisión (CRC).", "No son daños del disco: casi siempre es el cable SATA o que está flojo.", "Si el número no sube, puede ser de hace tiempo."],
                    ["Vuelve a mirar Salud de discos dentro de unos días.", "Si el número sube, cambia el cable SATA o conéctalo a otro puerto de la placa."], "health",
                    [$"Disco: {h.Model}", $"Errores CRC: {crc.Raw}"]));
        }
        return res;
    }

    // ======================= energía y uso =======================

    static IEnumerable<Suggestion> Power()
    {
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
        var active = (k?.GetValue("ActivePowerScheme") as string)?.ToLowerInvariant();
        if (active == "a1841308-3541-4fab-bc81-f71556f20b4a")
            yield return new("power-saver", "warn", "El PC está en modo Ahorro de energía",
                ["En ese modo Windows baja la velocidad de la CPU para gastar menos.", "En un PC de escritorio solo te quita rendimiento."],
                ["Panel de control › Opciones de energía › Equilibrado o Alto rendimiento."], null, ["Plan de energía: Ahorro de energía"]);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus { public byte AcLine, BatteryFlag, BatteryPercent, SaverOn; public int LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus s);

    /// <summary>¿Tiene batería (laptop)? Sirve para no poner cosas de PC de escritorio.</summary>
    public static bool HasBattery() => Environment.GetEnvironmentVariable("VIGIA_FAKE_BATTERY") == "1"
        || (GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255);

    /// <summary>Laptop funcionando con batería: Windows y la gráfica recortan potencia.</summary>
    static IEnumerable<Suggestion> Battery()
    {
        var fake = Environment.GetEnvironmentVariable("VIGIA_FAKE_BATTERY") == "1";
        var s = default(PowerStatus);
        if (!fake && !GetSystemPowerStatus(out s)) yield break;
        if (!fake && (s.BatteryFlag == 128 || s.BatteryFlag == 255 || s.AcLine != 0)) yield break;   // sin batería, desconocido o enchufada
        var pct = fake ? 64 : s.BatteryPercent;
        yield return new("battery", "warn", "Tu laptop está sin cargador",
            ["Con batería, Windows y la gráfica bajan la potencia para que dure más.", "Por eso rinde menos: menos FPS y más tirones."],
            ["Conecta el cargador para jugar."], null, pct <= 100 ? [$"Batería: {pct} %"] : []);
    }

    static IEnumerable<Suggestion> Usage(Snapshot s)
    {
        if (s.Ram.TotalGb > 0 && s.Ram.UsedGb / s.Ram.TotalGb > 0.85)
            yield return new("ram-full", "info", "La memoria está casi llena",
                [$"Están en uso {s.Ram.UsedGb:0.#} de {s.Ram.TotalGb:0} GB.", "Cuando se llena, Windows pasa cosas al disco y los juegos dan tirones."],
                ["Cierra lo que no uses (navegador con muchas pestañas, launchers) o libera memoria desde Memoria."], "mem",
                [$"RAM en uso: {s.Ram.UsedGb:0.#} / {s.Ram.TotalGb:0} GB"]);
        var days = TimeSpan.FromMilliseconds(Environment.TickCount64).TotalDays;
        if (days >= 7)
            yield return new("uptime", "info", $"El PC lleva {Math.Floor(days)} días sin reiniciar",
                ["Apagar y encender no reinicia Windows del todo (inicio rápido).", "Con el tiempo se acumulan procesos colgados y actualizaciones a medio instalar."],
                ["Reinicia desde Inicio › Reiniciar cuando termines de jugar."], null, [$"Encendido: {Math.Floor(days)} días"]);
    }

    // ======================= input lag y tirones =======================

    /// <summary>
    /// Pausas del sistema: un hilo con prioridad alta mira el reloj sin parar durante 1,5 s. Si de una lectura a la
    /// siguiente pasa más de 1 ms, el procesador se fue a atender un driver (DPC o interrupción): eso es lo que se
    /// siente como input lag y tirones. Solo corre al revisar Sugerencias.
    /// </summary>
    public static (int Spikes, double MaxMs) Pauses(int ms = 1500)
    {
        int spikes = 0; double max = 0;
        var t = new Thread(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            long last = sw.ElapsedTicks, end = (long)(ms / f);
            while (last < end)
            {
                long now = sw.ElapsedTicks; double gap = (now - last) * f;
                if (gap > max) max = gap;
                if (gap >= 1.0) spikes++;
                last = now;
            }
        }) { IsBackground = true, Priority = ThreadPriority.Highest };
        t.Start(); t.Join();
        return (spikes, Math.Round(max, 1));
    }

    static IEnumerable<Suggestion> InputLag()
    {
        var res = new List<Suggestion>();
        var fake = Environment.GetEnvironmentVariable("VIGIA_FAKE_LAG") == "1";
        var (spikes, max) = fake ? (14, 4.2) : Pauses();
        var facts = new List<string> { $"Pausas de más de 1 ms: {spikes} en 1,5 s", $"Pausa más larga: {max:0.0} ms" };
        if (spikes >= 3 || max >= 3)
            res.Add(new("lag-dpc", "warn", "Tu PC se traba a ratos: input lag y tirones",
                [$"En 1,5 s el procesador se paró {spikes} veces para atender un driver, la más larga de {max:0.0} ms.", "En el juego se nota como tirones y como que el ratón responde tarde."],
                ["Actualiza los drivers de la gráfica, del chipset de la placa, del audio y de la red.", "Desconecta lo USB que no uses (luces RGB, hubs, mandos) y vuelve a revisar."],
                null, facts));
        else
            res.Add(new("lag-ok", "ok", "Sin input lag del sistema", ["En la prueba el procesador no se trabó con ningún driver."], ["Nada que hacer."], null, facts));

        using (var m = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse"))
            if ((m?.GetValue("MouseSpeed") as string) is { } sp && sp != "0")
                res.Add(new("lag-accel", "info", "El ratón tiene aceleración",
                    ["Con aceleración, el puntero se mueve distinto según lo rápido que muevas la mano.", "Apuntar se siente raro y cuesta repetir el mismo movimiento."],
                    ["Ajustes › Ratón sin aceleración."], "tweaks", ["«Mejorar la precisión del puntero»: encendido"]));

        var bcd = Bcd();
        if (bcd.Contains("useplatformclock") && Regex.IsMatch(bcd, @"useplatformclock\s+(yes|on|true|sí|si|sim|oui|ja)", RegexOptions.IgnoreCase))
            res.Add(new("lag-hpet", "warn", "Windows usa el reloj HPET a la fuerza",
                ["Alguien activó «useplatformclock». Ese reloj es más lento de leer y causa tirones en juegos."],
                ["Abre «Símbolo del sistema» como administrador y escribe: bcdedit /deletevalue useplatformclock", "Reinicia cuando termines de jugar."],
                null, ["useplatformclock: sí"]));
        return res;
    }

    static string Bcd()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("bcdedit.exe", "/enum {current}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); return o;
        }
        catch { return ""; }
    }

    // ======================= prueba =======================

    /// <summary>Solo para capturas y pruebas (VIGIA_FAKE_NET=wifi): un Wi-Fi inestable inventado.</summary>
    static NetFacts FakeWifi() => new("wifi", "Wi-Fi", 0, new("casa", 54, -72, 2.437, "2,4 GHz", "Wi-Fi 6", 144, 5),
        new("192.168.0.1", 20, 2, 9.4, 2, 87, 14.2), new("1.1.1.1", 20, 2, 41, 31, 132, 19.5), ["Realtek Gaming 2.5GbE Family Controller"]);

    /// <summary>Comprueba las reglas de red con mediciones inventadas.</summary>
    public static string SelfTest()
    {
        var lines = new List<string>(); var ok = true;
        void Check(string name, NetFacts f, string? expectId)
        {
            var got = Network(f).Select(x => x.Id).ToList();
            var pass = expectId is null ? got.Count == 0 : got.Contains(expectId);
            ok &= pass; lines.Add($"{(pass ? "OK  " : "FAIL")} {name}: {string.Join(",", got)}");
        }
        PingStats Pg(int lost, double avg, double max, double jit) => new("x", 20, lost, avg, Math.Min(avg, 1), max, jit);
        var goodW = new WifiFacts("casa", 90, -48, 5.5, "5 GHz", "Wi-Fi 6", 866, 0);
        Check("wifi inestable", new("wifi", "w", 0, goodW, Pg(2, 6, 80, 12), Pg(2, 30, 120, 18), ["Realtek 2.5GbE"]), "net-cable");
        Check("wifi señal débil", new("wifi", "w", 0, goodW with { Quality = 40, Rssi = -78 }, Pg(0, 3, 6, 1), Pg(0, 20, 25, 2), []), "net-cable");
        Check("wifi estable", new("wifi", "w", 0, goodW, Pg(0, 3, 6, 1), Pg(0, 20, 25, 2), []), "net-ok");
        Check("wifi bien, internet mal", new("wifi", "w", 0, goodW, Pg(0, 3, 6, 1), Pg(3, 40, 300, 30), []), "net-isp");
        Check("wifi 2,4 GHz", new("wifi", "w", 0, goodW with { Band = "2,4 GHz", SameChannel = 6 }, Pg(0, 3, 6, 1), Pg(0, 20, 25, 2), []), "net-5ghz");
        Check("cable a 100 Mbps", new("ethernet", "e", 100, null, Pg(0, 0.4, 1, 0.1), Pg(0, 20, 25, 2), []), "net-link");
        Check("cable estable", new("ethernet", "e", 1000, null, Pg(0, 0.4, 1, 0.1), Pg(0, 20, 25, 2), []), "net-ok");
        Check("cable con pérdidas", new("ethernet", "e", 1000, null, Pg(3, 0.4, 1, 0.1), Pg(3, 20, 25, 2), []), "net-lan");
        Check("sin red", new("none", "", 0, null, null, null, []), null);
        var cable = Network(new("wifi", "w", 0, goodW, Pg(2, 6, 80, 12), Pg(2, 30, 120, 18), ["Realtek 2.5GbE"]))[0];
        var why = cable.Why.Any(x => x.Contains("2 de 20 respuestas")) && cable.Do.Any(x => x.Contains("Realtek 2.5GbE"));
        ok &= why; lines.Add($"{(why ? "OK  " : "FAIL")} el motivo dice lo medido y el puerto libre");
        lines.Add(ok ? "PASS" : "FAIL");
        return string.Join(Environment.NewLine, lines);
    }
}
