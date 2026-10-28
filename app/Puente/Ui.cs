using System.Runtime.InteropServices;

namespace Puente;

/// <summary>
/// Ventana de Puente. Win32 y GDI a mano: Puente va recortado (trimmed) y WinForms/WPF no se pueden recortar,
/// así que con esto pesa 12 MB y no 150. Colores y medidas de Vigía.
/// El hilo de trabajo cambia el estado (Show/Choose); el hilo de la ventana lo pinta.
/// </summary>
static class Ui
{
    // ---------------------------------------------------------------- estado
    static readonly object Gate = new();
    static string _main = "", _sub = "";
    static double? _progress;            // null = sin barra; double.NaN = trabajando sin porcentaje
    static string[] _buttons = [];
    static int _hover = -1, _tick;
    static TaskCompletionSource<int>? _choice;
    static bool _locked, _closing;
    static IntPtr _hwnd;
    static float _s = 1;
    static WndProc? _proc;               // referencia viva: Windows guarda solo el puntero
    static IntPtr _fTitle, _fMain, _fSub, _fBtn;
    static readonly ManualResetEventSlim Ready = new();

    public static bool Active => _hwnd != IntPtr.Zero;

    // colores de Vigía (app.css, oklch pasado a sRGB)
    static readonly int Ink = Rgb(15, 17, 19), Ink3 = Rgb(30, 33, 37), Line = Rgb(44, 47, 51),
        Fg = Rgb(233, 235, 238), Fg3 = Rgb(145, 150, 156), Accent = Rgb(88, 165, 228), AccentHi = Rgb(118, 183, 236),
        Ok = Rgb(109, 200, 143), Hot = Rgb(239, 103, 90);

    static int Rgb(int r, int g, int b) => r | (g << 8) | (b << 16);

    // ---------------------------------------------------------------- API para Program
    public static void Start()
    {
        var t = new Thread(Loop) { IsBackground = true, Name = "ventana" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Ready.Wait(5000);
    }

    /// <summary>Texto principal, detalle y barra (null = sin barra, NaN = en marcha sin porcentaje, 0..1 = porcentaje).</summary>
    public static void Show(string main, string sub = "", double? progress = null)
    {
        lock (Gate) { _tone = Tone(main); _main = L.T(main); _sub = L.T(sub); _progress = progress; _buttons = []; _hover = -1; }
        Redraw();
    }

    /// <summary>Muestra botones y espera. Devuelve el índice pulsado o -1 si se cerró la ventana. El primero es el principal.</summary>
    public static int Choose(string main, string sub, params string[] buttons)
    {
        if (!Active) return 0;
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate) { _tone = Tone(main); _main = L.T(main); _sub = L.T(sub); _progress = null; _buttons = [.. buttons.Select(L.T)]; _hover = -1; _choice = tcs; }
        Redraw();
        return tcs.Task.GetAwaiter().GetResult();
    }

    // el color del título se decide con el texto en español, antes de traducirlo
    static int _tone;
    static int Tone(string es) => es.StartsWith("No se pudo") ? -1 : es.StartsWith("Listo") ? 1 : 0;

    /// <summary>Mientras se cambian las carpetas no se deja cerrar la ventana.</summary>
    public static void Lock(bool on) { lock (Gate) _locked = on; }

    public static void Close()
    {
        if (!Active) return;
        _closing = true;
        PostMessageW(_hwnd, WM_CLOSE, 0, 0);
    }

    static void Redraw() { if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false); }

    // ---------------------------------------------------------------- ventana
    const int W = 500, H = 204, Pad = 28;

    static void Loop()
    {
        try { SetProcessDpiAwarenessContext(-4); } catch { }
        _s = GetDpiForSystem() / 96f;
        var hinst = GetModuleHandleW(null);
        _proc = Proc;
        IntPtr big = 0, small = 0;
        try { ExtractIconExW(Environment.ProcessPath!, 0, out big, out small, 1); } catch { }
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hinst, hCursor = LoadCursorW(0, IDC_ARROW), lpszClassName = "Vigia.Puente", hIcon = big, hIconSm = small,
        };
        RegisterClassExW(ref wc);

        var style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
        var r = new RECT { right = Px(W), bottom = Px(H) };
        AdjustWindowRectExForDpi(ref r, style, false, 0, (uint)(96 * _s));
        int ww = r.right - r.left, wh = r.bottom - r.top;
        int x = (GetSystemMetrics(0) - ww) / 2, y = (GetSystemMetrics(1) - wh) / 2;
        _hwnd = CreateWindowExW(0, "Vigia.Puente", "Puente · Vigía", style, x, y, ww, wh, 0, 0, hinst, 0);

        int dark = 1; DwmSetWindowAttribute(_hwnd, 20, ref dark, 4);   // barra de título oscura
        int cap = Ink; DwmSetWindowAttribute(_hwnd, 35, ref cap, 4);   // mismo color que el fondo (Windows 11)

        _fTitle = Font(19, 600); _fMain = Font(15, 400); _fSub = Font(13, 400); _fBtn = Font(13, 600);
        ShowWindow(_hwnd, 5);
        SetForegroundWindow(_hwnd);
        SetTimer(_hwnd, 1, 33, 0);
        Ready.Set();

        while (GetMessageW(out var m, 0, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
    }

    static int Px(float v) => (int)Math.Round(v * _s);
    static IntPtr Font(int px, int weight) => CreateFontW(-Px(px), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI");

    static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_ERASEBKGND: return 1;
            case WM_PAINT: Paint(h); return 0;
            case WM_TIMER:
                bool spin; lock (Gate) spin = _progress is double p && double.IsNaN(p);
                if (spin) { _tick++; Redraw(); }
                return 0;
            case WM_MOUSEMOVE:
            {
                var hit = Hit((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
                if (hit != _hover) { _hover = hit; Redraw(); }
                var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 2 /* LEAVE */, hwndTrack = h };
                TrackMouseEvent(ref tme);
                return 0;
            }
            case WM_MOUSELEAVE: if (_hover != -1) { _hover = -1; Redraw(); } return 0;
            case WM_SETCURSOR:
                if (_hover >= 0 && (l & 0xFFFF) == 1 /* HTCLIENT */) { SetCursor(LoadCursorW(0, IDC_HAND)); return 1; }
                break;
            case WM_LBUTTONUP:
            {
                var hit = Hit((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
                if (hit >= 0) Pick(hit);
                return 0;
            }
            case WM_KEYDOWN:
                if (w == 0x0D) { Pick(0); return 0; }
                if (w == 0x1B) { PostMessageW(h, WM_CLOSE, 0, 0); return 0; }
                break;
            case WM_CLOSE:
                if (!_closing)
                {
                    bool locked; TaskCompletionSource<int>? c;
                    lock (Gate) { locked = _locked; c = _choice; }
                    if (locked) return 0;
                    if (c is not null) { lock (Gate) _choice = null; c.TrySetResult(-1); }
                    else Environment.Exit(1);   // cerrar a mitad de descarga: no se instaló nada
                }
                DestroyWindow(h);
                return 0;
            case WM_DESTROY: PostQuitMessage(0); return 0;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    static void Pick(int i)
    {
        TaskCompletionSource<int>? c;
        lock (Gate) { if (i >= _buttons.Length) return; c = _choice; _choice = null; _buttons = []; _hover = -1; }
        Redraw();
        c?.TrySetResult(i);
    }

    // ---------------------------------------------------------------- dibujo
    static RECT[] ButtonRects(IntPtr hdc, string[] buttons)
    {
        var rects = new RECT[buttons.Length];
        int right = Px(W - Pad), top = Px(H - Pad - 34), bottom = Px(H - Pad);
        for (var i = 0; i < buttons.Length; i++)   // el principal queda a la derecha
        {
            var t = new RECT();
            var old = SelectObject(hdc, _fBtn);
            DrawTextW(hdc, buttons[i], -1, ref t, DT_CALCRECT | DT_SINGLELINE | DT_NOPREFIX);
            SelectObject(hdc, old);
            var width = Math.Max(t.right - t.left + Px(36), Px(96));
            rects[i] = new RECT { left = right - width, top = top, right = right, bottom = bottom };
            right -= width + Px(8);
        }
        return rects;
    }

    static int Hit(int x, int y)
    {
        string[] b; lock (Gate) b = _buttons;
        if (b.Length == 0) return -1;
        var hdc = GetDC(_hwnd);
        try
        {
            var rs = ButtonRects(hdc, b);
            for (var i = 0; i < rs.Length; i++)
                if (x >= rs[i].left && x < rs[i].right && y >= rs[i].top && y < rs[i].bottom) return i;
            return -1;
        }
        finally { ReleaseDC(_hwnd, hdc); }
    }

    static void Paint(IntPtr h)
    {
        string main, sub; double? prog; string[] buttons; int hover;
        lock (Gate) { main = _main; sub = _sub; prog = _progress; buttons = _buttons; hover = _hover; }

        var ps = Marshal.AllocHGlobal(128);
        var screen = BeginPaint(h, ps);
        GetClientRect(h, out var cr);
        var dc = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, cr.right, cr.bottom);
        var oldBmp = SelectObject(dc, bmp);
        try
        {
            Draw(dc, cr, main, sub, prog, buttons, hover);
            BitBlt(screen, 0, 0, cr.right, cr.bottom, dc, 0, 0, 0x00CC0020 /* SRCCOPY */);
        }
        finally
        {
            SelectObject(dc, oldBmp); DeleteObject(bmp); DeleteDC(dc);
            EndPaint(h, ps); Marshal.FreeHGlobal(ps);
        }
    }

    /// <summary>Solo para revisar el diseño: dibuja un estado en un .bmp sin abrir ventana.</summary>
    public static void Render(string path, string main, string sub, double? prog, string[] buttons, int hover = -1, float scale = 1.5f)
    {
        _s = scale; _tick = 30;
        _tone = Tone(main); main = L.T(main); sub = L.T(sub); buttons = [.. buttons.Select(L.T)];
        _fTitle = Font(19, 600); _fMain = Font(15, 400); _fSub = Font(13, 400); _fBtn = Font(13, 600);
        var cr = new RECT { right = Px(W), bottom = Px(H) };
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, cr.right, cr.bottom);
        var oldBmp = SelectObject(dc, bmp);
        Draw(dc, cr, main, sub, prog, buttons, hover);
        // BMP de 24 bits a mano: sin System.Drawing
        int w = cr.right, hh = cr.bottom, stride = (w * 3 + 3) & ~3;
        var px = new byte[stride * hh];
        var bi = new byte[40];
        BitConverter.GetBytes(40).CopyTo(bi, 0); BitConverter.GetBytes(w).CopyTo(bi, 4); BitConverter.GetBytes(hh).CopyTo(bi, 8);
        BitConverter.GetBytes((short)1).CopyTo(bi, 12); BitConverter.GetBytes((short)24).CopyTo(bi, 14);
        SelectObject(dc, oldBmp);
        GetDIBits(dc, bmp, 0, (uint)hh, px, bi, 0);
        using (var f = File.Create(path))
        {
            var hdr = new byte[14]; hdr[0] = (byte)'B'; hdr[1] = (byte)'M';
            BitConverter.GetBytes(54 + px.Length).CopyTo(hdr, 2); BitConverter.GetBytes(54).CopyTo(hdr, 10);
            f.Write(hdr); f.Write(bi); f.Write(px);
        }
        DeleteObject(bmp); DeleteDC(dc); ReleaseDC(0, screen);
    }

    static void Draw(IntPtr dc, RECT cr, string main, string sub, double? prog, string[] buttons, int hover)
    {
        {
            Fill(dc, cr, Ink);
            SetBkMode(dc, 1);

            // cabecera: nombre y, en gris, quién está trabajando
            Text(dc, "Vigía", _fTitle, Fg, Px(Pad), Px(22), Px(W - Pad), Px(50), DT_SINGLELINE);
            Text(dc, "Puente", _fSub, Fg3, Px(Pad), Px(26), Px(W - Pad), Px(50), DT_SINGLELINE | DT_RIGHT);
            Fill(dc, new RECT { left = Px(Pad), top = Px(58), right = Px(W - Pad), bottom = Px(58) + Math.Max(1, Px(1)) }, Line);

            var mainColor = _tone < 0 ? Hot : _tone > 0 ? Ok : Fg;
            Text(dc, main, _fMain, mainColor, Px(Pad), Px(76), Px(W - Pad), Px(100), DT_SINGLELINE | DT_END_ELLIPSIS);
            Text(dc, sub, _fSub, Fg3, Px(Pad), Px(100), Px(W - Pad), Px(138), DT_WORDBREAK | DT_END_ELLIPSIS);

            if (prog is double p)
            {
                var track = new RECT { left = Px(Pad), top = Px(146), right = Px(W - Pad), bottom = Px(150) };
                Fill(dc, track, Ink3);
                int tw = track.right - track.left;
                if (double.IsNaN(p))
                {
                    // un tramo que recorre la barra: Puente está trabajando pero no sabe cuánto falta
                    int seg = tw / 4, span = tw + seg, pos = (int)(_tick * Px(6) % span) - seg;
                    Fill(dc, new RECT { left = track.left + Math.Max(0, pos), top = track.top, right = track.left + Math.Min(tw, pos + seg), bottom = track.bottom }, Accent);
                }
                else Fill(dc, new RECT { left = track.left, top = track.top, right = track.left + (int)(tw * Math.Clamp(p, 0, 1)), bottom = track.bottom }, main.StartsWith("Listo") ? Ok : Accent);
            }

            var rs = ButtonRects(dc, buttons);
            for (var i = 0; i < rs.Length; i++)
            {
                bool primary = i == 0, hot = i == hover;
                var bg = primary ? (hot ? AccentHi : Accent) : (hot ? Line : Ink3);
                var brush = CreateSolidBrush(bg); var pen = CreatePen(0, 1, primary ? bg : Line);
                var ob = SelectObject(dc, brush); var op = SelectObject(dc, pen);
                RoundRect(dc, rs[i].left, rs[i].top, rs[i].right, rs[i].bottom, Px(12), Px(12));
                SelectObject(dc, ob); SelectObject(dc, op); DeleteObject(brush); DeleteObject(pen);
                Text(dc, buttons[i], _fBtn, primary ? Ink : Fg, rs[i].left, rs[i].top, rs[i].right, rs[i].bottom, DT_SINGLELINE | DT_CENTER | DT_VCENTER);
            }
        }
    }

    static void Fill(IntPtr dc, RECT r, int color)
    {
        var b = CreateSolidBrush(color);
        FillRect(dc, ref r, b);
        DeleteObject(b);
    }

    static void Text(IntPtr dc, string s, IntPtr font, int color, int l, int t, int r, int b, uint flags)
    {
        if (s.Length == 0) return;
        var rc = new RECT { left = l, top = t, right = r, bottom = b };
        var old = SelectObject(dc, font);
        SetTextColor(dc, color);
        DrawTextW(dc, s, -1, ref rc, flags | DT_NOPREFIX);
        SelectObject(dc, old);
    }

    // ---------------------------------------------------------------- Win32
    delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y, priv; }
    [StructLayout(LayoutKind.Sequential)] struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    const uint WS_OVERLAPPED = 0, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
    const uint WM_DESTROY = 0x02, WM_PAINT = 0x0F, WM_CLOSE = 0x10, WM_ERASEBKGND = 0x14, WM_SETCURSOR = 0x20, WM_KEYDOWN = 0x100,
        WM_TIMER = 0x113, WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_MOUSELEAVE = 0x2A3;
    const uint DT_CENTER = 0x1, DT_RIGHT = 0x2, DT_VCENTER = 0x4, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20, DT_CALCRECT = 0x400,
        DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000;
    static readonly IntPtr IDC_ARROW = 32512, IDC_HAND = 32649;

    const string U = "user32.dll", G = "gdi32.dll";
    [DllImport(U, CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport(U, CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport(U)] static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport(U)] static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport(U)] static extern bool TranslateMessage(ref MSG m);
    [DllImport(U)] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport(U)] static extern void PostQuitMessage(int code);
    [DllImport(U)] static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport(U)] static extern bool DestroyWindow(IntPtr h);
    [DllImport(U)] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport(U)] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport(U)] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
    [DllImport(U)] static extern IntPtr BeginPaint(IntPtr h, IntPtr ps);
    [DllImport(U)] static extern bool EndPaint(IntPtr h, IntPtr ps);
    [DllImport(U)] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport(U)] static extern IntPtr GetDC(IntPtr h);
    [DllImport(U)] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport(U)] static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
    [DllImport(U, CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr dc, string s, int n, ref RECT r, uint fmt);
    [DllImport(U)] static extern IntPtr LoadCursorW(IntPtr inst, IntPtr id);
    [DllImport(U)] static extern IntPtr SetCursor(IntPtr c);
    [DllImport(U)] static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
    [DllImport(U)] static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr fn);
    [DllImport(U)] static extern int GetSystemMetrics(int i);
    [DllImport(U)] static extern uint GetDpiForSystem();
    [DllImport(U)] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport(U)] static extern bool AdjustWindowRectExForDpi(ref RECT r, uint style, bool menu, uint ex, uint dpi);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint n);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport(G)] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport(G)] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, byte[] info, uint usage);
    [DllImport(G)] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport(G)] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport(G)] static extern bool DeleteObject(IntPtr o);
    [DllImport(G)] static extern bool DeleteDC(IntPtr dc);
    [DllImport(G)] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport(G)] static extern IntPtr CreateSolidBrush(int color);
    [DllImport(G)] static extern IntPtr CreatePen(int style, int width, int color);
    [DllImport(G)] static extern bool RoundRect(IntPtr dc, int l, int t, int r, int b, int w, int h);
    [DllImport(G)] static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport(G)] static extern int SetTextColor(IntPtr dc, int color);
    [DllImport(G, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
        uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
}
