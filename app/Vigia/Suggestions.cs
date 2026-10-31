using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Vigia;

/// <summary>Una sugerencia: qué pasa (con lo medido), por qué importa y qué hacer. Go = sección de Vigía que ayuda.</summary>
/// Why y Do van frase a frase: la interfaz traduce cada frase por separado.
/// <summary>Fix: lo que Vigía puede arreglar con un botón ("tweak:id", "hz:pantalla:hz", "trim", "sched"). Null si no se puede desde Windows.</summary>
public sealed record Suggestion(string Id, string Level, string Title, IReadOnlyList<string> Why, IReadOnlyList<string> Do, string? Go, IReadOnlyList<string> Facts, string? Fix = null, string? FixLabel = null);
public sealed record PingStats(string Target, int Sent, int Lost, double? Avg, double? Min, double? Max, double? Jitter);
/// <summary>Lo que se sabe del Wi-Fi conectado sin pedir ubicación: sin nombre de la red, sin BSSID y sin redes vecinas.</summary>
public sealed record WifiFacts(int Quality, int? Rssi, double? FreqGhz, string Band, string Phy, double RxMbps);
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

    /// <summary>Lo último que se midió del ratón y del mando al pulsar «Analizar» (ver Drift.Sense).</summary>
    public static volatile IReadOnlyList<Suggestion> Input = [];

    /// <summary>Cambia lo del ratón y del mando por lo recién medido y vuelve a ordenar.</summary>
    public static SuggestReport WithInput(SuggestReport r, IReadOnlyList<Suggestion> input)
    {
        Input = input;
        var order = new Dictionary<string, int> { ["bad"] = 0, ["warn"] = 1, ["info"] = 2, ["ok"] = 3 };
        var items = r.Items.Where(i => !i.Id.StartsWith("in-")).Concat(input).OrderBy(i => order.GetValueOrDefault(i.Level, 9)).ToList();
        return r with { Items = items };
    }

    public static SuggestReport Run(Telemetry tel)
    {
        var items = new List<Suggestion>();
        NetFacts? net = null;
        try { net = Environment.GetEnvironmentVariable("VIGIA_FAKE_NET") == "wifi" ? FakeWifi() : ReadNet(); items.AddRange(Network(net)); } catch { }
        try { items.AddRange(RamSpeed()); } catch { }
        try { items.AddRange(Disks(tel)); } catch { }
        try { items.AddRange(DiskUpkeep()); } catch { }
        try { items.AddRange(Power()); } catch { }
        try { items.AddRange(ForGames(items)); } catch { }
        try { items.AddRange(Battery()); } catch { }
        try { items.AddRange(Usage(tel.Read())); } catch { }
        try { items.AddRange(InputLag()); } catch { }
        items.AddRange(Input);
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
            if (wf.RxMbps > 0) facts.Add($"Velocidad entre tu PC y el router: {Math.Round(wf.RxMbps)} Mbps");
        }
        else if (n.LinkMbps > 0) facts.Add($"Velocidad entre tu PC y el router: {(n.LinkMbps >= 1000 ? $"{n.LinkMbps / 1000.0:0.#} Gbps" : $"{n.LinkMbps} Mbps")}");
        if (gw is { Sent: > 0 }) facts.Add($"Router: contestó {gw.Sent - gw.Lost} de {gw.Sent} veces, tarda {Ms(gw.Avg)} (lo más lento: {Ms(gw.Max)})");
        if (inet is { Sent: > 0 }) facts.Add($"Internet: contestó {inet.Sent - inet.Lost} de {inet.Sent} veces, tarda {Ms(inet.Avg)} (lo más lento: {Ms(inet.Max)})");

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
                    ["Es la banda más llena: la comparten las redes vecinas, el Bluetooth y hasta el microondas.", "La de 5 GHz da más velocidad y menos espera."],
                    ["Si tu router tiene 5 GHz, conéctate a esa red (a veces se llama igual con «5G» al final).", "Llega menos lejos, así que sirve si estás cerca del router."], null,
                    [$"Banda: {w.Band}"]));
        }
        else if (n.Kind == "ethernet")
        {
            var gwBad = LostAny(gw) || gw?.Jitter > 2 || gw?.Avg > 3;
            var netBad = LossPct(inet) >= 5 || inet?.Jitter > 15;
            if (n.LinkMbps is > 0 and < 1000)
                res.Add(new("net-link", "warn", "Tu cable de red va más lento de lo que puede",
                    [$"Tu PC y el router se están hablando a {n.LinkMbps} Mbps, cuando pueden ir a 1000 (diez veces más).", "Casi siempre es el cable: viejo, doblado o mal enchufado.", "Si tu internet contratado es de menos de 100 Mbps, al navegar no lo notas; al descargar juegos o pasar archivos, sí."],
                    ["Desenchufa el cable de los dos lados y vuelve a meterlo hasta que haga clic.", "Si sigue igual, cámbialo por uno que diga «Cat 6» (o «Cat 5e») impreso en el cable.", "Prueba también otro puerto del router."], null, facts));
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
        var link = main.Speed > 0 ? main.Speed / 1_000_000 : 0;
        WifiFacts? wifi = null;
        if (kind == "wifi")
        {
            if (Wlan.SignalOn) wifi = Wlan.Read(main.Id, link);
            else LocationGuard.Trace("wifi: lectura de señal apagada en esta versión (sin consultas WLAN)");
        }
        Task.WaitAll(tGw, tNet);
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

    /// <summary>
    /// Señal y banda del Wi-Fi conectado con la API nativa (no depende del idioma de Windows, a diferencia de netsh),
    /// SIN pedir ubicación. Desde Windows 11 24H2, WlanQueryInterface con current_connection, WlanGetNetworkBssList,
    /// WlanGetAvailableNetworkList y WlanScan dan el BSSID (sirve para ubicar el equipo), así que Windows exige permiso de
    /// ubicación y, si está apagada, avisa «Location has been turned off» señalando a la app. Aquí solo se usan
    /// consultas sin BSSID: calidad en tiempo real, RSSI y canal. Por eso ya no se sabe el nombre de la red ni cuántas
    /// redes vecinas comparten canal: no hacen falta para recomendar cable o 5 GHz.
    /// </summary>
    internal static class Wlan
    {
        [DllImport("wlanapi.dll")] static extern uint WlanOpenHandle(uint ver, IntPtr res, out uint neg, out IntPtr h);
        [DllImport("wlanapi.dll")] static extern uint WlanCloseHandle(IntPtr h, IntPtr res);
        [DllImport("wlanapi.dll")] static extern uint WlanQueryInterface(IntPtr h, ref Guid g, int op, IntPtr res, out uint size, out IntPtr data, out int type);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        // wlan_intf_opcode_channel_number, _realtime_connection_quality (Windows 11 24H2+) y _rssi
        public const int OpChannel = 8, OpRealtime = 19, OpRssi = 0x10000102;
        /// <summary>
        /// La lectura de señal no se ha comprobado aún con un Wi-Fi real conectado y la ubicación apagada, así que viene
        /// APAGADA: sin esto no se abre ni una consulta WLAN y las Sugerencias de red se basan solo en las pruebas de
        /// espera (router e internet). VIGIA_WIFI_SIGNAL=1 la enciende para esa validación (LOCATION_PRIVACY_FIX.md).
        /// </summary>
        public static bool SignalOn => Environment.GetEnvironmentVariable("VIGIA_WIFI_SIGNAL") == "1";

        /// <summary>Las únicas consultas permitidas. Ninguna está en la lista de Microsoft que exige ubicación.</summary>
        public static readonly int[] Allowed = [OpRealtime, OpRssi, OpChannel];

        public static WifiFacts? Read(string ifaceId, double linkMbps)
        {
            if (!Guid.TryParse(ifaceId, out var g)) return null;
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) { LocationGuard.Trace("wifi: el servicio WLAN no contesta"); return null; }
            try { return FromQueries(op => Query(h, g, op), linkMbps); }
            catch { return null; }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        static (uint rc, byte[]? data) Query(IntPtr h, Guid g, int op)
        {
            // barrera: una consulta nueva tiene que pasar por la lista (y por la prueba) antes de usarse
            if (Array.IndexOf(Allowed, op) < 0) throw new InvalidOperationException("consulta Wi-Fi no permitida: " + op);
            var rc = WlanQueryInterface(h, ref g, op, IntPtr.Zero, out var size, out var p, out _);
            if (rc != 0 || p == IntPtr.Zero) return (rc, null);
            try { var b = new byte[Math.Min((int)size, 4096)]; Marshal.Copy(p, b, 0, b.Length); return (0, b); }
            finally { WlanFreeMemory(p); }
        }

        /// <summary>Arma los datos con las respuestas de <paramref name="q"/>. Separado de la API para probarlo sin Wi-Fi.</summary>
        public static WifiFacts? FromQueries(Func<int, (uint rc, byte[]? data)> q, double linkMbps)
        {
            int? quality = null, rssi = null; double? freq = null; var phy = "";
            // WLAN_REALTIME_CONNECTION_QUALITY: phy(4) calidad(4) rx(4) tx(4) mlo(4) enlaces(4) | enlace: id(1)+3 frecuencia MHz(4) ancho(4) rssi(4) …
            var (rcRt, rt) = q(OpRealtime);
            if (rt is { Length: >= 40 })
            {
                phy = PhyName(BitConverter.ToInt32(rt, 0));
                if (BitConverter.ToInt32(rt, 4) is var lq and >= 0 and <= 100) quality = lq;
                if (BitConverter.ToInt32(rt, 20) >= 1)
                {
                    if (BitConverter.ToInt32(rt, 28) is var mhz and >= 2400 and <= 7200) freq = mhz / 1000.0;
                    if (BitConverter.ToInt32(rt, 36) is var r and < 0 and > -120) rssi = r;
                }
            }
            var rcRssi = "-"; var rcCh = "-";
            if (rssi is null)
            {
                var (rc, d) = q(OpRssi); rcRssi = rc.ToString();
                if (d is { Length: >= 4 } && BitConverter.ToInt32(d, 0) is var r and < 0 and > -120) rssi = r;
            }
            if (freq is null)
            {
                var (rc, d) = q(OpChannel); rcCh = rc.ToString();
                if (d is { Length: >= 4 }) freq = ChannelGhz(BitConverter.ToInt32(d, 0));
            }
            // misma escala que Windows: 0 % = -100 dBm, 100 % = -50 dBm
            quality ??= rssi is { } x ? Math.Clamp(2 * (x + 100), 0, 100) : null;
            LocationGuard.Trace($"wifi: lectura sin ubicación (tiempo real rc={rcRt}, rssi rc={rcRssi}, canal rc={rcCh}, datos={(quality is null ? "no" : "sí")})");
            if (quality is null) return null;
            var band = freq is null ? "" : freq < 3 ? "2,4 GHz" : freq < 5.925 ? "5 GHz" : "6 GHz";
            return new(quality.Value, rssi, freq, band, phy, linkMbps);
        }

        /// <summary>Canal → GHz. Solo cuando falta la calidad en tiempo real (Windows anterior a 24H2): ahí 6 GHz es raro
        /// y sus canales 1–233 se confundirían con 2,4 GHz, así que se tratan como 2,4/5 GHz.</summary>
        public static double? ChannelGhz(int ch) => ch switch
        {
            >= 1 and <= 13 => 2.407 + 0.005 * ch,
            14 => 2.484,
            >= 32 and <= 177 => 5.0 + 0.005 * ch,
            _ => null
        };

        static string PhyName(int phy) => phy switch { 7 => "Wi-Fi 4", 8 => "Wi-Fi 5", 10 => "Wi-Fi 6", 11 => "Wi-Fi 7", _ => "" };
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

    // ======================= mantenimiento de discos =======================

    const string FsKey = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem";
    const string DefragTask = @"\Microsoft\Windows\Defrag\ScheduledDefrag";

    /// <summary>Windows ya hace TRIM y desfragmenta solo cada semana; aquí solo se avisa si alguien lo apagó.</summary>
    static IEnumerable<Suggestion> DiskUpkeep()
    {
        using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem"))
            if (k?.GetValue("DisableDeleteNotification") is int v && v == 1)
                yield return new("disk-trim", "warn", "TRIM está apagado",
                    ["Sin TRIM, los SSD se van poniendo lentos al escribir."], [], null, ["DisableDeleteNotification: 1"], "trim", "Encender TRIM");
        if (!Optimize.ScheduledEnabled())
            yield return new("disk-sched", "warn", "Windows no está cuidando tus discos",
                ["Alguien apagó la tarea semanal que hace TRIM en los SSD y desfragmenta los discos mecánicos."], [], null, ["Tarea ScheduledDefrag: apagada"], "sched", "Encenderla");
    }

    /// <summary>Lo de «Tu PC para jugar» que antes estaba en Juegos: solo lo que falta, con su botón.</summary>
    static IEnumerable<Suggestion> ForGames(List<Suggestion> have)
    {
        var res = new List<Suggestion>();
        foreach (var c in Rig.Checks().Where(c => c.Level == "warn"))
        {
            if (c.Key == "ram" && have.Any(h => h.Id == "ram-expo")) continue;
            if (c.Key == "tweak:power-ultimate" && (have.Any(h => h.Id == "power-saver") || HasBattery())) continue;
            res.Add(new(c.Key.Replace(':', '-'), "warn", c.Title, [c.Detail], c.How is null ? [] : [c.How], null, [], c.Fix, c.FixLabel));
        }
        return res;
    }

    /// <summary>Lo que se arregla con el botón de una sugerencia. Guarda copia antes de tocar.</summary>
    public static (bool Ok, string Message) Fix(string fix)
    {
        switch (fix)
        {
            case "trim":
                if (Backups.Save("ajustes", "Encender TRIM", [Backups.Item.Reg(FsKey, "DisableDeleteNotification", "TRIM")]) is null)
                    return (false, "No pude guardar la copia de seguridad; no cambié nada.");
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem"))
                    k.SetValue("DisableDeleteNotification", 0, Microsoft.Win32.RegistryValueKind.DWord);
                return (true, "TRIM encendido.");
            case "sched":
                if (Backups.Save("ajustes", "Encender el cuidado semanal de los discos", [Backups.Item.Task(DefragTask, false, "Optimización semanal de discos")]) is null)
                    return (false, "No pude guardar la copia de seguridad; no cambié nada.");
                Startup.SetTask(DefragTask, true);
                return Optimize.ScheduledEnabled() ? (true, "Listo: Windows vuelve a cuidar tus discos cada semana.") : (false, "Windows no dejó encender la tarea.");
        }
        return (false, "No sé arreglar eso.");
    }

    // ======================= energía y uso =======================

    static IEnumerable<Suggestion> Power()
    {
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
        var active = (k?.GetValue("ActivePowerScheme") as string)?.ToLowerInvariant();
        if (active == "a1841308-3541-4fab-bc81-f71556f20b4a")
            yield return new("power-saver", "warn", "El PC está en modo Ahorro de energía",
                ["En ese modo Windows baja la velocidad de la CPU para gastar menos.", "En un PC de escritorio solo te quita rendimiento."],
                [], null, ["Plan de energía: Ahorro de energía"], HasBattery() ? null : "tweak:power-ultimate", HasBattery() ? null : "Aplicar plan de energía de Vigía");
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
                    [], null, ["«Mejorar la precisión del puntero»: encendido"], "tweak:mouse-raw", "Quitar la aceleración"));

        var bcd = Bcd();
        // Programas que leen el mando y lo vuelven a enviar como otro: cada paso extra suma retraso
        string[] remap = ["ds4windows", "dsx", "rewasd", "rewasdengine", "antimicrox", "antimicro", "inputmapper", "x360ce", "joytokey", "steamrelay"];
        var open = System.Diagnostics.Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } })
            .Where(n => remap.Contains(n.ToLowerInvariant())).Distinct().ToList();
        if (Environment.GetEnvironmentVariable("VIGIA_FAKE_REMAP") == "1") open = ["Remap"];
        if (open.Count > 0)
            res.Add(new("lag-remap", "info", "Un programa traduce tu mando",
                ["El mando pasa primero por ese programa y luego llega al juego: es un paso más y suma retraso."],
                ["Si el juego reconoce tu mando sin él, ciérralo antes de jugar."],
                null, open.Select(n => n + ".exe").ToList()));
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
    static NetFacts FakeWifi() => new("wifi", "Wi-Fi", 0, new(54, -72, 2.437, "2,4 GHz", "Wi-Fi 6", 144),
        new("192.168.0.1", 20, 2, 9.4, 2, 87, 14.2), new("1.1.1.1", 20, 2, 41, 31, 132, 19.5), ["Realtek Gaming 2.5GbE Family Controller"]);

    /// <summary>Regresión del aviso «Location has been turned off»: la lectura de Wi-Fi solo hace consultas sin BSSID,
    /// lee bien las respuestas y la traza no lleva nada que ubique el equipo. Con respuestas inventadas, sin Wi-Fi real.</summary>
    static List<string> WlanSelfTest()
    {
        var res = new List<string>();
        void Ok(bool c, string what) => res.Add($"{(c ? "OK  " : "FAIL")} ubicación: {what}");
        // las consultas que Microsoft dice que exigen ubicación nunca están permitidas
        Ok(!Wlan.Allowed.Contains(7), "current_connection (BSSID) no está entre las consultas Wi-Fi");
        Ok(Wlan.Allowed.All(o => o is Wlan.OpRealtime or Wlan.OpRssi or Wlan.OpChannel), "solo tiempo real, RSSI y canal");

        byte[] Rt(int phy, int q, int mhz, int rssi)
        {
            var b = new byte[64];
            BitConverter.GetBytes(phy).CopyTo(b, 0); BitConverter.GetBytes(q).CopyTo(b, 4); BitConverter.GetBytes(1).CopyTo(b, 20);
            BitConverter.GetBytes(mhz).CopyTo(b, 28); BitConverter.GetBytes(rssi).CopyTo(b, 36); return b;
        }
        var asked = new List<int>();
        var traceBefore = File.Exists(LocationGuard.LogPath) ? File.ReadAllText(LocationGuard.LogPath).Length : 0;
        var f = Wlan.FromQueries(op => { asked.Add(op); return op == Wlan.OpRealtime ? (0u, Rt(10, 70, 5180, -61)) : (0u, null); }, 1201);
        Ok(f is { Quality: 70, Rssi: -61, Band: "5 GHz", Phy: "Wi-Fi 6", RxMbps: 1201 }, "Windows 24H2+: calidad, RSSI y banda de la consulta en tiempo real");
        Ok(asked.SequenceEqual([Wlan.OpRealtime]), "con tiempo real no hace falta nada más");
        var log = File.Exists(LocationGuard.LogPath) ? File.ReadAllText(LocationGuard.LogPath)[traceBefore..] : "";
        Ok(log.Contains("wifi: lectura sin ubicación") && !log.Contains("5180") && !log.Contains("5,18") && !log.Contains("-61"),
            "la traza dice qué se pidió, sin frecuencia ni señal");

        asked.Clear();
        f = Wlan.FromQueries(op => { asked.Add(op); return op switch { Wlan.OpRssi => (0u, BitConverter.GetBytes(-75)), Wlan.OpChannel => (0u, BitConverter.GetBytes(6)), _ => (50u, (byte[]?)null) }; }, 144);
        Ok(f is { Quality: 50, Rssi: -75, Band: "2,4 GHz" }, "Windows anterior: RSSI y canal 6 → 50 %, 2,4 GHz");
        Ok(asked.SequenceEqual([Wlan.OpRealtime, Wlan.OpRssi, Wlan.OpChannel]), "y solo pregunta lo permitido");

        // si Windows niega algo (5 = acceso denegado) no se insiste ni se pide permiso: sin datos de Wi-Fi y ya
        asked.Clear();
        f = Wlan.FromQueries(op => { asked.Add(op); return (5u, null); }, 0);
        Ok(f is null && asked.Count == 3, "con acceso denegado devuelve sin datos y no reintenta");
        Ok(Wlan.ChannelGhz(36) is > 5.17 and < 5.19 && Wlan.ChannelGhz(14) is 2.484 && Wlan.ChannelGhz(0) is null, "canales → GHz");
        return res;
    }

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
        var goodW = new WifiFacts(90, -48, 5.5, "5 GHz", "Wi-Fi 6", 866);
        Check("wifi inestable", new("wifi", "w", 0, goodW, Pg(2, 6, 80, 12), Pg(2, 30, 120, 18), ["Realtek 2.5GbE"]), "net-cable");
        Check("wifi señal débil", new("wifi", "w", 0, goodW with { Quality = 40, Rssi = -78 }, Pg(0, 3, 6, 1), Pg(0, 20, 25, 2), []), "net-cable");
        Check("wifi estable", new("wifi", "w", 0, goodW, Pg(0, 3, 6, 1), Pg(0, 20, 25, 2), []), "net-ok");
        Check("wifi bien, internet mal", new("wifi", "w", 0, goodW, Pg(0, 3, 6, 1), Pg(3, 40, 300, 30), []), "net-isp");
        Check("wifi 2,4 GHz", new("wifi", "w", 0, goodW with { Band = "2,4 GHz" }, Pg(0, 3, 6, 1), Pg(0, 20, 25, 2), []), "net-5ghz");
        Check("cable a 100 Mbps", new("ethernet", "e", 100, null, Pg(0, 0.4, 1, 0.1), Pg(0, 20, 25, 2), []), "net-link");
        Check("cable estable", new("ethernet", "e", 1000, null, Pg(0, 0.4, 1, 0.1), Pg(0, 20, 25, 2), []), "net-ok");
        Check("cable con pérdidas", new("ethernet", "e", 1000, null, Pg(3, 0.4, 1, 0.1), Pg(3, 20, 25, 2), []), "net-lan");
        Check("sin red", new("none", "", 0, null, null, null, []), null);
        var cable = Network(new("wifi", "w", 0, goodW, Pg(2, 6, 80, 12), Pg(2, 30, 120, 18), ["Realtek 2.5GbE"]))[0];
        var why = cable.Why.Any(x => x.Contains("2 de 20 respuestas")) && cable.Do.Any(x => x.Contains("Realtek 2.5GbE"));
        ok &= why; lines.Add($"{(why ? "OK  " : "FAIL")} el motivo dice lo medido y el puerto libre");
        foreach (var l in WlanSelfTest()) { ok &= l.StartsWith("OK"); lines.Add(l); }
        lines.Add(ok ? "PASS" : "FAIL");
        return string.Join(Environment.NewLine, lines);
    }
}
