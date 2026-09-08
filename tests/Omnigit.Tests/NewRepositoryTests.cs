using LibGit2Sharp;
using Omnigit.HostProviders;
using Omnigit.Services;
using Omnigit.ViewModels;

namespace Omnigit.Tests;

/// <summary>
/// The local half of creating a repository: <c>Init</c>, the starting files, and the
/// state that makes the sync button offer to publish.
/// </summary>
public class NewRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "omnigit-tests", Guid.NewGuid().ToString("n"));

    private readonly GitService _git = new();

    public void Dispose()
    {
        if (!Directory.Exists(_root))
            return;

        // libgit2 marks .git contents read-only on some platforms.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string PathFor(string name) => Path.Combine(_root, name);

    // ---- Init ---------------------------------------------------------------

    [Fact]
    public void InitCreatesTheDirectoryAndPutsHeadOnTheBranchAsked()
    {
        var workdir = _git.Init(PathFor("fresh"), "main");

        Assert.True(Repository.IsValid(workdir));

        using var repo = new Repository(workdir);

        // The branch does not exist yet - nothing does - so this is HEAD pointing at an
        // unborn ref, which is exactly what `git init -b main` leaves behind. Getting it
        // wrong means the first commit lands on libgit2's built-in master instead.
        Assert.Equal("refs/heads/main", repo.Refs.Head.TargetIdentifier);
        Assert.Null(repo.Head.Tip);
    }

    [Fact]
    public void InitRefusesToTakeOverSomethingThatIsAlreadyARepository()
    {
        var workdir = _git.Init(PathFor("twice"), "main");

        Assert.Throws<InvalidOperationException>(() => _git.Init(workdir, "main"));
    }

    [Fact]
    public void TheFirstCommitLandsOnTheChosenBranchRatherThanMaster()
    {
        var workdir = _git.Init(PathFor("first"), "main");
        File.WriteAllText(Path.Combine(workdir, "README.md"), "# first\n");

        using (var repo = new Repository(workdir))
        {
            repo.Config.Set("user.name", "Test");
            repo.Config.Set("user.email", "test@example.com");
        }

        _git.Commit(workdir, ["README.md"], "Initial commit", string.Empty);

        var info = _git.OpenRepository(workdir);

        Assert.Equal("main", new Repository(workdir).Head.FriendlyName);

        // No remote, so nothing to be ahead or behind of - and the toolbar reads this
        // to offer "Publish repository" rather than a fetch that has nowhere to go.
        Assert.False(info.HasRemote);
        Assert.False(info.IsPublished);
    }

    [Fact]
    public void AddingARemoteIsWhatTurnsPublishIntoSync()
    {
        var workdir = _git.Init(PathFor("remote"), "main");

        Assert.False(_git.OpenRepository(workdir).HasRemote);

        _git.AddRemote(workdir, "origin", "https://example.com/tester/thing.git");

        Assert.True(_git.OpenRepository(workdir).HasRemote);
        Assert.Equal("https://example.com/tester/thing.git", _git.GetRemoteUrl(workdir));
    }

    [Fact]
    public void PublishingTwiceOverwritesOriginRatherThanThrowing()
    {
        // The push can fail after the remote is written, and the retry must not die on
        // a name that is already there.
        var workdir = _git.Init(PathFor("retry"), "main");

        _git.AddRemote(workdir, "origin", "https://example.com/a.git");
        _git.AddRemote(workdir, "origin", "https://example.com/b.git");

        Assert.Equal("https://example.com/b.git", _git.GetRemoteUrl(workdir));
    }

    // ---- The starting files -------------------------------------------------

    [Fact]
    public void ALicenceIsWrittenWithItsBlanksFilledIn()
    {
        var text = RepositoryTemplates.Licence("mit", "Ada Lovelace", "omnigit", 2026);

        Assert.NotNull(text);
        Assert.Contains("Copyright (c) 2026 Ada Lovelace", text);

        // A licence still holding [fullname] names nobody and protects nobody.
        Assert.DoesNotContain("[fullname]", text);
        Assert.DoesNotContain("[year]", text);
    }

    [Fact]
    public void TheGnuTextsHaveTheirOwnPlaceholdersFilledToo()
    {
        var text = RepositoryTemplates.Licence("gpl-3.0", "Ada Lovelace", "omnigit", 2026);

        Assert.NotNull(text);
        Assert.DoesNotContain("<name of author>", text);
        Assert.DoesNotContain("<year>", text);
        Assert.DoesNotContain("one line to give the program's name", text);
    }

    [Fact]
    public void NoneMeansNoFileRatherThanAnEmptyOne()
    {
        Assert.Null(RepositoryTemplates.Licence(RepositoryTemplates.None, "Ada", "x", 2026));
        Assert.Null(RepositoryTemplates.Gitignore(RepositoryTemplates.None));
        Assert.Null(RepositoryTemplates.Gitignore(null));
    }

    [Fact]
    public void TheTemplateNamesSurviveBeingEmbedded()
    {
        // MSBuild's default resource naming mangles anything that isn't an identifier,
        // which would have turned C++ into C__ in the picker. LogicalName stops that,
        // and this is what would notice if it were removed.
        Assert.Contains("C++", RepositoryTemplates.GitignoreNames);
        Assert.NotNull(RepositoryTemplates.Gitignore("C++"));

        Assert.Contains("Objective-C", RepositoryTemplates.GitignoreNames);
        Assert.NotNull(RepositoryTemplates.Gitignore("Objective-C"));
    }

    [Fact]
    public void BothListsOfferNoneFirst()
    {
        Assert.Equal(RepositoryTemplates.None, RepositoryTemplates.GitignoreNames[0]);
        Assert.Equal(RepositoryTemplates.None, RepositoryTemplates.LicenceIds[0]);
    }

    [Fact]
    public void ALicenceIsNamedByItsOwnTitleRatherThanBySpdxId()
    {
        // The titles come out of the files' own front matter, so the folder is still the
        // only list.
        Assert.Equal("MIT License", RepositoryTemplates.LicenceTitle("mit"));
        Assert.Equal("Apache License 2.0", RepositoryTemplates.LicenceTitle("apache-2.0"));
    }

    [Fact]
    public void TheTwoGnuLicencesAreToldApartInThePicker()
    {
        // Both texts open with the line "GNU GENERAL PUBLIC LICENSE", so a title read
        // from the licence itself gave two rows saying the same thing.
        var second = RepositoryTemplates.LicenceTitle("gpl-2.0");
        var third = RepositoryTemplates.LicenceTitle("gpl-3.0");

        Assert.NotEqual(second, third);
        Assert.Contains("v2.0", second);
        Assert.Contains("v3.0", third);
    }

    [Fact]
    public void EveryLicenceOfferedHasARealNameRatherThanFallingBackToItsId()
    {
        foreach (var id in RepositoryTemplates.LicenceIds.Skip(1))
            Assert.NotEqual(id, RepositoryTemplates.LicenceTitle(id));
    }

    [Fact]
    public void TheFrontMatterNeverReachesTheFileOnDisk()
    {
        // The title is read out of it; writing it into someone's LICENSE would put a
        // block of Jekyll metadata above the licence text.
        var text = RepositoryTemplates.Licence("mit", "Ada Lovelace", "omnigit", 2026)!;

        Assert.StartsWith("MIT License", text);
        Assert.DoesNotContain("spdx-id:", text);
        Assert.DoesNotContain("permissions:", text);
    }

    // ---- Where it goes, and what the button says --------------------------

    private static HostAccount Signed(string host) => new()
    {
        ProviderId = "gitea",
        BaseUrl = new Uri($"https://{host}"),
        Login = "tester",
        DisplayName = "Tester",
        Token = "t0ken",
    };

    [Fact]
    public void ANewRepositoryGoesNowhereUntilASiteIsPicked()
    {
        var draft = new NewRepositoryViewModel([Signed("git.example.com")]);

        // "Nowhere yet" is the default, so the form still works signed out and creating
        // a repository never reaches the network unless it was asked to.
        Assert.False(draft.Publish.HasSite);
        Assert.Null(draft.Publish.ToRequest("omnigit", ""));
        Assert.Equal("Create repository", draft.CreateButtonLabel);
        Assert.Contains("sync button", draft.SummaryLabel);
    }

    [Fact]
    public void PickingASiteChangesTheVerbOnTheButton()
    {
        // One press does noticeably more once a site is chosen - it creates something on
        // someone else's server - and the button has to say so before it is pressed.
        var draft = new NewRepositoryViewModel([Signed("git.example.com")]);

        draft.Publish.Target = draft.Publish.Targets.Last();

        Assert.True(draft.Publish.HasSite);
        Assert.Equal("Create and publish", draft.CreateButtonLabel);
        Assert.Contains("git.example.com", draft.SummaryLabel);
    }

    [Fact]
    public void TheSitePickerIsHiddenWhenThereIsNoSiteToPick()
    {
        // A dropdown offering only "nowhere" is a question with one answer.
        Assert.False(new NewRepositoryViewModel([]).Publish.CanChooseSite);
        Assert.True(new NewRepositoryViewModel([Signed("git.example.com")]).Publish.CanChooseSite);
    }

    [Fact]
    public void TheRequestCarriesTheNameDescriptionOwnerAndPrivacy()
    {
        var draft = new NewRepositoryViewModel([Signed("git.example.com")]);
        draft.Publish.Target = draft.Publish.Targets.Last();
        draft.Publish.SetOwners([new RepositoryOwner("tester", IsSelf: true), new RepositoryOwner("polemus")]);
        draft.Publish.Owner = draft.Publish.Owners.Last();
        draft.Publish.IsPrivate = false;

        var request = draft.Publish.ToRequest("omnigit", " A git client ");

        Assert.NotNull(request);
        Assert.Equal("omnigit", request!.Name);
        Assert.Equal("A git client", request.Description);
        Assert.Equal("polemus", request.Owner!.Login);
        Assert.False(request.IsPrivate);
    }

    [Fact]
    public void PrivateIsTheDefaultBecauseTheTwoMistakesCostDifferently()
    {
        // A private repository made public later is a click; code published by accident
        // is on somebody's crawler before it can be taken back.
        Assert.True(new NewRepositoryViewModel([Signed("git.example.com")]).Publish.IsPrivate);
        Assert.True(new PublishRepositoryViewModel("x", "", [Signed("git.example.com")]).Target.IsPrivate);
    }

    [Fact]
    public void CreateWaitsWhileTheOrganisationListIsStillArriving()
    {
        // Pressing Create in that window would publish to whatever the list settled on,
        // which is not what was on screen when the button was pressed.
        var draft = new NewRepositoryViewModel([Signed("git.example.com")])
        {
            Name = "omnigit",
            ParentPath = Path.GetTempPath(),
        };

        Assert.True(draft.CanCreate);

        draft.Publish.Target = draft.Publish.Targets.Last();
        Assert.False(draft.CanCreate);

        draft.Publish.SetOwners([new RepositoryOwner("tester", IsSelf: true)]);
        Assert.True(draft.CanCreate);
    }

    [Fact]
    public void GoingBackToNowhereClearsTheOwnersOfTheSiteThatWasPicked()
    {
        var target = new PublishTargetViewModel([Signed("git.example.com")], allowNone: true);

        target.Target = target.Targets.Last();
        target.SetOwners([new RepositoryOwner("tester", IsSelf: true), new RepositoryOwner("polemus")]);
        Assert.True(target.HasChoiceOfOwner);

        // MainWindowViewModel does this when the site goes away; an organisation of the
        // old site must not stay on screen under a different answer.
        target.Target = target.Targets.First();
        target.SetOwners([]);

        Assert.False(target.HasSite);
        Assert.Empty(target.Owners);
        Assert.True(target.IsSettled);
    }

    [Fact]
    public void ThePublishDialogOffersNoWayToPublishNowhere()
    {
        // A dialog whose entire job is to publish has nothing to offer if the answer is
        // "don't" - which is the one way it differs from the section on the other form.
        var dialog = new PublishRepositoryViewModel("omnigit", "", [Signed("git.example.com")]);

        Assert.DoesNotContain(
            PublishTargetViewModel.NowhereLabel, dialog.Target.Targets.Select(t => t.Label));

        Assert.True(dialog.Target.HasSite);
    }

    [Fact]
    public void ThePublishDialogNamesEverySiteEvenWhenThereIsOnlyOne()
    {
        // Hiding a one-item picker was tried and is wrong here: "which forge is this
        // going to" is precisely the question the dialog exists to answer.
        var dialog = new PublishRepositoryViewModel("omnigit", "", [Signed("git.example.com")]);

        Assert.True(dialog.Target.CanChooseSite);
        Assert.Equal("@tester on git.example.com", dialog.Target.Targets[0].Label);
    }

    [Fact]
    public void PublishIsRefusedUntilAnOwnerHasArrived()
    {
        var dialog = new PublishRepositoryViewModel("omnigit", "", [Signed("git.example.com")]);
        Assert.False(dialog.CanPublish);

        dialog.Target.SetOwners([new RepositoryOwner("tester", IsSelf: true)]);
        Assert.True(dialog.CanPublish);

        dialog.Name = "   ";
        Assert.False(dialog.CanPublish);
    }

    [Fact]
    public void ASiteThatDidNotAnswerSaysSoOnTheFormRatherThanLookingHealthy()
    {
        // MainWindowViewModel falls back to the account itself when the owner lookup
        // fails, because a token that cannot list organisations can usually still
        // create. Silently was wrong: a stopped server then looked exactly like a
        // working one until Create reached the network.
        var target = new PublishTargetViewModel([Signed("git.example.com")], allowNone: true);
        target.Target = target.Targets.Last();

        target.SetOwners([new RepositoryOwner("tester", IsSelf: true)]);
        target.SiteProblem = "Couldn't ask git.example.com …";

        Assert.True(target.HasSiteProblem);
        Assert.True(target.IsSettled);
    }

    [Fact]
    public void AWorkingAnswerClearsAWarningLeftByThePreviousSite()
    {
        var target = new PublishTargetViewModel([Signed("a.example.com"), Signed("b.example.com")], allowNone: true);

        target.SiteProblem = "Couldn't ask a.example.com …";
        target.SetOwners([new RepositoryOwner("tester", IsSelf: true)]);

        Assert.False(target.HasSiteProblem);
    }

    // ---- The form -----------------------------------------------------------

    [Fact]
    public void TheFormAsksForEverythingItNeedsBeforeCreateIsOffered()
    {
        var draft = new NewRepositoryViewModel();
        Assert.False(draft.CanCreate);

        draft.Name = "omnigit";
        Assert.False(draft.CanCreate);

        draft.ParentPath = _root;
        Assert.True(draft.CanCreate);
        Assert.Equal(Path.Combine(_root, "omnigit"), draft.TargetPath);
    }

    [Fact]
    public void ADirectoryWithSomethingInItIsRefusedOutLoud()
    {
        Directory.CreateDirectory(Path.Combine(_root, "taken"));
        File.WriteAllText(Path.Combine(_root, "taken", "notes.txt"), "hello");

        var draft = new NewRepositoryViewModel { Name = "taken", ParentPath = _root };

        // Said out loud rather than left to a disabled button with no explanation.
        Assert.True(draft.HasProblem);
        Assert.Contains("isn't empty", draft.Problem);
        Assert.False(draft.CanCreate);
    }

    [Fact]
    public void AnEmptyDirectoryTheUserJustMadeInThePickerIsFine()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var draft = new NewRepositoryViewModel { Name = "empty", ParentPath = _root };

        Assert.False(draft.HasProblem);
        Assert.True(draft.CanCreate);
    }

    [Fact]
    public void TheReadmeIsOnByDefaultBecauseItIsWhatMakesTheFirstCommit()
    {
        var draft = new NewRepositoryViewModel { Name = "omnigit", ParentPath = _root };

        var files = draft.StartingFiles("Ada Lovelace");

        Assert.Single(files);
        Assert.Equal("README.md", files[0].Path);
        Assert.StartsWith("# omnigit", files[0].Contents);
    }

    [Fact]
    public void AskingForNothingLeavesNoFilesAndSoNoFirstCommit()
    {
        // Allowed: it is what someone importing existing work into an empty folder
        // wants, and it resolves itself the moment they commit.
        var draft = new NewRepositoryViewModel
        {
            Name = "omnigit",
            ParentPath = _root,
            WriteReadme = false,
        };

        Assert.Empty(draft.StartingFiles("Ada Lovelace"));
    }

    [Fact]
    public void AllThreeStartingFilesAreWrittenWhenAllThreeAreAskedFor()
    {
        var draft = new NewRepositoryViewModel
        {
            Name = "omnigit",
            Description = "A git client",
            ParentPath = _root,
            GitignoreName = "Rust",
            Licence = NewRepositoryViewModel.Licences.Single(l => l.Id == "mit"),
        };

        var files = draft.StartingFiles("Ada Lovelace");

        Assert.Equal(["README.md", ".gitignore", "LICENSE"], files.Select(f => f.Path));
        Assert.Contains("A git client", files[0].Contents);
        Assert.Contains("target", files[1].Contents);
        Assert.Contains("Ada Lovelace", files[2].Contents);
    }
}
