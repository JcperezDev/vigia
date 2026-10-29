using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Vigia;

/// <summary>Un dispositivo medido: ratón o stick de un mando. Level: ok | warn | bad | touched.</summary>
public sealed record DriftItem(string Kind, string Name, string Level, string Title, string? Detail, string? Fix);
public sealed record DriftReport(int Seconds, int Mice, int Pads, IReadOnlyList<DriftItem> Items);

/// <summary>
/// Drift: el ratón o el stick del mando se mueven solos sin tocarlos. Solo cuando el usuario pulsa «Probar»:
/// escucha el ratón en crudo (Raw Input, sin hooks ni inyección) y lee los mandos (XInput y, si no hay, winmm)
/// durante unos segundos; luego lo suelta todo. No escribe nada.
/// </summary>
public static class Drift
{
    // ---------- Raw Input (ratón) ----------
    const int WM_INPUT = 0x00FF, RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIM_TYPEMOUSE = 0;
    const int RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1;
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public int Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, int n, int size);
    [DllImport("user32.dll")] static extern int GetRawInputData(IntPtr h, int cmd, IntPtr data, ref int size, int header);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetRawInputDeviceInfo(IntPtr dev, int cmd, StringBuilder? data, ref int size);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time, x, y; }
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr h, int min, int max, int remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);

    sealed class Mouse { public int Moves, Clicks; public long Dist; }

    sealed class Sink : NativeWindow
    {
        public readonly Dictionary<IntPtr, Mouse> Mice = new();
        public Sink() => CreateHandle(new CreateParams { Parent = new IntPtr(-3) });   // ventana solo de mensajes: invisible
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) Read(m.LParam);
            base.WndProc(ref m);
        }
        void Read(IntPtr h)
        {
            int hdr = 8 + 2 * IntPtr.Size, size = 0;
            GetRawInputData(h, RID_INPUT, IntPtr.Zero, ref size, hdr);
            if (size <= 0 || size > 1024) return;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetRawInputData(h, RID_INPUT, buf, ref size, hdr) != size) return;
                if (Marshal.ReadInt32(buf) != RIM_TYPEMOUSE) return;
                var dev = Marshal.ReadIntPtr(buf, 8);
                var flags = (ushort)Marshal.ReadInt16(buf, hdr);
                var btn = (ushort)Marshal.ReadInt16(buf, hdr + 4);
                int dx = Marshal.ReadInt32(buf, hdr + 12), dy = Marshal.ReadInt32(buf, hdr + 16);
                if (!Mice.TryGetValue(dev, out var mo)) Mice[dev] = mo = new Mouse();
                if ((btn & 0x03FF) != 0) mo.Clicks++;   // clics y rueda: lo tocaron
                if ((flags & 1) == 0 && (dx != 0 || dy != 0)) { mo.Moves++; mo.Dist += Math.Abs(dx) + Math.Abs(dy); }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    // ---------- Mandos ----------
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_STATE { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll")] static extern int XInputGetState(int i, out XINPUT_STATE s);
    [StructLayout(LayoutKind.Sequential)] struct JOYINFOEX { public int Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Res1, Res2; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JOYCAPS
    {
        public ushort Mid, Pid; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public int Xmin, Xmax, Ymin, Ymax, Zmin, Zmax, NumButtons, PeriodMin, PeriodMax, Rmin, Rmax, Umin, Umax, Vmin, Vmax, Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxD;
    }
    [DllImport("winmm.dll")] static extern int joyGetPosEx(int id, ref JOYINFOEX j);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int joyGetDevCapsW(IntPtr id, ref JOYCAPS c, int size);
    [DllImport("winmm.dll")] static extern int joyGetNumDevs();

    /// <summary>Muestras de un stick normalizadas a -1..1, más si se pulsó algo.</summary>
    sealed class Stick { public string Pad = "", Side = ""; public readonly List<(double x, double y)> S = new(); public bool Pressed; }

    static double Norm(int v, int min, int max) => max > min ? (2.0 * (v - min) / (max - min)) - 1 : 0;

    public static DriftReport Run(int seconds)
    {
        seconds = Math.Clamp(seconds, 5, 30);
        DriftReport? rep = null; Exception? err = null;
        var t = new Thread(() => { try { rep = Measure(seconds); } catch (Exception e) { err = e; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw err;
        return rep!;
    }

    static DriftReport Measure(int seconds)
    {
        var sink = new Sink();
        var reg = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = sink.Handle } };
        var listening = RegisterRawInputDevices(reg, 1, Marshal.SizeOf<RAWINPUTDEVICE>());

        // mandos: XInput primero; si no hay, los de winmm (DualSense y otros por HID)
        var sticks = new List<Stick>();
        var xi = new List<int>();
        for (int i = 0; i < 4; i++) { try { if (XInputGetState(i, out _) == 0) xi.Add(i); } catch (DllNotFoundException) { break; } }
        var wm = new List<(int id, JOYCAPS caps)>();
        if (xi.Count == 0)
        {
            int n = Math.Min(joyGetNumDevs(), 16);
            for (int i = 0; i < n; i++)
            {
                var j = new JOYINFOEX { Size = Marshal.SizeOf<JOYINFOEX>(), Flags = 0xFF };
                if (joyGetPosEx(i, ref j) != 0) continue;
                var c = new JOYCAPS();
                if (joyGetDevCapsW(new IntPtr(i), ref c, Marshal.SizeOf<JOYCAPS>()) == 0) wm.Add((i, c));
            }
        }
        foreach (var i in xi) { sticks.Add(new Stick { Pad = $"Mando {i + 1}", Side = "izquierdo" }); sticks.Add(new Stick { Pad = $"Mando {i + 1}", Side = "derecho" }); }
        foreach (var (id, c) in wm)
        {
            var name = string.IsNullOrWhiteSpace(c.Name) ? $"Mando {id + 1}" : c.Name.Trim();
            sticks.Add(new Stick { Pad = name, Side = "izquierdo" });
            if (c.NumAxes >= 4) sticks.Add(new Stick { Pad = name, Side = "derecho" });
        }

        var end = DateTime.UtcNow.AddSeconds(seconds);
        var next = DateTime.UtcNow;
        while (DateTime.UtcNow < end)
        {
            while (PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessage(ref m); }
            if (DateTime.UtcNow >= next)
            {
                next = DateTime.UtcNow.AddMilliseconds(20);
                int k = 0;
                foreach (var i in xi)
                {
                    if (XInputGetState(i, out var s) != 0) { k += 2; continue; }
                    bool pressed = s.Buttons != 0 || s.LT > 40 || s.RT > 40;
                    var l = sticks[k++]; var r = sticks[k++];
                    l.S.Add((s.LX / 32768.0, s.LY / 32768.0)); r.S.Add((s.RX / 32768.0, s.RY / 32768.0));
                    l.Pressed |= pressed; r.Pressed |= pressed;
                }
                foreach (var (id, c) in wm)
                {
                    var j = new JOYINFOEX { Size = Marshal.SizeOf<JOYINFOEX>(), Flags = 0xFF };
                    if (joyGetPosEx(id, ref j) != 0) { k += c.NumAxes >= 4 ? 2 : 1; continue; }
                    var l = sticks[k++];
                    l.S.Add((Norm(j.X, c.Xmin, c.Xmax), Norm(j.Y, c.Ymin, c.Ymax))); l.Pressed |= j.Buttons != 0;
                    if (c.NumAxes >= 4) { var r = sticks[k++]; r.S.Add((Norm(j.Z, c.Zmin, c.Zmax), Norm(j.R, c.Rmin, c.Rmax))); r.Pressed |= j.Buttons != 0; }
                }
            }
            Thread.Sleep(4);
        }

        reg[0].Flags = RIDEV_REMOVE; reg[0].Target = IntPtr.Zero;
        RegisterRawInputDevices(reg, 1, Marshal.SizeOf<RAWINPUTDEVICE>());
        var mice = sink.Mice.ToList();
        sink.DestroyHandle();

        var items = new List<DriftItem>();
        foreach (var (dev, mo) in mice) items.Add(MouseItem(dev, mo, seconds));
        if (listening && mice.Count == 0)
            items.Add(new("mouse", "Ratón", "ok", "El ratón no se movió solo.", null, null));
        foreach (var st in sticks.Where(s => s.S.Count > 5)) items.Add(StickItem(st));
        return new DriftReport(seconds, mice.Count, xi.Count + wm.Count, items);
    }

    static DriftItem MouseItem(IntPtr dev, Mouse mo, int secs)
    {
        var name = dev == IntPtr.Zero ? "Puntero" : MouseName(dev);
        if (mo.Clicks > 0 || mo.Dist > 400)
            return new("mouse", name, "touched", "Se tocó durante la prueba.", "Suelta el ratón y no toques la mesa, y vuelve a probar.", null);
        if (dev == IntPtr.Zero && mo.Moves > 0)
            return new("mouse", name, "warn", "Algo del PC movió el puntero.", "No fue el ratón: lo movió un programa.", "Cierra programas de macros, escritorio remoto o mandos que muevan el puntero.");
        if (mo.Moves <= 2 && mo.Dist <= 3)
            return new("mouse", name, "ok", "No se movió solo.", null, null);
        var lvl = mo.Moves >= 20 || mo.Dist >= 40 ? "bad" : "warn";
        return new("mouse", name, lvl, $"Se movió solo: {mo.Moves} veces en {secs} s sin tocarlo.",
            lvl == "bad" ? "El sensor está leyendo movimiento que no hay: eso es el drift." : "Movimiento pequeño: polvo en el sensor o la alfombrilla.",
            "Limpia el sensor con aire o un bastoncillo seco y prueba en otra alfombrilla. Si sigue, cambia de puerto USB; si aun así se mueve, el sensor está fallando.");
    }

    static string MouseName(IntPtr dev)
    {
        try
        {
            int n = 0; GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, null, ref n);
            var sb = new StringBuilder(n + 1);
            if (GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, sb, ref n) <= 0) return "Ratón";
            var m = System.Text.RegularExpressions.Regex.Match(sb.ToString(), @"VID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return "Ratón";
            var vid = m.Groups[1].Value.ToUpperInvariant();
            var brand = vid switch
            {
                "046D" => "Logitech", "1532" => "Razer", "1038" => "SteelSeries", "1B1C" => "Corsair", "0951" or "03F0" => "HyperX",
                "045E" => "Microsoft", "1E7D" => "Roccat", "0B05" => "ASUS", _ => null
            };
            return brand is null ? $"Ratón (VID {vid})" : $"Ratón {brand}";
        }
        catch { return "Ratón"; }
    }

    static DriftItem StickItem(Stick st)
    {
        var name = $"{st.Pad} · stick {st.Side}";
        var xs = st.S.Select(p => p.x).OrderBy(v => v).ToList(); var ys = st.S.Select(p => p.y).OrderBy(v => v).ToList();
        double mx = xs[xs.Count / 2], my = ys[ys.Count / 2];
        double off = Math.Sqrt(mx * mx + my * my) * 100;
        double noise = st.S.Max(p => Math.Sqrt((p.x - mx) * (p.x - mx) + (p.y - my) * (p.y - my))) * 100;
        if (st.Pressed || noise > 45)
            return new("pad", name, "touched", "Se tocó durante la prueba.", "Deja el mando quieto sobre la mesa y vuelve a probar.", null);
        var rest = (int)Math.Round(off); var dz = (int)Math.Ceiling(Math.Min(off + noise, 100) + 3);
        if (off < 6 && noise < 6) return new("pad", name, "ok", $"Quieto en el centro ({rest} %).", null, null);
        var lvl = off >= 15 || noise >= 15 ? "bad" : "warn";
        return new("pad", name, lvl, $"Se va solo: descansa al {rest} % del centro.",
            lvl == "bad" ? "Eso es drift: el juego lo lee como si movieras el stick." : "Drift leve: en algunos juegos ya se nota.",
            $"Sube la zona muerta de ese stick en el juego a {Math.Min(dz, 40)} %. Si va a más, limpia el stick o cámbialo.");
    }

    // ---------- Hz: cada cuánto mandan datos el ratón y el mando ----------
    [DllImport("winmm.dll")] static extern int timeBeginPeriod(int ms);
    [DllImport("winmm.dll")] static extern int timeEndPeriod(int ms);

    /// <summary>Lo que se midió moviendo el ratón y el stick: Hz reales de cada uno.</summary>
    public sealed record RateItem(string Kind, string Name, int Hz, string Level, string Title, string? Fix);
    public sealed record RateReport(int Seconds, IReadOnlyList<RateItem> Items);

    sealed class RateCount
    {
        public readonly Dictionary<IntPtr, int[]> Buckets = new();   // informes por tramo de 50 ms, por ratón
        public readonly Dictionary<IntPtr, long[]> Dist = new();     // cuánto se movió en cada tramo (cuentas del sensor)
        public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        public int Slots;
        public RateCount(int slots) => Slots = slots;
        public void Read(IntPtr h)
        {
            int hdr = 8 + 2 * IntPtr.Size, size = 0;
            GetRawInputData(h, RID_INPUT, IntPtr.Zero, ref size, hdr);
            if (size <= 0 || size > 1024) return;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetRawInputData(h, RID_INPUT, buf, ref size, hdr) != size || Marshal.ReadInt32(buf) != RIM_TYPEMOUSE) return;
                var dev = Marshal.ReadIntPtr(buf, 8);
                int dx = Marshal.ReadInt32(buf, hdr + 12), dy = Marshal.ReadInt32(buf, hdr + 16);
                if (dev == IntPtr.Zero || (dx == 0 && dy == 0)) return;
                if (!Buckets.TryGetValue(dev, out var b)) { Buckets[dev] = b = new int[Slots]; Dist[dev] = new long[Slots]; }
                var i = (int)(Clock.ElapsedMilliseconds / 50);
                if (i < Slots) { b[i]++; Dist[dev][i] += Math.Abs(dx) + Math.Abs(dy); }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>Ventana invisible: solo para la línea de comandos (sin ventana de Vigía).</summary>
    sealed class RateSink : NativeWindow
    {
        readonly RateCount _c;
        public RateSink(RateCount c) { _c = c; CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) _c.Read(m.LParam);
            base.WndProc(ref m);
        }
    }

    // Windows 11 (24H2 en adelante) solo pasa ~125 Hz del ratón a las ventanas que no están al frente.
    // Por eso, con Vigía abierta, se mide en su propia ventana: si no, cualquier ratón saldría a 125 Hz.
    static volatile RateCount? _feed;
    /// <summary>La ventana de Vigía pasa aquí cada WM_INPUT mientras se mide.</summary>
    public static void Feed(IntPtr lParam) => _feed?.Read(lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    static readonly int[] Steps = [125, 250, 500, 1000, 2000, 4000, 8000];
    static int Snap(double hz) => hz < 90 ? (int)Math.Round(hz) : Steps.OrderBy(s => Math.Abs(Math.Log(hz / s))).First();

    /// <summary>Hz de un tramo: los tramos de 50 ms con movimiento seguido, el valor alto (no el medio: al frenar bajan).</summary>
    static double Rate(IEnumerable<int> buckets)
    {
        var busy = buckets.Where(n => n >= 2).OrderBy(n => n).ToList();
        if (busy.Count < 6) return 0;
        return busy[(int)(busy.Count * 0.8)] / 0.05;
    }

    /// <summary>Solo cuando el usuario pulsa «Medir»: unos segundos moviendo el ratón y el stick. No escribe nada.</summary>
    public static RateReport Rates(int seconds, Control? host = null)
    {
        seconds = Math.Clamp(seconds, 3, 15);
        RateReport? rep = null; Exception? err = null;
        var t = new Thread(() => { try { rep = MeasureRates(seconds, host); } catch (Exception e) { err = e; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw err;
        return rep!;
    }

    static RateReport MeasureRates(int seconds, Control? host)
    {
        int slots = seconds * 20;
        var count = new RateCount(slots);
        RateSink? sink = null; IntPtr target;
        if (host is not null) { target = host.Handle; _feed = count; }
        else { sink = new RateSink(count); target = sink.Handle; }
        var reg = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = target } };
        void Reg() => RegisterRawInputDevices(reg, 1, Marshal.SizeOf<RAWINPUTDEVICE>());
        if (host is not null) host.Invoke(Reg); else Reg();
        int front = 0, looks = 0;
        var xi = new List<int>();
        for (int i = 0; i < 4; i++) { try { if (XInputGetState(i, out _) == 0) xi.Add(i); } catch (DllNotFoundException) { break; } }
        var pads = xi.ToDictionary(i => i, _ => new int[slots]);
        var last = xi.ToDictionary(i => i, _ => 0u);
        timeBeginPeriod(1);   // solo durante la medida, para leer el mando cada 1 ms
        try
        {
            var end = seconds * 1000L;
            while (count.Clock.ElapsedMilliseconds < end)
            {
                if (sink is not null) while (PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessage(ref m); }
                else if (++looks % 20 == 0 && GetForegroundWindow() == target) front++;
                var slot = (int)(count.Clock.ElapsedMilliseconds / 50);
                foreach (var i in xi)
                    if (XInputGetState(i, out var s) == 0 && s.Packet != last[i])
                    {
                        // solo cuenta si el stick está fuera del centro: así se mide mientras se mueve
                        if (last[i] != 0 && slot < slots && (Math.Abs((int)s.LX) > 6000 || Math.Abs((int)s.LY) > 6000 || Math.Abs((int)s.RX) > 6000 || Math.Abs((int)s.RY) > 6000)) pads[i][slot]++;
                        last[i] = s.Packet;
                    }
                Thread.Sleep(1);
            }
        }
        finally
        {
            timeEndPeriod(1);
            reg[0].Flags = RIDEV_REMOVE; reg[0].Target = IntPtr.Zero;
            if (host is not null) { host.Invoke(Reg); _feed = null; } else Reg();
        }
        var mice = count.Buckets.ToList();
        sink?.DestroyHandle();
        // ¿se cambió de ventana durante la medida? entonces Windows recortó el ratón y el número no vale
        var away = host is not null && looks >= 20 && front < looks / 20 * 0.8;

        var items = new List<RateItem>();
        foreach (var (dev, b) in mice)
        {
            var raw = Rate(b); if (raw == 0) continue;
            var hz = Snap(raw); var name = MouseName(dev);
            if (away && hz <= 125) { items.Add(new("mouse", name, 0, "touched", "Cambiaste de ventana durante la medida.", "Vuelve a medir con Vigía al frente.")); continue; }
            items.Add(hz >= 900 ? new("mouse", name, hz, "ok", $"{hz} Hz: responde rápido.", null)
                : hz >= 450 ? new("mouse", name, hz, "ok", $"{hz} Hz: bien.", "Si tu ratón llega a 1000 Hz, súbelo en su programa o con su botón.")
                : new("mouse", name, hz, "warn", $"{hz} Hz: cada movimiento llega hasta {1000 / Math.Max(hz, 1)} ms tarde.", "Súbelo a 1000 Hz en el programa del ratón o con su botón de abajo."));
        }
        if (items.Count == 0 && mice.Count > 0) items.Add(new("mouse", "Ratón", 0, "touched", "Lo moviste muy poco.", "Muévelo en círculos rápidos durante toda la medida."));
        foreach (var i in xi)
        {
            var raw = Rate(pads[i]);
            if (raw == 0) { items.Add(new("pad", $"Mando {i + 1}", 0, "touched", "No moviste el stick.", "Gira un stick sin parar durante la medida.")); continue; }
            var hz = (int)Math.Round(raw);
            items.Add(hz >= 240 ? new("pad", $"Mando {i + 1}", hz, "ok", $"{hz} Hz: responde rápido.", null)
                : new("pad", $"Mando {i + 1}", hz, "warn", $"{hz} Hz: cada movimiento llega hasta {1000 / Math.Max(hz, 1)} ms tarde.",
                    "Por cable suele ir más rápido que inalámbrico. Si lo usas con un programa que traduce el mando, prueba sin él."));
        }
        return new RateReport(seconds, items);
    }

    // ---------- Dentro de «Analizar» de Sugerencias: sin pedirle nada al usuario ----------

    /// <summary>
    /// Mientras Sugerencias analiza (unos segundos), escucha el ratón en la ventana de Vigía y lee los sticks.
    /// Ratón: si se movió lo bastante, saca sus Hz. Solo avisa de Hz bajos si cada informe trae varios puntos de
    /// movimiento juntos: un ratón de 1000 Hz movido despacio también manda pocos informes, pero de uno en uno.
    /// Mando: si un stick quieto descansa fuera del centro, es drift. Lo que no se pueda medir, no sale. No escribe nada.
    /// </summary>
    public static List<Suggestion> Sense(Control host, int ms = 3000)
    {
        List<Suggestion> res = new();
        var t = new Thread(() => { try { res = SenseCore(host, Math.Clamp(ms, 1000, 8000)); } catch { } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        return res;
    }

    static List<Suggestion> SenseCore(Control host, int ms)
    {
        int slots = ms / 50 + 1;
        var count = new RateCount(slots);
        var target = host.Handle; _feed = count;
        var reg = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = target } };
        void Reg() => RegisterRawInputDevices(reg, 1, Marshal.SizeOf<RAWINPUTDEVICE>());
        host.Invoke(Reg);

        var sticks = new List<Stick>();
        var xi = new List<int>();
        for (int i = 0; i < 4; i++) { try { if (XInputGetState(i, out _) == 0) xi.Add(i); } catch (DllNotFoundException) { break; } }
        var wm = new List<(int id, JOYCAPS caps)>();
        if (xi.Count == 0)
        {
            int n = Math.Min(joyGetNumDevs(), 16);
            for (int i = 0; i < n; i++)
            {
                var j = new JOYINFOEX { Size = Marshal.SizeOf<JOYINFOEX>(), Flags = 0xFF };
                if (joyGetPosEx(i, ref j) != 0) continue;
                var c = new JOYCAPS();
                if (joyGetDevCapsW(new IntPtr(i), ref c, Marshal.SizeOf<JOYCAPS>()) == 0) wm.Add((i, c));
            }
        }
        foreach (var i in xi) { sticks.Add(new Stick { Pad = $"Mando {i + 1}", Side = "izquierdo" }); sticks.Add(new Stick { Pad = $"Mando {i + 1}", Side = "derecho" }); }
        foreach (var (id, c) in wm)
        {
            sticks.Add(new Stick { Pad = $"Mando {id + 1}", Side = "izquierdo" });
            if (c.NumAxes >= 4) sticks.Add(new Stick { Pad = $"Mando {id + 1}", Side = "derecho" });
        }

        int front = 0, looks = 0;
        try
        {
            while (count.Clock.ElapsedMilliseconds < ms)
            {
                looks++; if (GetForegroundWindow() == target) front++;
                int k = 0;
                foreach (var i in xi)
                {
                    if (XInputGetState(i, out var s) != 0) { k += 2; continue; }
                    bool pressed = s.Buttons != 0 || s.LT > 40 || s.RT > 40;
                    var l = sticks[k++]; var r = sticks[k++];
                    l.S.Add((s.LX / 32768.0, s.LY / 32768.0)); r.S.Add((s.RX / 32768.0, s.RY / 32768.0));
                    l.Pressed |= pressed; r.Pressed |= pressed;
                }
                foreach (var (id, c) in wm)
                {
                    var j = new JOYINFOEX { Size = Marshal.SizeOf<JOYINFOEX>(), Flags = 0xFF };
                    if (joyGetPosEx(id, ref j) != 0) { k += c.NumAxes >= 4 ? 2 : 1; continue; }
                    var l = sticks[k++];
                    l.S.Add((Norm(j.X, c.Xmin, c.Xmax), Norm(j.Y, c.Ymin, c.Ymax))); l.Pressed |= j.Buttons != 0;
                    if (c.NumAxes >= 4) { var r = sticks[k++]; r.S.Add((Norm(j.Z, c.Zmin, c.Zmax), Norm(j.R, c.Rmin, c.Rmax))); r.Pressed |= j.Buttons != 0; }
                }
                Thread.Sleep(20);
            }
        }
        finally
        {
            reg[0].Flags = RIDEV_REMOVE; reg[0].Target = IntPtr.Zero;
            try { host.Invoke(Reg); } catch { }
            _feed = null;
        }

        var res = new List<Suggestion>();
        // ratón: solo si Vigía estuvo al frente (si no, Windows lo recorta a ~125 Hz y el número no vale)
        if (looks > 0 && front >= looks * 0.9)
            foreach (var (dev, b) in count.Buckets.ToList())
            {
                var d = count.Dist[dev];
                var busy = Enumerable.Range(0, slots).Where(i => b[i] >= 2).ToList();
                if (busy.Count < 6) continue;
                var hz = Snap(Rate(b));
                double per = (double)busy.Sum(i => d[i]) / Math.Max(1, busy.Sum(i => b[i]));
                if (hz > 250 || per < 3) continue;   // va rápido, o se movió muy despacio para saberlo
                res.Add(new("in-mouse-hz", "warn", $"Tu ratón va a {hz} Hz",
                    [$"Cada movimiento llega hasta {1000 / Math.Max(hz, 1)} ms tarde.", "Lo notas como input lag al apuntar."],
                    ["Súbelo a 1000 Hz en el programa del ratón o con su botón de abajo."], null,
                    [MouseName(dev), $"{hz} Hz medidos mientras movías el ratón"]));
            }
        foreach (var st in sticks.Where(s => s.S.Count > 20))
        {
            var it = StickItem(st);
            if (it.Level is not ("warn" or "bad")) continue;
            res.Add(new($"in-drift-{st.Pad}-{st.Side}", it.Level == "bad" ? "bad" : "warn",
                st.Side == "izquierdo" ? "El stick izquierdo del mando se mueve solo" : "El stick derecho del mando se mueve solo",
                [it.Detail ?? ""], [it.Fix ?? ""], null, [st.Pad, it.Title]));
        }
        return res;
    }
}
