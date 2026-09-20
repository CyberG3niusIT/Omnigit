using Omnigit.Localization;
using Omnigit.Services;

namespace Omnigit.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void EnglishIsTheFallbackForAnUnsupportedSystemCulture()
    {
        var service = new LocalizationService(new MemorySettingsStore(), "xx-YY");

        Assert.Equal("en-US", service.CurrentCulture.Name);
        Assert.Equal("SETTINGS", service["Settings_Title"]);
    }

    [Fact]
    public void ARegionalSystemCultureResolvesToAnAvailableTranslation()
    {
        var service = new LocalizationService(new MemorySettingsStore(), "de-CH");

        Assert.Equal("de-DE", service.CurrentCulture.Name);
        Assert.Equal("EINSTELLUNGEN", service["Settings_Title"]);
    }

    [Fact]
    public void StoredPreferenceWinsOverTheSystemCulture()
    {
        var store = new MemorySettingsStore(new AppSettings { UiCulture = "de-DE" });
        var service = new LocalizationService(store, "en-GB");

        Assert.Equal("de-DE", service.CurrentCulture.Name);
        Assert.Equal("EINSTELLUNGEN", service["Settings_Title"]);
    }

    [Fact]
    public void SwitchingLanguagePersistsAndRefreshesIndexerBindings()
    {
        var store = new MemorySettingsStore();
        var service = new LocalizationService(store, "en-US");
        var changed = new List<string?>();

        service.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        service.SetCulture("de-DE");

        Assert.Equal("de-DE", store.Settings.UiCulture);
        Assert.Equal("EINSTELLUNGEN", service["Settings_Title"]);
        Assert.Contains("Item[]", changed);
    }

    [Fact]
    public void NullSelectionReturnsToTheSystemLanguage()
    {
        var store = new MemorySettingsStore(new AppSettings { UiCulture = "de-DE" });
        var service = new LocalizationService(store, "en-US");

        service.SetCulture(null);

        Assert.Null(store.Settings.UiCulture);
    }

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        public MemorySettingsStore(AppSettings? settings = null)
            => Settings = settings ?? new AppSettings();

        public AppSettings Settings { get; private set; }

        public AppSettings Load() => new() { UiCulture = Settings.UiCulture };

        public void Save(AppSettings settings)
            => Settings = new AppSettings { UiCulture = settings.UiCulture };
    }
}
