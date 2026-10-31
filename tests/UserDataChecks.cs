using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class UserDataChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        string root = Path.Combine(AppContext.BaseDirectory, "scratch", "paths-" + Guid.NewGuid().ToString("N"));
        string alice = UserDataPaths.GetDirectory(root, "alice");
        string bob = UserDataPaths.GetDirectory(root, "bob");
        Check(alice == Path.Combine(Path.GetFullPath(root), "UserData", "alice") && alice != bob, "User data folders not under application directory or not separated by user");
        Check(UserDataPaths.DirectoryPath == UserDataPaths.GetDirectory(AppContext.BaseDirectory, Environment.UserName), "Actual user directory is inconsistent");
        Check(SettingsStore.FilePath == Path.Combine(UserDataPaths.DirectoryPath, "settings.json"), "Settings path did not move with user directory");
        foreach (string invalid in new[] { "..", "", "../outside", "a\\b", "C:\\other" })
        {
            string path = UserDataPaths.GetDirectory(root, invalid);
            Check(Path.GetDirectoryName(path) == Path.Combine(Path.GetFullPath(root), "UserData"), "Username escaped UserData directory");
        }
        string defaults = Path.Combine(root, "settings.default.json");
        string legacy = Path.Combine(root, "legacy", "settings.json");
        string target = Path.Combine(alice, "settings.json");
        SettingsStore.Save(new Settings { SensitivityX = 1.4 }, defaults);
        SettingsStore.Save(new Settings { SensitivityX = 2.4, ClickPressLockMilliseconds = 20 }, legacy);
        string oldText = File.ReadAllText(legacy);
        var migrated = SettingsStore.LoadCore(target, defaults, legacy);
        Check(migrated.SensitivityX == 2.4 && migrated.ClickPressLockMilliseconds == 20 && File.Exists(target), "Old settings were not migrated to new directory");
        Check(File.ReadAllText(legacy) == oldText && !File.Exists(target + ".tmp"), "Migration altered old settings or retained temporary file");
        SettingsStore.Save(new Settings { SensitivityX = 3.4 }, target);
        Check(SettingsStore.LoadCore(target, defaults, legacy).SensitivityX == 3.4, "Existing user settings were overwritten by legacy settings");
        string bobTarget = Path.Combine(bob, "settings.json");
        Check(SettingsStore.LoadCore(bobTarget, defaults, null).SensitivityX == 1.4 && !File.Exists(bobTarget), "No-legacy fallback did not load defaults");
        File.WriteAllText(legacy, "broken");
        try { SettingsStore.LoadCore(bobTarget, defaults, legacy); throw new Exception("Corrupt legacy settings accepted"); }
        catch (System.Text.Json.JsonException) { Check(!File.Exists(bobTarget) && File.ReadAllText(legacy) == "broken", "Corrupt migration wrote new settings or modified old file"); }
        File.WriteAllText(target, "broken new file");
        SettingsStore.Save(new Settings(), legacy);
        try { SettingsStore.LoadCore(target, defaults, legacy); throw new Exception("Corrupt target replaced by legacy"); }
        catch (System.Text.Json.JsonException) { Check(File.ReadAllText(target) == "broken new file", "Corrupt new settings were silently overwritten"); }
        string log = Path.Combine(bob, "input-diagnostics.log");
        var writer = new DiagnosticWriter(log); writer.Write("user directory log"); await writer.DisposeAsync();
        Check(File.ReadAllText(log).Contains("user directory log"), "Diagnostics did not create and write user directory");
        return count;
    }
}
