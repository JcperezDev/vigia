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
}
