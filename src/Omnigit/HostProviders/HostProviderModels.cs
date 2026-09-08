using System;
using System.Collections.Generic;

namespace Omnigit.HostProviders;

/// <summary>How a provider can get a token for a site.</summary>
public enum AuthMethod
{
    /// <summary>The user creates a token on the site and pastes it in. Works everywhere.</summary>
    PersonalAccessToken,

    /// <summary>Show a short code, the user approves it in a browser, we poll until done.</summary>
    BrowserDeviceLogin,
}

/// <summary>
/// A signed-in identity on one site. The token is held here only while the app runs;
/// saving it goes through <see cref="Services.ICredentialStore"/> so it never lands in
/// a plain text file next to the repository list.
/// </summary>
public sealed class HostAccount
{
    public required string ProviderId { get; init; }
    public required Uri BaseUrl { get; init; }
    public required string Login { get; init; }
    public required string DisplayName { get; init; }
    public required string Token { get; init; }
    public string? AvatarUrl { get; init; }

    /// <summary>Stable key for the credential store and for spotting duplicate sign-ins.</summary>
    public string Key => $"{ProviderId}|{BaseUrl.Host}|{Login}";

    public string Handle => $"@{Login}";
}

/// <summary>A repository as the site describes it, before it has been cloned.</summary>
public sealed class RemoteRepository
{
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public required string CloneUrl { get; init; }
    public required string DefaultBranch { get; init; }
    public bool IsPrivate { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public string FullName => string.IsNullOrEmpty(Owner) ? Name : $"{Owner}/{Name}";
}

/// <summary>
/// What to create on a site, filled in by the publish dialog. Deliberately the small
/// set every forge agrees on - a name, some prose, and whether the world can see it.
/// </summary>
public sealed class NewRepository
{
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool IsPrivate { get; init; } = true;

    /// <summary>
    /// The account or organisation it belongs to. Empty means the signed-in user, which
    /// is a different API call on most sites rather than a value to send.
    /// </summary>
    public RepositoryOwner? Owner { get; init; }
}

/// <summary>
/// Somewhere a repository can be created: the signed-in user, or an organisation they
/// belong to.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is carried because the forges disagree on how an owner is named.
/// GitHub and Gitea put the login in the URL - <c>/orgs/{login}/repos</c> - while GitLab
/// POSTs to one address and identifies the group by a numeric <c>namespace_id</c> in the
/// body. Holding both means a manifest can describe either without code.
/// </remarks>
public sealed record RepositoryOwner(string Login, string? Id = null, bool IsSelf = false)
{
    public string Label => IsSelf ? $"{Login} (your account)" : Login;
}

/// <summary>An open pull request, as the site describes it.</summary>
/// <remarks>
/// Deliberately the same shape for every site. What a forge calls this differs - GitLab
/// says merge request - but a number, a title, and the two branches involved is all the
/// list and the checkout need, and every forge has those.
/// </remarks>
public sealed class PullRequest
{
    public required int Number { get; init; }
    public required string Title { get; init; }
    public required string Author { get; init; }

    /// <summary>The branch being proposed. On a fork, it exists only on the fork.</summary>
    public required string SourceBranch { get; init; }

    /// <summary>The branch it would be merged into.</summary>
    public required string TargetBranch { get; init; }

    public bool IsDraft { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The page on the site. Sites return this, so it needs no template.</summary>
    public string? WebUrl { get; init; }

    public string Reference => $"#{Number}";

    /// <summary>
    /// The local branch a checkout lands on. Always <c>pr/&lt;number&gt;</c>, never the
    /// source branch's own name: a pull request from a fork can be called anything,
    /// including something already checked out here.
    /// </summary>
    public string LocalBranchName => $"pr/{Number}";

    public string AuthorLabel => string.IsNullOrEmpty(Author) ? Reference : $"{Reference} by {Author}";

    public string BranchLabel => $"{SourceBranch} → {TargetBranch}";

    public string RelativeTime => UpdatedAt is { } when ? Models.TimeFormat.Relative(when) : string.Empty;

    public string TitleLabel => IsDraft ? $"{Title} (draft)" : Title;
}

/// <summary>
/// The pending half of a browser login. The UI shows <see cref="UserCode"/> and
/// <see cref="VerificationUri"/> while the provider waits for the user to approve.
/// </summary>
public sealed class DeviceLogin
{
    public required string DeviceCode { get; init; }
    public required string UserCode { get; init; }
    public required Uri VerificationUri { get; init; }

    /// <summary>Seconds the site asks us to wait between checks.</summary>
    public int IntervalSeconds { get; init; } = 5;

    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.Now.AddMinutes(15);
}

/// <summary>Username and password handed to libgit2 for an HTTPS remote.</summary>
public sealed record GitCredentials(string Username, string Password);

/// <summary>What a provider supports, so the UI can hide what it can't do.</summary>
public sealed class HostCapabilities
{
    public required IReadOnlyList<AuthMethod> AuthMethods { get; init; }
    public bool CanListRepositories { get; init; } = true;

    /// <summary>
    /// False for a manifest with no createRepository block, which is every host written
    /// before the format had one. The publish dialog leaves such an account out of its
    /// picker rather than offering a site that will refuse.
    /// </summary>
    public bool CanCreateRepositories { get; init; }

    /// <summary>
    /// Whether <see cref="IHostProvider.ListOwnersAsync"/> can name organisations as
    /// well as the user. False leaves the publish dialog's owner picker showing the one
    /// account, which is correct rather than empty.
    /// </summary>
    public bool CanListOwners { get; init; }

    /// <summary>
    /// False for a manifest with no pullRequests endpoint, which is every host written
    /// before the format had one. The picker hides the tab rather than showing an
    /// empty list that can never fill.
    /// </summary>
    public bool CanListPullRequests { get; init; }

    public bool SupportsHttpsCredentials { get; init; } = true;
}

/// <summary>Thrown for site/API failures so the UI can show something meaningful.</summary>
public sealed class HostProviderException(string message, Exception? inner = null)
    : Exception(message, inner);
