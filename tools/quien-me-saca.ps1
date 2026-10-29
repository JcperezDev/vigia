# Quién me saca del juego: anota cada ventana que se pone delante, con su programa y la hora.
# Uso: clic derecho > "Ejecutar con PowerShell", minimizar esta ventana y jugar. Al volver, cerrar esta ventana.
# El resultado queda en el Escritorio: quien-me-saca.txt. No cambia nada del PC ni manda nada a internet.
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'quien-me-saca.txt'
$src = @'
using System; using System.Diagnostics; using System.IO; using System.Runtime.InteropServices; using System.Text;
public static class FocusLog {
  delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
  [DllImport("user32")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
  [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
  [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
  [DllImport("user32")] static extern int GetMessageW(out MSG m, IntPtr h, uint a, uint b);
  [DllImport("user32")] static extern IntPtr DispatchMessageW(ref MSG m);
  static WinEventProc keep; static string file; static DateTime since = DateTime.Now; static string last = "";
  static void Write(string s) { File.AppendAllText(file, s + Environment.NewLine, Encoding.UTF8); }
  static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time) {
    var now = DateTime.Now;
    uint pid; GetWindowThreadProcessId(hwnd, out pid);
    string name = "?", path = "";
    try { var p = Process.GetProcessById((int)pid); name = p.ProcessName + ".exe"; try { path = p.MainModule.FileName; } catch { } } catch { }
    var t = new StringBuilder(200); GetWindowTextW(hwnd, t, 200);
    var c = new StringBuilder(100); GetClassNameW(hwnd, c, 100);
    var stay = (now - since).TotalMilliseconds;
    // la ventana de antes estuvo delante muy poco: lo típico de algo que roba el foco y lo suelta
    if (last.Length > 0 && stay < 3000) Write("      ^ estuvo delante solo " + (int)stay + " ms  <-- SOSPECHOSO");
    since = now; last = name;
    Write(now.ToString("HH:mm:ss.fff") + "  " + name + " (PID " + pid + ")  \"" + t + "\"  [" + c + "]  " + path);
  }
  public static void Run(string f) {
    file = f; keep = OnEvent;
    Write("=== Empieza " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
    SetWinEventHook(3, 3, IntPtr.Zero, keep, 0, 0, 0);   // EVENT_SYSTEM_FOREGROUND, fuera del proceso
    MSG m; while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) DispatchMessageW(ref m);
  }
}
'@
Add-Type -TypeDefinition $src
Write-Host "Anotando en $out"
Write-Host "Minimiza esta ventana y juega. Cuando te saque del juego, vuelve y cierra esta ventana."
[FocusLog]::Run($out)
