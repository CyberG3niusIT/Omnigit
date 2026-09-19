using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Omnigit.Services;

namespace Omnigit.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }

    /// <summary>Null means follow the operating system's UI language.</summary>
    string? SelectedCulture { get; }

    bool IsRightToLeft { get; }

    string this[string key] { get; }

    void SetCulture(string? cultureName);
}

/// <summary>
/// Resolves localized strings and owns the application's UI-language preference.
/// Views bind through the indexer so changing the culture can refresh them without
/// recreating the window.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources =
        new("Omnigit.Localization.Strings", typeof(LocalizationService).Assembly);

    private readonly IAppSettingsStore? _settingsStore;
    private readonly AppSettings _settings;
    private CultureInfo _currentCulture;

    /// <param name="settingsStore">
    /// Null is useful at design time and in isolated tests where no preference should
    /// touch the user's real configuration directory.
    /// </param>
    /// <param name="systemCultureName">
    /// Test seam for the operating-system language. Production leaves this null.
    /// </param>
    public LocalizationService(
        IAppSettingsStore? settingsStore = null,
        string? systemCultureName = null)
    {
        _settingsStore = settingsStore;
        _settings = settingsStore?.Load() ?? new AppSettings();

        var requested = _settings.UiCulture
            ?? systemCultureName
            ?? CultureInfo.CurrentUICulture.Name;

        _currentCulture = CultureInfo.GetCultureInfo(ResolveAvailableCulture(requested));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public string? SelectedCulture => _settings.UiCulture;

    public bool IsRightToLeft => _currentCulture.TextInfo.IsRightToLeft;

    public string this[string key]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return Resources.GetString(key, _currentCulture) ?? key;
        }
    }

    public void SetCulture(string? cultureName)
    {
        string resolved;

        if (cultureName is null)
        {
            resolved = ResolveAvailableCulture(CultureInfo.CurrentUICulture.Name);
        }
        else
        {
            resolved = FindExactAvailableCulture(cultureName)
                ?? throw new ArgumentException(
                    $"Culture '{cultureName}' has no complete Omnigit translation.",
                    nameof(cultureName));
        }

        var selected = cultureName is null ? null : resolved;
        if (string.Equals(_settings.UiCulture, selected, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_currentCulture.Name, resolved, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.UiCulture = selected;
        _settingsStore?.Save(_settings);
        _currentCulture = CultureInfo.GetCultureInfo(resolved);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightToLeft)));

        // Avalonia indexer bindings observe Item[] just like ordinary .NET binding
        // engines do, so every localized string is re-read after a language switch.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    internal static string ResolveAvailableCulture(string? requested)
    {
        var exact = FindExactAvailableCulture(requested);
        if (exact is not null)
            return exact;

        CultureInfo culture;
        try
        {
            culture = string.IsNullOrWhiteSpace(requested)
                ? CultureInfo.GetCultureInfo("en-US")
                : CultureInfo.GetCultureInfo(requested);
        }
        catch (CultureNotFoundException)
        {
            return "en-US";
        }

        // Traditional Chinese should not silently resolve to Simplified Chinese when
        // both translations are available.
        if (string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
        {
            var traditional = culture.Name.Contains("-TW", StringComparison.OrdinalIgnoreCase)
                || culture.Name.Contains("-HK", StringComparison.OrdinalIgnoreCase)
                || culture.Name.Contains("-MO", StringComparison.OrdinalIgnoreCase)
                || culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase);

            var preferred = traditional ? "zh-TW" : "zh-CN";
            var chinese = FindExactAvailableCulture(preferred);
            if (chinese is not null)
                return chinese;
        }

        foreach (var candidate in SupportedCultures.Available)
        {
            var available = CultureInfo.GetCultureInfo(candidate);
            if (string.Equals(
                    available.TwoLetterISOLanguageName,
                    culture.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return available.Name;
            }
        }

        return "en-US";
    }

    private static string? FindExactAvailableCulture(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        foreach (var candidate in SupportedCultures.Available)
        {
            if (string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
