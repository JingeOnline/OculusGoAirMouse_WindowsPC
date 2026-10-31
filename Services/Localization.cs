using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OculusGoAirMouse.Services;

public sealed class LanguageOption(string code, string nativeName) : ObservableObject
{
    public string Code { get; } = code;
    public string DisplayName => Code == "system" ? Localization.Current["UI.SystemOption"] : nativeName;
    public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
}

public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Current { get; } = new();
    private readonly Dictionary<string, string> english = Read("en"), chinese = Read("zh"), japanese = Read("ja");
    private readonly Dictionary<string, KeyValuePair<string, string>[]> replacements;
    private string language = Resolve("system", CultureInfo.CurrentUICulture);
    private Localization() => replacements = new() { ["en"] = BuildReplacements(english), ["ja"] = BuildReplacements(japanese) };
    private static KeyValuePair<string, string>[] BuildReplacements(Dictionary<string, string> catalog) =>
        catalog.Where(p => !p.Key.StartsWith("UI.", StringComparison.Ordinal)).OrderByDescending(p => p.Key.Length).ToArray();
    private Dictionary<string, string> Catalog => Language switch { "zh" => chinese, "ja" => japanese, _ => english };
    public string Language => Volatile.Read(ref language);
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Catalog.TryGetValue(key, out var value) ? value : key;
    public static string Resolve(string preference, CultureInfo systemCulture) => preference switch
    {
        "zh" => "zh",
        "en" => "en",
        "ja" => "ja",
        "system" when systemCulture.TwoLetterISOLanguageName == "zh" => "zh",
        "system" when systemCulture.TwoLetterISOLanguageName == "ja" => "ja",
        _ => "en"
    };
    public void SetPreference(string preference)
    {
        Volatile.Write(ref language, Resolve(preference, CultureInfo.CurrentUICulture));
        PropertyChanged?.Invoke(this, new(nameof(Language)));
        PropertyChanged?.Invoke(this, new("Item[]"));
    }
    public string Translate(string source)
    {
        if (Language == "zh" || string.IsNullOrEmpty(source)) return source;
        const string pathPrefix = "配置文件：";
        if (source.StartsWith(pathPrefix, StringComparison.Ordinal)) return this[pathPrefix] + source[pathPrefix.Length..];
        if (Catalog.TryGetValue(source, out var exact)) return exact;
        // Messages are kept in their canonical form. Translate only at the view
        // boundary so switching languages also refreshes existing status/log text.
        foreach (var pair in replacements[Language]) source = source.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return source;
    }
    private static Dictionary<string, string> Read(string language)
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream($"OculusGoAirMouse.Resources.Strings.{language}.json")
            ?? throw new InvalidOperationException("Missing language resources: " + language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Invalid language resources.");
    }
}
