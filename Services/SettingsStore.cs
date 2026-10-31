using System.IO;
using System.Text.Json;
using OculusGoAirMouse.Models;
namespace OculusGoAirMouse.Services;

public static class SettingsStore
{
    public static string FilePath => Path.Combine(UserDataPaths.DirectoryPath, "settings.json");
    private static string LegacyFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OculusGoAirMouse", "settings.json");
    public static Settings Load(string? filePath = null) => LoadCore(
        filePath ?? FilePath,
        Path.Combine(AppContext.BaseDirectory, "settings.default.json"),
        filePath == null ? LegacyFilePath : null);

    internal static Settings LoadCore(string requested, string defaults, string? legacy)
    {
        if (File.Exists(requested)) return Read(requested);
        if (legacy != null && File.Exists(legacy))
        {
            var migrated = Read(legacy);
            Save(migrated, requested);
            return migrated;
        }
        return File.Exists(defaults) ? Read(defaults) : new Settings();
    }
    private static Settings Read(string path)
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("配置为空。");
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
