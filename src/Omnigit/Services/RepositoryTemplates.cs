using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Omnigit.Services;

/// <summary>
/// The starting files a new repository can be given: a README, a <c>.gitignore</c> for
/// the language, and a licence.
/// </summary>
/// <remarks>
/// The texts are embedded resources rather than strings in code, because a licence has
/// to be reproduced exactly and one retyped by hand is one that is subtly wrong. The
/// <c>.gitignore</c> files come from <c>github/gitignore</c> (CC0) and the licences from
/// <c>github/choosealicense.com</c> - the same two sources GitHub Desktop bundles, so a
/// repository started here and one started there begin identically. Adding another is
/// dropping a file into the folder; nothing here names them individually.
///
/// The licence files keep their upstream YAML front matter, and
/// <see cref="LicenceTitle"/> reads the title out of it. Deriving the title from the
/// licence text instead was tried and is wrong: the first line of both GNU files is
/// "GNU GENERAL PUBLIC LICENSE", so the picker showed two rows saying the same thing
/// with no way to tell v2 from v3. A hand-written table of names would have fixed that
/// and become a second place to keep in step with the folder; the front matter is the
/// file's own metadata and travels with it. It is stripped before anything is written.
/// </remarks>
public static class RepositoryTemplates
{
    private const string GitignorePrefix = "Omnigit.Resources.Gitignore.";
    private const string LicencePrefix = "Omnigit.Resources.Licences.";

    /// <summary>What the pickers show for "I don't want one", in both lists.</summary>
    public const string None = "None";

    private static readonly Assembly Owner = Assembly.GetExecutingAssembly();

    /// <summary>
    /// Language names for the <c>.gitignore</c> picker, alphabetically, with
    /// <see cref="None"/> first. The name is also the key <see cref="Gitignore"/> takes.
    /// </summary>
    public static IReadOnlyList<string> GitignoreNames { get; } =
        [None, .. Names(GitignorePrefix, ".gitignore").OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// SPDX identifiers for the licence picker, ordered by the title shown for each -
    /// which is what the reader is scanning, and is not the order the ids sort in.
    /// </summary>
    public static IReadOnlyList<string> LicenceIds { get; } =
        [None, .. Names(LicencePrefix, ".txt").OrderBy(LicenceTitle, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The contents of one <c>.gitignore</c>, or null for <see cref="None"/>.</summary>
    public static string? Gitignore(string? name)
        => Read(GitignorePrefix, name, ".gitignore");

    /// <summary>
    /// One licence with its blanks filled in: the year, the copyright holder, and for
    /// the GNU family the program's own name. A licence left holding <c>[fullname]</c>
    /// names nobody and protects nobody, which is why this substitutes rather than
    /// copying the file across untouched.
    /// </summary>
    public static string? Licence(string? id, string holder, string repositoryName, int year)
    {
        if (Read(LicencePrefix, id, ".txt") is not { } file)
            return null;

        var text = Body(file);
        var name = string.IsNullOrWhiteSpace(holder) ? "the copyright holders" : holder.Trim();

        return text
            .Replace("[year]", year.ToString(), StringComparison.Ordinal)
            .Replace("[yyyy]", year.ToString(), StringComparison.Ordinal)
            .Replace("<year>", year.ToString(), StringComparison.Ordinal)
            .Replace("[fullname]", name, StringComparison.Ordinal)
            .Replace("[name of copyright owner]", name, StringComparison.Ordinal)
            .Replace("<name of author>", name, StringComparison.Ordinal)
            .Replace("<program>", repositoryName, StringComparison.Ordinal)
            .Replace(
                "<one line to give the program's name and a brief idea of what it does.>",
                repositoryName,
                StringComparison.Ordinal)
            .Replace(
                "<one line to give the library's name and a brief idea of what it does.>",
                repositoryName,
                StringComparison.Ordinal);
    }

    /// <summary>
    /// The name a licence goes by, read from its own front matter - so the folder is
    /// still the only list. Falls back to the id for a file dropped in without any.
    /// </summary>
    public static string LicenceTitle(string id)
    {
        if (string.Equals(id, None, StringComparison.Ordinal))
            return None;

        if (Read(LicencePrefix, id, ".txt") is not { } file)
            return id;

        foreach (var line in FrontMatter(file).Split('\n'))
        {
            if (line.StartsWith("title:", StringComparison.Ordinal))
                return line["title:".Length..].Trim();
        }

        return id;
    }

    /// <summary>The opening README, which is what makes the first commit worth having.</summary>
    public static string Readme(string name, string description)
    {
        var text = new StringBuilder($"# {name}\n");

        if (!string.IsNullOrWhiteSpace(description))
            text.Append('\n').Append(description.Trim()).Append('\n');

        return text.ToString();
    }

    /// <summary>The <c>---</c>-fenced YAML at the top of a licence file, or empty.</summary>
    private static string FrontMatter(string file)
        => Fence(file) is var (start, end) && end > start ? file[start..end] : string.Empty;

    /// <summary>The licence itself, with any front matter and its fence removed.</summary>
    private static string Body(string file)
        => Fence(file) is var (_, end) && end > 0
            ? file[end..].TrimStart('-', '\r', '\n')
            : file;

    /// <summary>
    /// Where the front matter starts and ends. A file that does not open with
    /// <c>---</c> has none, and is all body - which is what a licence someone dropped
    /// into the folder by hand looks like.
    /// </summary>
    private static (int Start, int End) Fence(string file)
    {
        if (!file.StartsWith("---", StringComparison.Ordinal))
            return (0, 0);

        var start = file.IndexOf('\n') + 1;
        if (start <= 0)
            return (0, 0);

        var end = file.IndexOf("\n---", start, StringComparison.Ordinal);
        return end < 0 ? (0, 0) : (start, end + 1);
    }

    private static IEnumerable<string> Names(string prefix, string suffix)
        => Owner.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                        && n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..^suffix.Length]);

    private static string? Read(string prefix, string? name, string suffix)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, None, StringComparison.Ordinal))
            return null;

        using var stream = Owner.GetManifestResourceStream($"{prefix}{name}{suffix}");
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
