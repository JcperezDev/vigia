using System.Text.Json;

namespace Vigia;

/// <summary>Preferencias de Vigía en %LOCALAPPDATA%\Vigia\settings.json.</summary>
public sealed class Settings
{
    public bool MemAuto { get; set; } = false;
    /// <summary>¿Instalar el driver PawnIO para los grados del procesador? null = todavía no se preguntó.</summary>
    public bool? PawnConsent { get; set; }   // apagada si no lo pides: liberar RAM no da rendimiento por sí solo
    public int MemAutoThreshold { get; set; } = 60;
    public int MemAutoIntervalMin { get; set; } = 5;
    public string ToolsRoot { get; set; } = "";
    /// <summary>Mirar al abrir y cada 6 h si hay versión nueva (solo pregunta a GitHub; no descarga nada).</summary>
    public bool UpdateCheck { get; set; } = true;
    /// <summary>Idioma de la interfaz: auto (el de Windows), es, en, pt, fr, de. Puente también lo lee.</summary>
    public string Lang { get; set; } = "auto";
    /// <summary>Cerrar la ventana la deja en la bandeja (en reposo) en vez de salir.</summary>
    public bool TrayOnClose { get; set; } = true;
    /// <summary>El aviso «Vigía sigue aquí» solo sale la primera vez.</summary>
    public bool TrayHintShown { get; set; }

    // VIGIA_DATA: carpeta de datos aparte, para pruebas sin tocar la del usuario
    public static string Dir { get; } = Environment.GetEnvironmentVariable("VIGIA_DATA") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Program.Json) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions(Program.Json) { WriteIndented = true }));
    }
}
