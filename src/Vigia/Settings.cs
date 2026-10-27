using System.Text.Json;

namespace Vigia;

/// <summary>Preferencias de Vigía en %LOCALAPPDATA%\Vigia\settings.json.</summary>
public sealed class Settings
{
    public bool MemAuto { get; set; } = true;
    public int MemAutoThreshold { get; set; } = 60;
    public int MemAutoIntervalMin { get; set; } = 5;
    public string ToolsRoot { get; set; } = @"D:\pack externo";

    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia");
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
