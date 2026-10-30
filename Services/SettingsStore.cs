using System.IO;
using System.Text.Json;
using OculusGoAirMouse.Models;
namespace OculusGoAirMouse.Services;

public static class SettingsStore
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OculusGoAirMouse", "settings.json");
    public static Settings Load(string? filePath = null)
    {
        var requested = filePath ?? FilePath;
        var path = File.Exists(requested) ? requested : Path.Combine(AppContext.BaseDirectory, "settings.default.json");
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("配置为空。") : new Settings();
        settings.Validate();
        return settings;
    }
    public static void Save(Settings settings, string? filePath = null)
    {
        settings.Validate();
        var path = filePath ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
