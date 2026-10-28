using System.Runtime.InteropServices;

namespace Vigia;

/// <summary>
/// Crea una resolución personalizada en el driver de la tarjeta, igual que «Cambiar resolución › Personalizar»
/// del Panel de control de NVIDIA o «Resoluciones personalizadas» de AMD Software. Queda guardada en el panel,
/// así los juegos la ven como una más y se puede borrar desde ahí.
/// NVIDIA: NVAPI (nvapi64.dll, viene con el driver). AMD: ADL (atiadlxx.dll, viene con el driver).
/// Nada de drivers ni programas de terceros. Intel y otras: no hay API pública; se explica cómo hacerlo a mano.
/// </summary>
public static class GpuModes
{
    public enum Vendor { None, Nvidia, Amd, Other }

    public static Vendor Detect()
    {
        if (NativeLibrary.TryLoad("nvapi64.dll", out var h)) { NativeLibrary.Free(h); return Vendor.Nvidia; }
        if (NativeLibrary.TryLoad("atiadlxx.dll", out h)) { NativeLibrary.Free(h); return Vendor.Amd; }
        return Vendor.Other;
    }

    /// <summary>Crea w × h a hz en la pantalla principal. Devuelve null si salió bien, o el motivo.</summary>
    public static string? Create(int w, int h, int hz, int nativeW, int nativeH) => Detect() switch
    {
        Vendor.Nvidia => Nv.Create(w, h, hz),
        Vendor.Amd => Amd.Create(w, h, hz, nativeW, nativeH),
        _ => "tu tarjeta gráfica no deja crear resoluciones desde fuera de su panel",
    };

    /// <summary>Para pruebas: dice qué haría sin cambiar nada (solo lee).</summary>
    public static string Probe(int w, int h, int hz) => Detect() switch
    {
        Vendor.Nvidia => Nv.Probe(w, h, hz),
        Vendor.Amd => "AMD: ADL presente",
        var v => v.ToString(),
    };

    // ================= NVIDIA (NVAPI) =================
    static class Nv
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PrimaryIdFn(out uint displayId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetTimingFn(uint displayId, ref TimingInput input, ref Timing timing);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TryFn(uint[] ids, uint count, ref CustomDisplay cd);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SaveFn(uint[] ids, uint count, uint outputOnly, uint monitorOnly);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int RevertFn(uint[] ids, uint count);

        // ids de nvapi_interface.h (SDK público de NVIDIA)
        const uint ID_Initialize = 0x0150e828, ID_PrimaryId = 0x1e9d8a31, ID_GetTiming = 0x175167e9,
                   ID_Try = 0x1f7db630, ID_Save = 0x49882876, ID_Revert = 0xcbbd40f0;
        const int TIMING_AUTO = 1, TIMING_CVT_RB = 6;

        // Mismo orden y alineación que nvapi.h (pack 8; todo cae alineado a 4)
        [StructLayout(LayoutKind.Sequential)]
        struct TimingFlag { public uint A; public uint B; public uint Scaling; }   // bitfields de NV_TIMING_FLAG: 12 bytes

        [StructLayout(LayoutKind.Sequential)]
        struct TimingInput { public uint Version; public uint Width; public uint Height; public float Rr; public TimingFlag Flag; public int Type; }

        [StructLayout(LayoutKind.Sequential)]
        struct Timing
        {
            public ushort HVisible, HBorder, HFrontPorch, HSyncWidth, HTotal; public byte HSyncPol;
            public ushort VVisible, VBorder, VFrontPorch, VSyncWidth, VTotal; public byte VSyncPol;
            public ushort Interlaced; public uint Pclk;
            public uint EtcFlag; public ushort EtcRr; public uint EtcRrx1k; public uint EtcAspect; public ushort EtcRep; public uint EtcStatus;
            public uint N0, N1, N2, N3, N4, N5, N6, N7, N8, N9;   // name[40]
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CustomDisplay
        {
            public uint Version; public uint Width; public uint Height; public uint Depth; public int ColorFormat;
            public float SrcX, SrcY, SrcW, SrcH; public float XRatio, YRatio;
            public Timing Timing; public uint HwModeSetOnly;
        }

        static T Fn<T>(uint id) where T : Delegate
        {
            var p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException($"NVAPI 0x{id:x8}");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        static uint Ver<T>(int v) => (uint)(Marshal.SizeOf<T>() | (v << 16));

        static (uint id, Timing t, int r) Prepare(int w, int h, int hz)
        {
            var r = Fn<InitFn>(ID_Initialize)();
            if (r != 0) return (0, default, r);
            r = Fn<PrimaryIdFn>(ID_PrimaryId)(out var id);
            if (r != 0) return (0, default, r);
            var get = Fn<GetTimingFn>(ID_GetTiming);
            // «Automático» del panel de NVIDIA; si el driver no la calcula, CVT con borrado reducido
            var t = default(Timing);
            foreach (var type in new[] { TIMING_AUTO, TIMING_CVT_RB })
            {
                var input = new TimingInput { Version = Ver<TimingInput>(1), Width = (uint)w, Height = (uint)h, Rr = hz, Type = type };
                t = default;
                r = get(id, ref input, ref t);
                if (r == 0 && t.HVisible == w && t.VVisible == h && t.Pclk > 0) return (id, t, 0);
            }
            return (id, t, r != 0 ? r : -9);
        }

        public static string Probe(int w, int h, int hz)
        {
            try
            {
                var (id, t, r) = Prepare(w, h, hz);
                return r != 0 ? $"NVIDIA: error {r} (id {id:x})"
                    : $"NVIDIA id {id:x}: {t.HVisible}x{t.VVisible} total {t.HTotal}x{t.VTotal} pclk {t.Pclk / 100.0:0.00} MHz · tamaños {Marshal.SizeOf<TimingInput>()}/{Marshal.SizeOf<Timing>()}/{Marshal.SizeOf<CustomDisplay>()}";
            }
            catch (Exception e) { return "NVIDIA: " + e.Message; }
        }

        public static string? Create(int w, int h, int hz)
        {
            try
            {
                var (id, t, r) = Prepare(w, h, hz);
                if (r != 0) return $"el driver de NVIDIA no calculó la señal para {w} × {h} a {hz} Hz (código {r})";
                var cd = new CustomDisplay
                {
                    Version = Ver<CustomDisplay>(1), Width = (uint)w, Height = (uint)h, Depth = 32, ColorFormat = 0,
                    SrcX = 0, SrcY = 0, SrcW = 1, SrcH = 1, XRatio = 1, YRatio = 1, Timing = t,
                };
                var ids = new[] { id };
                // «Probar» del panel: la pone un momento; si el driver la acepta se guarda, si no se deshace
                r = Fn<TryFn>(ID_Try)(ids, 1, ref cd);
                if (r != 0) { try { Fn<RevertFn>(ID_Revert)(ids, 1); } catch { } return $"el driver de NVIDIA no aceptó {w} × {h} a {hz} Hz (código {r})"; }
                r = Fn<SaveFn>(ID_Save)(ids, 1, 1, 1);
                if (r != 0) { try { Fn<RevertFn>(ID_Revert)(ids, 1); } catch { } return $"el driver de NVIDIA no la guardó (código {r})"; }
                return null;
            }
            catch (DllNotFoundException) { return "no encontré el driver de NVIDIA"; }
            catch (EntryPointNotFoundException) { return "este driver de NVIDIA no deja crear resoluciones; actualízalo"; }
            catch (Exception e) { return e.Message; }
        }
    }

    // ================= AMD (ADL) =================
    static class Amd
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr AllocFn(int size);
        static readonly AllocFn Alloc = size => Marshal.AllocCoTaskMem(size);   // vive mientras la app: ADL guarda el puntero

        [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Create(AllocFn alloc, int connectedOnly, out IntPtr ctx);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Destroy(IntPtr ctx);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Adapter_Primary_Get(IntPtr ctx, out int adapter);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Display_DisplayInfo_Get(IntPtr ctx, int adapter, out int count, out IntPtr info, int forceDetect);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Display_CustomizedMode_Add(IntPtr ctx, int adapter, int display, CustomMode mode);

        [StructLayout(LayoutKind.Sequential)]
        struct CustomMode { public int Flags, ModeWidth, ModeHeight, BaseModeWidth, BaseModeHeight, RefreshRate; }

        const int InfoSize = 16 + 4 + 256 + 256 + 20;   // ADLDisplayInfo
        const int Connected = 0x1, Mapped = 0x2;

        public static string? Create(int w, int h, int hz, int baseW, int baseH)
        {
            var ctx = IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(Alloc, 1, out ctx) != 0) return "no pude hablar con el driver de AMD";
                if (ADL2_Adapter_Primary_Get(ctx, out var adapter) != 0) return "no encontré la tarjeta AMD principal";
                if (ADL2_Display_DisplayInfo_Get(ctx, adapter, out var n, out var info, 0) != 0 || info == IntPtr.Zero) return "no encontré la pantalla en el driver de AMD";
                var display = -1;
                for (var i = 0; i < n && display < 0; i++)
                {
                    var p = info + i * InfoSize;
                    var logical = Marshal.ReadInt32(p);
                    var value = Marshal.ReadInt32(p + InfoSize - 4);
                    if ((value & (Connected | Mapped)) == (Connected | Mapped)) display = logical;
                }
                Marshal.FreeCoTaskMem(info);
                if (display < 0) return "no encontré la pantalla en el driver de AMD";
                var mode = new CustomMode { Flags = 0, ModeWidth = w, ModeHeight = h, BaseModeWidth = baseW, BaseModeHeight = baseH, RefreshRate = hz };
                var r = ADL2_Display_CustomizedMode_Add(ctx, adapter, display, mode);
                return r == 0 ? null : $"el driver de AMD no aceptó {w} × {h} a {hz} Hz (código {r})";
            }
            catch (DllNotFoundException) { return "no encontré el driver de AMD"; }
            catch (EntryPointNotFoundException) { return "este driver de AMD no deja crear resoluciones; actualízalo"; }
            catch (Exception e) { return e.Message; }
            finally { if (ctx != IntPtr.Zero) try { ADL2_Main_Control_Destroy(ctx); } catch { } }
        }
    }
}
