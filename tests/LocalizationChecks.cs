using System.Globalization;
using System.Text.Json;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;

static class LocalizationChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        foreach (string culture in new[] { "zh-CN", "zh-TW", "zh-HK", "zh-Hans" })
            Check(Localization.Resolve("system", CultureInfo.GetCultureInfo(culture)) == "zh", "Chinese system language did not resolve to Chinese");
        foreach (string culture in new[] { "en-US", "en-GB", "fr-FR", "ko-KR", "de-DE" })
            Check(Localization.Resolve("system", CultureInfo.GetCultureInfo(culture)) == "en", "Unsupported/English system language did not resolve to English");
        Check(Localization.Resolve("system", CultureInfo.InvariantCulture) == "en", "Invariant UI language did not fall back to English");
        Check(Localization.Resolve("en", CultureInfo.GetCultureInfo("zh-CN")) == "en" && Localization.Resolve("zh", CultureInfo.GetCultureInfo("en-US")) == "zh", "Explicit language did not override system");
        Check(Localization.Resolve("system", CultureInfo.GetCultureInfo("ja-JP")) == "ja", "Japanese system language did not resolve to Japanese");
        Check(Localization.Resolve("ja", CultureInfo.GetCultureInfo("zh-CN")) == "ja", "Explicit Japanese did not override system");
        var text = Localization.Current;
        try
        {
            text.SetPreference("en");
            Check(text["UI.Connect"] == "Search and connect", "English UI resource missing");
            string pairing = text.Translate(new PairingRequiredException("OMVR-V190").Message);
            Check(pairing.Contains("not paired") && pairing.Contains("OMVR-V190") && !HasChinese(pairing), "Pairing error not fully translated");
            string progress = text.Translate("静止校准：50% / 1.25 秒（完成后继续控制）");
            Check(progress.Contains("50%") && progress.Contains("1.25") && !HasChinese(progress), "Calibration progress not translated or lost values");
            string log = text.Translate("12:34:56  连接尝试 2/3\n12:34:58  连接失败：读取特征失败：AccessDenied；2 秒后重试（3/3）。");
            Check(log.Contains("2/3") && log.Contains("AccessDenied") && log.Contains("12:34:58") && !HasChinese(log), "Dynamic/historical connection log not translated");
            Check(text.Translate(@"配置文件：C:\UserData\设置\settings.json").EndsWith(@"C:\UserData\设置\settings.json"), "Translation changed characters in the configuration path");
            text.SetPreference("zh");
            Check(text["UI.Connect"] == "搜索并连接" && text.Translate("未连接") == "未连接", "Chinese resources or canonical status were changed");
            var assembly = typeof(Localization).Assembly;
            using var enStream = assembly.GetManifestResourceStream("OculusGoAirMouse.Resources.Strings.en.json")!;
            using var zhStream = assembly.GetManifestResourceStream("OculusGoAirMouse.Resources.Strings.zh.json")!;
            var en = JsonSerializer.Deserialize<Dictionary<string,string>>(enStream)!;
            var zh = JsonSerializer.Deserialize<Dictionary<string,string>>(zhStream)!;
            Check(en.Keys.ToHashSet().SetEquals(zh.Keys) && en.Values.All(v => !string.IsNullOrWhiteSpace(v) && !HasChinese(v)), "Resource keys differ or English resources contain untranslated Chinese");
            using var jaStream = assembly.GetManifestResourceStream("OculusGoAirMouse.Resources.Strings.ja.json")!;
            var ja = JsonSerializer.Deserialize<Dictionary<string,string>>(jaStream)!;
            Check(ja.Keys.ToHashSet().SetEquals(en.Keys) && ja.Values.All(v => !string.IsNullOrWhiteSpace(v)), "Japanese resources are incomplete");
            text.SetPreference("ja");
            Check(text["UI.Connect"] == "検索して接続" && text["UI.SettingsTab"] == "設定", "Japanese UI resources missing");
            string japanesePairing = text.Translate(new PairingRequiredException("OMVR-V190").Message);
            Check(japanesePairing.Contains("ペアリングされていません") && japanesePairing.Contains("OMVR-V190"), "Japanese pairing message lost translation or device name");
            string japaneseProgress = text.Translate("静止校准：50% / 1.25 秒（完成后继续控制）");
            Check(japaneseProgress.Contains("動かさずに調整中") && japaneseProgress.Contains("50%") && japaneseProgress.Contains("1.25"), "Japanese calibration progress lost values");
            Check(text.Translate("12:34:56  连接失败：读取特征失败：AccessDenied；2 秒后重试（3/3）。").Contains("再試行") && text.Translate("未连接") == "未接続", "Japanese dynamic status/log translation failed");
            Check(text.Translate(@"配置文件：C:\UserData\设置\settings.json").EndsWith(@"C:\UserData\设置\settings.json"), "Japanese translation changed configuration path");
            var store = new Store(); var runtime = new Runtime();
            var vm = new MainViewModel(runtime, store, new Scheduler()); await vm.InitializeAsync();
            Check(vm.SelectedLanguage!.Code == "system" && vm.Languages.Count == 4, "Initial language menu selection incorrect");
            vm.Settings.Fields[1].TextValue = "2.75";
            vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "en");
            Check(store.Value.Language == "en" && text.Language == "en" && store.Value.SensitivityX == 1.75, "Language save unexpectedly applied settings draft");
            Check(vm.Settings.Fields[1].TextValue == "2.75" && runtime.Applies == 1 && runtime.Toggles == 0, "Language switch reset draft, recalibrated, or toggled mouse");
            vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "zh");
            Check(text.Language == "zh" && store.Value.Language == "zh", "Chinese switch did not persist");
            var reopened = new MainViewModel(new Runtime(), store, new Scheduler());
            Check(reopened.SelectedLanguage!.Code == "zh", "Saved language did not survive reopening"); await reopened.ShutdownAsync();
            bool systemLabelChanged = false;
            vm.Languages.Single(l => l.Code == "system").PropertyChanged += (_,e) => systemLabelChanged |= e.PropertyName == "DisplayName";
            vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "ja");
            Check(store.Value.Language == "ja" && text.Language == "ja" && vm.Settings.Fields[1].TextValue == "2.75" && runtime.Applies == 1, "Japanese selection did not persist or changed draft/runtime");
            Check(systemLabelChanged && vm.Languages.Single(l => l.Code == "system").DisplayName == "システムに合わせる", "System option did not update to Japanese");
            var japaneseReopened = new MainViewModel(new Runtime(), store, new Scheduler());
            Check(japaneseReopened.SelectedLanguage!.Code == "ja", "Japanese preference did not survive reopening"); await japaneseReopened.ShutdownAsync();
            vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "system");
            Check(store.Value.Language == "system" && text.Language == Localization.Resolve("system", CultureInfo.CurrentUICulture), "System language option did not restore automatic selection");
            await vm.Settings.SaveCommand.ExecuteAsync(null);
            Check(store.Value.Language == "system" && store.Value.SensitivityX == 2.75, "Settings save lost language preference or draft");
            store.Fail = true; vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "en");
            Check(text.Language == "en" && vm.LogText.Contains("语言设置保存失败"), "Language write failure broke switch or was not reported");
            await vm.ShutdownAsync();
            string path = Path.Combine(AppContext.BaseDirectory,"scratch","language-settings.json");
            SettingsStore.Save(new Settings { Language = "zh" },path);
            Check(SettingsStore.Load(path).Language == "zh", "Language preference did not roundtrip");
            SettingsStore.Save(new Settings { Language = "ja" },path); Check(SettingsStore.Load(path).Language == "ja", "Japanese preference did not roundtrip");
            File.WriteAllText(path,"{}"); Check(SettingsStore.Load(path).Language == "system", "Existing settings without language did not follow system");
            try { new Settings { Language = "invalid" }.Validate(); throw new Exception("Invalid language accepted"); }
            catch (ArgumentException) { count++; }
        }
        finally { text.SetPreference("system"); }
        return count;
    }
    private static bool HasChinese(string text) => text.Any(c => c is >= '\u4e00' and <= '\u9fff');
    private sealed class Store : ISettingsStore
    {
        public Settings Value = new() { AutoConnectOnStartup = false }; public bool Fail;
        public bool Exists => true; public string FilePath => "language-test.json";
        public Settings Load() => Value.Copy();
        public void Save(Settings value) { if (Fail) throw new IOException("simulated write failure"); Value = value.Copy(); }
    }
    private sealed class Scheduler : IUiScheduler
    {
        public void Post(Action action) => action();
        public IDisposable Every(TimeSpan interval,Action action) => new TimerRegistration();
        private sealed class TimerRegistration : IDisposable { public void Dispose() {} }
    }
    private sealed class Runtime : IControllerRuntime
    {
        public int Applies,Toggles;
        public EngineSnapshot Snapshot => new(false,false,"未连接"); public bool Connected => false;
        public event Action<string>? Progress { add {} remove {} }
        public event Action<Exception>? Faulted { add {} remove {} }
        public event Action? Disconnected { add {} remove {} }
        public event Action<bool>? LinkChanged { add {} remove {} }
        public Task ApplyAsync(Settings value) { Applies++; return Task.CompletedTask; }
        public Task ConnectAsync(Settings value) => Task.CompletedTask;
        public Task DisconnectAsync(string reason) => Task.CompletedTask;
        public Task ToggleAsync() { Toggles++; return Task.CompletedTask; }
        public Task CalibrateAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
