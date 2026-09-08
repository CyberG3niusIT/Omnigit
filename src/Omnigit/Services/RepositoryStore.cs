using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Omnigit.Services;

/// <summary>Remembers which clones the user has added, between launches.</summary>
public interface IRepositoryStore
{
    /// <summary>
    /// The saved list, and which of them was open last. Paths that no longer exist are
    /// dropped - a repository deleted from outside the app must not stop it starting.
    /// </summary>
    StoredRepositories Load();

    /// <summary>
    /// Writes both halves at once.
    /// </summary>
    /// <param name="lastOpened">
    /// The repository to reopen next launch, or null to leave that unanswered. Written
    /// alongside the list rather than in a file of its own, because the two are only
    /// ever meaningful together: a last-opened path that is not in the list is a
    /// repository the app would not otherwise know about.
    /// </param>
    void Save(IEnumerable<string> paths, string? lastOpened);
}

/// <summary>What <see cref="IRepositoryStore.Load"/> found.</summary>
public sealed record StoredRepositories(IReadOnlyList<string> Paths, string? LastOpened)
{
    public static StoredRepositories Empty { get; } = new([], null);
}

/// <summary>
/// Persists the known-repository list as JSON under the platform's per-user
/// application data directory.
/// </summary>
public sealed class RepositoryStore : IRepositoryStore
{
    private readonly string _file;

    public RepositoryStore() : this(AppPaths.In("repositories.json")) { }

    /// <summary>
    /// Writes somewhere other than the user's own config directory. Internal because
    /// nothing in the app wants it; the tests do, and reading the real file back is the
    /// only way to prove the on-disk shape survives a version that has never seen it.
    /// </summary>
    internal RepositoryStore(string file) => _file = file;

    public StoredRepositories Load()
    {
        try
        {
            if (!File.Exists(_file))
                return StoredRepositories.Empty;

            var json = File.ReadAllText(_file);
            var state = JsonSerializer.Deserialize(json, StoreJsonContext.Default.StoreState);

            if (state is null)
                return StoredRepositories.Empty;

            var paths = state.Repositories?.Where(Directory.Exists).ToList() ?? [];

            // A repository moved or deleted since last time is dropped from the list
            // above, and must not be reopened either - the app would start by failing
            // to open something the user cannot see.
            var last = paths.Contains(state.LastOpened ?? string.Empty, StringComparer.Ordinal)
                ? state.LastOpened
                : null;

            return new StoredRepositories(paths, last);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable list must never stop the app starting.
            return StoredRepositories.Empty;
        }
    }

    public void Save(IEnumerable<string> paths, string? lastOpened)
    {
        try
        {
            var state = new StoreState
            {
                Repositories = paths.Distinct(StringComparer.Ordinal).ToList(),
                LastOpened = lastOpened,
            };

            var json = JsonSerializer.Serialize(state, StoreJsonContext.Default.StoreState);

            File.WriteAllText(_file, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the list is an annoyance, not a failure worth surfacing.
        }
    }
}

public sealed class StoreState
{
    public List<string> Repositories { get; set; } = [];

    /// <summary>
    /// The repository open when the app last wrote this file. Null in a file written by
    /// an older version, which reads as "no preference" and opens the first in the list
    /// exactly as before.
    /// </summary>
    public string? LastOpened { get; set; }
}

// Source-generated so the store keeps working if trimming is ever enabled.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(StoreState))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;
