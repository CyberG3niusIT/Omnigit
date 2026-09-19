using Omnigit.Services;

namespace Omnigit.Tests;

/// <summary>
/// Application-wide preferences live independently from repository state so adding a
/// preference cannot change the format or migration rules of repositories.json.
/// </summary>
public class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "omnigit-tests", Guid.NewGuid().ToString("n"));

    public AppSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string File_ => Path.Combine(_dir, "settings.json");

    private AppSettingsStore Store() => new(File_);

    [Fact]
    public void ARoundTripKeepsTheSelectedUiCulture()
    {
        Store().Save(new AppSettings { UiCulture = "de-DE" });

        var back = Store().Load();

        Assert.Equal("de-DE", back.UiCulture);
    }

    [Fact]
    public void NoFileMeansFollowTheOperatingSystem()
    {
        Assert.Null(Store().Load().UiCulture);
    }

    [Fact]
    public void AFileFromBeforeLanguagesExistedFollowsTheOperatingSystem()
    {
        System.IO.File.WriteAllText(File_, "{}");

        Assert.Null(Store().Load().UiCulture);
    }

    [Fact]
    public void ACorruptFileFallsBackWithoutStoppingTheApplication()
    {
        System.IO.File.WriteAllText(File_, "{ not json");

        Assert.Null(Store().Load().UiCulture);
    }
}
