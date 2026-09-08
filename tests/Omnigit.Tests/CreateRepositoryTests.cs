using System.Net;
using System.Text;
using System.Text.Json;
using Omnigit.HostProviders;
using Omnigit.ViewModels;

namespace Omnigit.Tests;

/// <summary>
/// Creating a repository on a hosting site: what goes out in the request body, and how
/// the answer is read back.
/// </summary>
/// <remarks>
/// The three sites we ship disagree on every axis of this call at once - the address,
/// how an organisation is named, and how privacy is spelled - which is why the manifest
/// describes a request body here and nowhere else. These tests pin all three against the
/// manifests as shipped, so a typo in <c>gitlab.json</c> fails here rather than at the
/// moment somebody presses Publish.
/// </remarks>
public class CreateRepositoryTests
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions Write = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The manifests we ship, read from source rather than copied in here.</summary>
    private static HostManifest Shipped(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var path = Path.Combine(
            directory!.FullName, "src", "Omnigit", "HostProviders", "Manifests", name);

        return JsonSerializer.Deserialize<HostManifest>(File.ReadAllText(path), Read)!;
    }

    private static HostAccount Account(string providerId, string baseUrl) => new()
    {
        ProviderId = providerId,
        BaseUrl = new Uri(baseUrl),
        Login = "tester",
        DisplayName = "Tester",
        Token = "t0ken",
    };

    // ---- What goes out ------------------------------------------------------

    [Fact]
    public async Task GiteaPostsToTheUserEndpointWithABooleanPrivateFlag()
    {
        var recorder = new Recorder("""
            { "name": "omnigit", "owner": { "login": "tester" },
              "clone_url": "https://git.example.com/tester/omnigit.git",
              "default_branch": "main", "private": true }
            """);

        var provider = new ManifestHostProvider(Shipped("gitea.json"), new HttpClient(recorder));

        var created = await provider.CreateRepositoryAsync(
            Account("gitea", "https://git.example.com"),
            new NewRepository { Name = "omnigit", Description = "A git client", IsPrivate = true },
            default);

        Assert.Equal("/api/v1/user/repos", recorder.Path);
        Assert.Equal("omnigit", recorder.Field("name")!.Value.GetString());
        Assert.Equal("A git client", recorder.Field("description")!.Value.GetString());

        // A boolean, not the string "true": Gitea rejects the quoted form.
        Assert.Equal(JsonValueKind.True, recorder.Field("private")!.Value.ValueKind);

        Assert.Equal("https://git.example.com/tester/omnigit.git", created.CloneUrl);
        Assert.Equal("tester", created.Owner);
    }

    [Fact]
    public async Task GiteaAddressesAnOrganisationByChangingTheUrl()
    {
        var recorder = new Recorder("""
            { "name": "omnigit", "owner": { "login": "polemus" },
              "clone_url": "https://git.example.com/polemus/omnigit.git" }
            """);

        var provider = new ManifestHostProvider(Shipped("gitea.json"), new HttpClient(recorder));

        await provider.CreateRepositoryAsync(
            Account("gitea", "https://git.example.com"),
            new NewRepository { Name = "omnigit", Owner = new RepositoryOwner("polemus") },
            default);

        Assert.Equal("/api/v1/orgs/polemus/repos", recorder.Path);

        // Nothing in the body names the owner - the address already did.
        Assert.Null(recorder.Field("namespace_id"));
    }

    [Fact]
    public async Task GitLabSpellsPrivacyAsAWordAndNamesTheGroupInTheBody()
    {
        var recorder = new Recorder("""
            { "path": "omnigit", "namespace": { "full_path": "polemus" },
              "http_url_to_repo": "https://gitlab.example.com/polemus/omnigit.git",
              "visibility": "private" }
            """);

        var provider = new ManifestHostProvider(Shipped("gitlab.json"), new HttpClient(recorder));

        var created = await provider.CreateRepositoryAsync(
            Account("gitlab", "https://gitlab.example.com"),
            new NewRepository { Name = "omnigit", IsPrivate = true, Owner = new RepositoryOwner("polemus", "42") },
            default);

        // One address for everything, unlike Gitea above.
        Assert.Equal("/api/v4/projects", recorder.Path);
        Assert.Equal("private", recorder.Field("visibility")!.Value.GetString());

        // The group's numeric id, sent as a number. A quoted "42" is refused.
        Assert.Equal(JsonValueKind.Number, recorder.Field("namespace_id")!.Value.ValueKind);
        Assert.Equal(42, recorder.Field("namespace_id")!.Value.GetInt32());

        // And read back through the same visibility mapping the listing uses.
        Assert.True(created.IsPrivate);
        Assert.Equal("polemus", created.Owner);
    }

    [Fact]
    public async Task APublicRepositoryGetsTheOtherWordRatherThanNothing()
    {
        var recorder = new Recorder("""
            { "path": "omnigit", "http_url_to_repo": "https://gitlab.example.com/tester/omnigit.git",
              "visibility": "public" }
            """);

        var provider = new ManifestHostProvider(Shipped("gitlab.json"), new HttpClient(recorder));

        await provider.CreateRepositoryAsync(
            Account("gitlab", "https://gitlab.example.com"),
            new NewRepository { Name = "omnigit", IsPrivate = false },
            default);

        Assert.Equal("public", recorder.Field("visibility")!.Value.GetString());
    }

    // ---- What comes back ----------------------------------------------------

    [Fact]
    public async Task TheSitesOwnWordsExplainARefusal()
    {
        // Every forge answers a name already in use with a 422 and a sentence. "422
        // Unprocessable Content" alone tells the user nothing they can act on.
        var handler = new Recorder(
            """{ "message": { "name": ["has already been taken"] } }""",
            HttpStatusCode.UnprocessableContent);

        var provider = new ManifestHostProvider(Shipped("gitlab.json"), new HttpClient(handler));

        var thrown = await Assert.ThrowsAsync<HostProviderException>(() =>
            provider.CreateRepositoryAsync(
                Account("gitlab", "https://gitlab.example.com"),
                new NewRepository { Name = "omnigit" },
                default));

        Assert.Contains("has already been taken", thrown.Message);
    }

    [Fact]
    public async Task ASiteThatCreatesButReturnsNoCloneUrlSaysSoRatherThanReturningNothing()
    {
        var handler = new Recorder("""{ "name": "omnigit" }""");
        var provider = new ManifestHostProvider(Shipped("gitea.json"), new HttpClient(handler));

        var thrown = await Assert.ThrowsAsync<HostProviderException>(() =>
            provider.CreateRepositoryAsync(
                Account("gitea", "https://git.example.com"),
                new NewRepository { Name = "omnigit" },
                default));

        Assert.Contains("clone URL", thrown.Message);
    }

    // ---- Organisations ------------------------------------------------------

    [Fact]
    public async Task TheOwnerListAlwaysStartsWithTheAccountItself()
    {
        var handler = new Recorder("""[{ "username": "polemus" }, { "username": "acme" }]""");
        var provider = new ManifestHostProvider(Shipped("gitea.json"), new HttpClient(handler));

        var owners = await provider.ListOwnersAsync(Account("gitea", "https://git.example.com"), default);

        Assert.Equal(3, owners.Count);
        Assert.True(owners[0].IsSelf);
        Assert.Equal("tester", owners[0].Login);
        Assert.Equal(["polemus", "acme"], owners.Skip(1).Select(o => o.Login));
    }

    [Fact]
    public async Task GitLabsGroupsCarryTheirNumericIdBecauseTheCreateCallNeedsIt()
    {
        var handler = new Recorder("""[{ "full_path": "polemus/tools", "id": 42 }]""");
        var provider = new ManifestHostProvider(Shipped("gitlab.json"), new HttpClient(handler));

        var owners = await provider.ListOwnersAsync(Account("gitlab", "https://gitlab.example.com"), default);

        Assert.Equal("polemus/tools", owners[1].Login);
        Assert.Equal("42", owners[1].Id);
    }

    [Fact]
    public async Task ASiteWithNoOwnersEndpointStillOffersTheAccount()
    {
        // Not a failure: publishing under your own name works everywhere, and an empty
        // picker would say otherwise.
        var manifest = JsonSerializer.Deserialize<HostManifest>(
            """{ "id": "plain", "displayName": "Plain" }""", Read)!;

        var provider = new ManifestHostProvider(manifest, new HttpClient(new Recorder("[]")));
        var owners = await provider.ListOwnersAsync(Account("plain", "https://example.com"), default);

        Assert.Single(owners);
        Assert.True(owners[0].IsSelf);
    }

    // ---- Capability ---------------------------------------------------------

    [Fact]
    public void AManifestWithNoCreateBlockSaysItCannotCreate()
    {
        var manifest = JsonSerializer.Deserialize<HostManifest>(
            """{ "id": "plain", "displayName": "Plain", "endpoints": { "currentUser": "/user" } }""",
            Read)!;

        var provider = new ManifestHostProvider(manifest, new HttpClient(new Recorder("{}")));

        Assert.False(provider.Capabilities.CanCreateRepositories);
        Assert.False(provider.Capabilities.CanListOwners);
    }

    [Fact]
    public void GitHubSaysItCanCreateSoThePublishDialogOffersIt()
    {
        // The flag is what the publish dialog filters accounts on, and it is easy to
        // add the methods and forget the capability - which would leave GitHub, of all
        // sites, silently absent from the picker.
        var provider = new GitHubProvider(new HttpClient(new Recorder("{}")), configuredClientId: null);

        Assert.True(provider.Capabilities.CanCreateRepositories);
        Assert.True(provider.Capabilities.CanListOwners);
    }

    [Fact]
    public async Task GitHubPostsToTheOrganisationsAddressRatherThanNamingItInTheBody()
    {
        var recorder = new Recorder("""
            { "name": "omnigit", "owner": { "login": "polemus" },
              "clone_url": "https://github.com/polemus/omnigit.git", "private": true }
            """);

        var provider = new GitHubProvider(new HttpClient(recorder), configuredClientId: null);

        var created = await provider.CreateRepositoryAsync(
            Account("github", "https://github.com"),
            new NewRepository { Name = "omnigit", IsPrivate = true, Owner = new RepositoryOwner("polemus") },
            default);

        Assert.Equal("/orgs/polemus/repos", recorder.Path);

        // No auto_init: the local repository already has a first commit, and a README
        // written on the server would be a second root the first push could not pass.
        Assert.Equal(JsonValueKind.False, recorder.Field("auto_init")!.Value.ValueKind);

        Assert.Equal("https://github.com/polemus/omnigit.git", created.CloneUrl);
    }

    [Fact]
    public async Task GitHubsOwnSentenceExplainsARefusalRatherThanTheStatusCode()
    {
        // The top-level message for a duplicate is only "Repository creation failed";
        // the sentence worth showing is in the errors array underneath it.
        var recorder = new Recorder(
            """
            { "message": "Repository creation failed.",
              "errors": [{ "message": "name already exists on this account" }] }
            """,
            HttpStatusCode.UnprocessableContent);

        var provider = new GitHubProvider(new HttpClient(recorder), configuredClientId: null);

        var thrown = await Assert.ThrowsAsync<HostProviderException>(() =>
            provider.CreateRepositoryAsync(
                Account("github", "https://github.com"),
                new NewRepository { Name = "omnigit" },
                default));

        Assert.Contains("name already exists on this account", thrown.Message);
    }

    [Fact]
    public async Task AnAccountThatCannotListOrganisationsStillGetsItsOwnName()
    {
        // A token issued before read:org was asked for gets a 403 here. Publishing under
        // your own name still works, so this is a shorter list rather than a failure.
        var recorder = new Recorder("""{ "message": "Forbidden" }""", HttpStatusCode.Forbidden);
        var provider = new GitHubProvider(new HttpClient(recorder), configuredClientId: null);

        var owners = await provider.ListOwnersAsync(Account("github", "https://github.com"), default);

        Assert.Single(owners);
        Assert.True(owners[0].IsSelf);
    }

    [Fact]
    public void BothShippedManifestsCanCreate()
    {
        foreach (var name in (string[])["gitea.json", "gitlab.json"])
        {
            var provider = new ManifestHostProvider(Shipped(name), new HttpClient(new Recorder("{}")));
            Assert.True(provider.Capabilities.CanCreateRepositories, name);
            Assert.True(provider.Capabilities.CanListOwners, name);
        }
    }

    // ---- The settings form carries the fields too ---------------------------

    [Fact]
    public void TheSettingsFormRoundTripsEverythingCreatingNeeds()
    {
        // A field the form forgets is dropped from the file the moment anyone edits
        // that host - which would silently take Publish away from a working site.
        var original = HostDraftViewModel.GitLabLike();
        original.Id = "gitlab";
        original.DisplayName = "GitLab";

        var reopened = HostDraftViewModel.FromManifest(
            JsonSerializer.Deserialize<HostManifest>(
                JsonSerializer.Serialize(original.ToManifest(), Write), Read)!);

        Assert.Equal("/api/v4/projects", reopened.CreateRepositoryPath);
        Assert.Equal("visibility", reopened.CreatePrivacyField);
        Assert.Equal("private", reopened.CreatePrivacyWhenPrivate);
        Assert.Equal("public", reopened.CreatePrivacyWhenPublic);
        Assert.Equal("namespace_id", reopened.CreateOwnerField);
        Assert.Equal("id", reopened.CreateOwnerSource);
        Assert.Equal("full_path", reopened.OwnerLoginField);
        Assert.Equal("id", reopened.OwnerIdField);
    }

    [Fact]
    public void ClearingTheCreatePathIsHowAHostSaysItCannotCreate()
    {
        var draft = new HostDraftViewModel { Id = "x", DisplayName = "X", CreateRepositoryPath = "  " };

        // Null rather than an empty block: the absence is what the capability reads.
        Assert.Null(draft.ToManifest().CreateRepository);
    }

    /// <summary>
    /// Answers every request with one body, and keeps the last request's path and JSON
    /// so a test can assert on what went out as well as what came back.
    /// </summary>
    private sealed class Recorder(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        private JsonDocument? _sent;

        public string? Path { get; private set; }

        /// <summary>One key of the request body, or null if it was not sent at all.</summary>
        public JsonElement? Field(string name)
            => _sent?.RootElement.TryGetProperty(name, out var value) == true ? value : null;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = "/" + request.RequestUri!.GetComponents(UriComponents.Path, UriFormat.UriEscaped);

            if (request.Content is not null)
                _sent = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
