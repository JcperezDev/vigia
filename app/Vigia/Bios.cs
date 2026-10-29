using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Vigia;

/// <summary>
/// BIOS: lo que se ajusta ahí, leído desde Windows. Vigía nunca escribe en la BIOS: dice qué está apagado,
/// dónde se cambia en la placa de cada marca y, si el usuario quiere, reinicia directo a la BIOS.
/// </summary>
public static class Bios
{
    public record Firmware(string Board, string Vendor, string Version, string Maker, string? Date, int? AgeMonths, bool Uefi, string? SupportUrl);
    public record View(Firmware Fw, List<Rig.Check> Checks);

    [DllImport("kernel32.dll")] static extern bool GetFirmwareType(out uint type);

    public static bool Uefi() => GetFirmwareType(out var t) && t == 2;

    public static View Get()
    {
        string maker = "", product = "", ver = "", biosMaker = ""; DateTime? date = null;
        try { using var s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"); foreach (var o in s.Get()) { maker = ((o["Manufacturer"] as string) ?? "").Trim(); product = ((o["Product"] as string) ?? "").Trim(); } } catch { }
        try
        {
            using var s = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, Manufacturer, ReleaseDate FROM Win32_BIOS");
            foreach (var o in s.Get())
            {
                ver = ((o["SMBIOSBIOSVersion"] as string) ?? "").Trim(); biosMaker = ((o["Manufacturer"] as string) ?? "").Trim();
                if (o["ReleaseDate"] is string rd && rd.Length >= 8 && DateTime.TryParseExact(rd[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;
            }
        }
        catch { }
        var vendor = Rig.Vendor(maker);
        int? age = date is { } dd ? (DateTime.Today.Year - dd.Year) * 12 + DateTime.Today.Month - dd.Month : null;
        var uefi = Uefi();
        var fw = new Firmware(product, maker, ver, biosMaker, date?.ToString("yyyy-MM-dd"), age, uefi, SupportUrl(vendor, product));

        var amd = Rig.Cpu().Contains("AMD", StringComparison.OrdinalIgnoreCase);
        var list = new List<Rig.Check>
        {
            Rig.Ram(maker),
            Rig.ReBar(maker),
            Mode(uefi),
            SecureBoot(vendor, uefi),
            Tpm(vendor, amd),
            Virt(vendor, amd),
        };
        if (age is { } a)
            list.Add(a >= 12
                ? new("bios-age", "warn", "Versión de la BIOS", $"Tienes la {ver}, de hace {Months(a)}. Las versiones nuevas suelen traer arreglos de estabilidad para la RAM y el procesador.",
                      $"Actualizarla lo haces tú: descárgala solo de la web de {Brand(vendor, maker)}, cópiala a un USB en FAT32 y usa {Flash(vendor)} desde la BIOS. No apagues el PC mientras se instala.", null, null)
                : new("bios-age", "ok", "Versión de la BIOS", $"Tienes la {ver}, de hace {Months(a)}. Está reciente.", null, null, null));
        return new(fw, list);
    }

    static string Months(int m) => m < 1 ? "menos de un mes" : m == 1 ? "1 mes" : m < 24 ? $"{m} meses" : $"{m / 12} años";

    static Rig.Check Mode(bool uefi) => uefi
        ? new("uefi", "ok", "Modo de arranque", "UEFI, sin CSM. Es lo que piden Resizable BAR, Secure Boot y Windows 11.", null, null, null)
        : new("uefi", "warn", "Modo de arranque", "Legacy (CSM). Así no funcionan Resizable BAR ni Secure Boot.",
              "Pasar a UEFI necesita convertir el disco a GPT antes (mbr2gpt de Windows) o el PC no arranca. Si no lo has hecho nunca, pide ayuda.", null, null);

    static Rig.Check SecureBoot(string vendor, bool uefi)
    {
        int on = 0;
        try { on = Convert.ToInt32(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", 0) ?? 0); } catch { }
        if (on == 1) return new("secureboot", "ok", "Secure Boot", "Activado. Lo piden los antitrampas de Valorant, Battlefield 6 y Call of Duty.", null, null, null);
        var where = vendor switch
        {
            "msi" => "Supr al encender, F7, Settings → Security → Secure Boot → «Enabled».",
            "asus" => "Supr al encender, F7, Boot → Secure Boot → OS Type: «Windows UEFI mode».",
            "gigabyte" => "Supr al encender, Boot → Secure Boot → «Enabled».",
            "asrock" => "F2 al encender, Security → Secure Boot → «Enabled».",
            _ => "búscalo en la pestaña Boot o Security de la BIOS.",
        };
        return new("secureboot", "warn", "Secure Boot", "Desactivado. Valorant, Battlefield 6 y Call of Duty no arrancan sin él.",
                   uefi ? $"Se activa en la BIOS: {where} Guarda con F10." : "Primero hace falta el arranque UEFI.", null, null);
    }

    static Rig.Check Tpm(string vendor, bool amd)
    {
        bool? on = null; string spec = "";
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\CIMV2\Security\MicrosoftTpm"), new ObjectQuery("SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm"));
            on = false;
            foreach (var o in s.Get())
            {
                on = (o["IsEnabled_InitialValue"] as bool? ?? false) && (o["IsActivated_InitialValue"] as bool? ?? false);
                spec = ((o["SpecVersion"] as string) ?? "").Split(',')[0].Trim();
            }
        }
        catch { }
        var name = amd ? "fTPM" : "PTT";
        if (on == true) return new("tpm", "ok", "TPM", spec.Length > 0 ? $"Activado (versión {spec}). Lo piden Windows 11 y Vanguard de Valorant." : "Activado. Lo piden Windows 11 y Vanguard de Valorant.", null, null, null);
        if (on is null) return new("tpm", "info", "TPM", "No se pudo leer.", null, null, null);
        var where = (vendor, amd) switch
        {
            ("msi", true) => "Supr al encender, F7, Settings → Security → Trusted Computing → «AMD fTPM switch»: AMD CPU fTPM.",
            ("msi", false) => "Supr al encender, F7, Settings → Security → Trusted Computing → «Security Device Support»: Enabled, y PTT.",
            ("asus", _) => $"Supr al encender, F7, Advanced → {(amd ? "AMD fTPM configuration" : "PCH-FW Configuration")} → Firmware TPM.",
            ("gigabyte", _) => $"Supr al encender, Settings → Miscellaneous → {(amd ? "AMD CPU fTPM" : "Intel Platform Trust Technology (PTT)")}: Enabled.",
            ("asrock", _) => $"F2 al encender, Security → {(amd ? "AMD fTPM switch" : "Intel PTT")}: Enabled.",
            _ => $"busca «{name}» o «TPM» en la pestaña Security de la BIOS.",
        };
        return new("tpm", "warn", "TPM", $"Apagado. Sin él no hay Windows 11 oficial ni Valorant.", $"Se activa en la BIOS ({name}): {where} Guarda con F10.", null, null);
    }

    static Rig.Check Virt(string vendor, bool amd)
    {
        bool fw = false, hv = false;
        try { using var s = new ManagementObjectSearcher("SELECT VirtualizationFirmwareEnabled FROM Win32_Processor"); foreach (var o in s.Get()) fw = o["VirtualizationFirmwareEnabled"] as bool? ?? false; } catch { }
        try { using var s = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem"); foreach (var o in s.Get()) hv = o["HypervisorPresent"] as bool? ?? false; } catch { }
        var name = amd ? "SVM Mode" : "Intel Virtualization Technology";
        if (fw || hv) return new("virt", "info", "Virtualización", hv
            ? "Activada y en uso: Hyper-V está encendido. Para jugar cuesta algunos FPS; si no usas máquinas virtuales, WSL ni el emulador de Android, puedes apagar Hyper-V."
            : "Activada en la BIOS. Sirve para máquinas virtuales, WSL y emuladores de Android; no resta FPS mientras no la uses.", null, null, null);
        var where = vendor switch
        {
            "msi" => $"Supr al encender, F7, OC → {(amd ? "Advanced CPU Configuration → SVM Mode" : "CPU Features → Intel Virtualization Tech")}: Enabled.",
            "asus" => $"Supr al encender, F7, Advanced → CPU Configuration → {name}: Enabled.",
            "gigabyte" => $"Supr al encender, Tweaker → Advanced CPU Settings → {name}: Enabled.",
            "asrock" => $"F2 al encender, Advanced → CPU Configuration → {name}: Enabled.",
            _ => $"busca «{name}» en la BIOS.",
        };
        return new("virt", "info", "Virtualización", "Apagada. Solo hace falta para máquinas virtuales, WSL o emuladores de Android; para jugar no cambia nada.", $"Si la necesitas: {where} Guarda con F10.", null, null);
    }

    static string Brand(string vendor, string maker) => vendor switch { "msi" => "MSI", "asus" => "ASUS", "gigabyte" => "Gigabyte", "asrock" => "ASRock", _ => maker.Length > 0 ? maker : "tu placa" };
    static string Flash(string vendor) => vendor switch { "msi" => "M-FLASH", "asus" => "EZ Flash", "gigabyte" => "Q-Flash", "asrock" => "Instant Flash", _ => "la opción de actualizar" };

    /// <summary>Página de soporte de la placa en la web del fabricante. Solo dominios de la lista de Support.</summary>
    static string? SupportUrl(string vendor, string product)
    {
        var model = product.Split('(')[0].Trim();
        if (model.Length == 0) return null;
        var q = Uri.EscapeDataString(model);
        return vendor switch
        {
            "msi" => $"https://www.msi.com/Motherboard/{Uri.EscapeDataString(string.Join('-', model.Split(' ', StringSplitOptions.RemoveEmptyEntries)))}/support",
            "asus" => $"https://www.asus.com/searchresult?searchType=support&searchKey={q}",
            "gigabyte" => $"https://www.gigabyte.com/Search?kw={q}",
            "asrock" => $"https://www.asrock.com/search/index.asp?Keyword={q}",
            _ => null,
        };
    }

    /// <summary>Reinicia y entra directo a la BIOS (solo UEFI). Lo pide el usuario con un botón de dos pasos.</summary>
    public static RestorePoint.Result RebootToFirmware()
    {
        if (!Uefi()) return new(false, "Tu PC arranca en modo Legacy: entra pulsando Supr o F2 justo al encender.", false);
        var (exit, output) = Tweaks.Run("shutdown.exe", "/r /fw /t 5");
        return exit == 0 ? new(true, "Reiniciando en 5 segundos…", false) : new(false, output.Trim(), false);
    }
}
