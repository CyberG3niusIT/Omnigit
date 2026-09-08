using Omnigit.Services;

namespace Omnigit.Tests;

/// <summary>
/// The repository list on disk, and the one thing it remembers besides the list: which
/// repository was open, so reopening the app puts you back where you left off.
/// </summary>
public class RepositoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "omnigit-tests", Guid.NewGuid().ToString("n"));

    public RepositoryStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string File_ => Path.Combine(_dir, "repositories.json");

    private RepositoryStore Store() => new(File_);

    /// <summary>A directory that exists, since the store drops paths that do not.</summary>
    private string Repo(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ARoundTripKeepsBothTheListAndWhichWasOpen()
    {
        var a = Repo("alpha");
        var b = Repo("beta");

        Store().Save([a, b], b);
        var back = Store().Load();

        Assert.Equal([a, b], back.Paths);
        Assert.Equal(b, back.LastOpened);
    }

    [Fact]
    public void NothingWasOpenIsAFairAnswer()
    {
        var a = Repo("alpha");

        // What removing the open repository leaves behind, and what the very first run
        // writes. It has to mean "open the first one", not "fail".
        Store().Save([a], null);

        Assert.Null(Store().Load().LastOpened);
        Assert.Single(Store().Load().Paths);
    }

    [Fact]
    public void ARepositoryDeletedFromOutsideIsNotReopened()
    {
        var a = Repo("alpha");
        var gone = Repo("gone");

        Store().Save([a, gone], gone);
        Directory.Delete(gone);

        var back = Store().Load();

        // Dropped from the list, so it must be dropped as the one to reopen too - the
        // app would otherwise start by failing to open something the user cannot see.
        Assert.Equal([a], back.Paths);
        Assert.Null(back.LastOpened);
    }

    [Fact]
    public void AFileFromAVersionThatNeverKnewAboutThisHasNoPreference()
    {
        var a = Repo("alpha");
        System.IO.File.WriteAllText(File_, $$"""{ "Repositories": [{{System.Text.Json.JsonSerializer.Serialize(a)}}] }""");

        var back = Store().Load();

        Assert.Equal([a], back.Paths);
        Assert.Null(back.LastOpened);
    }

    [Fact]
    public void ACorruptFileStartsEmptyRatherThanStoppingTheApp()
    {
        System.IO.File.WriteAllText(File_, "{ not json");

        Assert.Empty(Store().Load().Paths);
        Assert.Null(Store().Load().LastOpened);
    }

    [Fact]
    public void NoFileAtAllIsTheFirstRun()
    {
        Assert.Empty(Store().Load().Paths);
    }
}
