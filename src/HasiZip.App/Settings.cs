using System;
using System.IO;
using System.Text.Json;
using HasiZip.Core;

namespace HasiZip.App;

/// <summary>Einstellungen in %APPDATA%\HasiZip\settings.json.</summary>
public sealed class Settings
{
    public static Settings Current { get; } = Load();

    public PackLevel Level { get; set; } = PackLevel.Normal;
    /// <summary>Ergebnis danach im Explorer markieren.</summary>
    public bool RevealWhenDone { get; set; } = true;
    /// <summary>Fenster nach Kontextmenü-Aktionen automatisch schließen, wenn alles geklappt hat.</summary>
    public bool CloseWhenDone { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HasiZip", "settings.json");

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* kaputte Datei → Standardwerte */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht kritisch */ }
    }
}
