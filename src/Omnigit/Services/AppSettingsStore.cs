using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Omnigit.Services;

/// <summary>
/// Application-wide preferences that are independent of repositories, accounts and
/// credentials. A missing value means the application should use its platform default.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// BCP 47 culture name for the UI, such as "en-US" or "de-DE".
    /// Null means follow the operating system's UI culture.
    /// </summary>
    public string? UiCulture { get; set; }
}

/// <summary>Loads and saves application-wide user preferences.</summary>
public interface IAppSettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>
/// Persists application-wide preferences as JSON under the platform's per-user
/// application data directory.
/// </summary>
public sealed class AppSettingsStore : IAppSettingsStore
{
    private readonly string _file;

    public AppSettingsStore() : this(AppPaths.In("settings.json")) { }

    /// <summary>
    /// Writes somewhere other than the user's own config directory. Internal because
    /// production always uses <see cref="AppPaths"/>, while tests need an isolated file.
    /// </summary>
    internal AppSettingsStore(string file) => _file = file;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_file))
                return new AppSettings();

            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preferences must never prevent the application from starting.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(_file, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is an annoyance, not a failure worth surfacing.
        }
    }
}

// Source-generated so the store keeps working if trimming is ever enabled.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
