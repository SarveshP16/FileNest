using System.Text.RegularExpressions;

namespace FileNest.Core.Services;

/// <summary>An app from Windows' installed-programs list.</summary>
public sealed record InstalledApp(string Name, string? Publisher, string? Version, DateTime? InstallDate);

/// <summary>An installer file and what it says about itself (product name from its version info, if any).</summary>
public sealed record InstallerFile(string Path, string? ProductName, long SizeBytes, DateTime Modified);

/// <summary>How sure we are an installer's app is already installed.</summary>
public enum MatchConfidence
{
    /// <summary>The installer's own product name matches an installed app.</summary>
    High,

    /// <summary>Only the file name matches.</summary>
    Likely,
}

public sealed record InstallerMatch(InstallerFile Installer, InstalledApp App, MatchConfidence Confidence);

/// <summary>
/// Decides whether the app an installer would install is already on this PC, by comparing words in the
/// installer's product name (or file name) with installed app names. Pure logic, no IO.
/// </summary>
public static partial class InstallerMatcher
{
    public static readonly string[] InstallerExtensions = [".exe", ".msi"];

    /// <summary>Words that say nothing about which app an installer is for.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "installer", "install", "installation", "x64", "x86", "win64", "win32", "amd64", "arm64", "64bit", "32bit",
        "bit", "win", "windows", "user", "system", "full", "offline", "online", "web", "stub", "latest", "stable", "release",
        "bootstrapper", "bootstrap", "update", "updater", "x", "v", "the", "for", "and", "edition", "free", "app",
        "desktop", "client", "inc", "corp", "corporation", "llc", "ltd", "msi", "exe", "en", "us", "multi", "lang",
    };

    public static InstallerMatch? Match(InstallerFile installer, IReadOnlyList<InstalledApp> apps)
    {
        if (Tokens(installer.ProductName) is { Count: > 0 } productTokens && Find(productTokens, apps, minWordLength: 4) is { } byProduct)
            return new InstallerMatch(installer, byProduct, MatchConfidence.High);

        // File names are less reliable than product names, so a lone word must be longer ("discord" yes, "code" no).
        var fileTokens = Tokens(Path.GetFileNameWithoutExtension(installer.Path));
        return fileTokens.Count > 0 && Find(fileTokens, apps, minWordLength: 5) is { } byFile
            ? new InstallerMatch(installer, byFile, MatchConfidence.Likely)
            : null;
    }

    /// <summary>
    /// The installed app containing every installer word. A single word shorter than <paramref name="minWordLength"/>
    /// only matches an app whose name is that word alone, so "zip" doesn't match everything. When several apps
    /// qualify, the closest name wins.
    /// </summary>
    private static InstalledApp? Find(IReadOnlyList<string> wanted, IReadOnlyList<InstalledApp> apps, int minWordLength)
    {
        var shortName = wanted.Count < 2 && wanted.All(t => t.Length < minWordLength);

        InstalledApp? best = null;
        var bestExtra = int.MaxValue;
        foreach (var app in apps)
        {
            var have = Tokens(app.Name);
            if (have.Count == 0)
                continue;
            if (shortName && !have.SequenceEqual(wanted))
                continue;
            var joined = string.Concat(have);
            if (!wanted.All(w => have.Contains(w) || (w.Length >= 5 && joined.Contains(w, StringComparison.Ordinal))))
                continue;
            var extra = have.Count - wanted.Count;
            if (extra < bestExtra)
            {
                best = app;
                bestExtra = extra;
            }
        }

        // The other way round: the app's whole name inside the installer's ("Brave" in "BraveBrowserSetup").
        // Only for distinctive app names, so "Git" doesn't claim "GitHubDesktopSetup".
        if (best is null)
        {
            foreach (var app in apps)
            {
                var have = Tokens(app.Name);
                var distinctive = have.Count >= 2 || have.Any(t => t.Length >= minWordLength);
                if (have.Count == 0 || !distinctive || !have.All(wanted.Contains))
                    continue;
                var missing = wanted.Count - have.Count;
                if (missing < bestExtra)
                {
                    best = app;
                    bestExtra = missing;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Lower-case meaningful words: splits on punctuation and camelCase ("VSCodeUserSetup" -> vs, code),
    /// drops version numbers and noise words.
    /// </summary>
    public static List<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var spaced = CamelBoundary().Replace(text, " ");
        return WordSplit().Split(spaced)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2 && !Noise.Contains(t) && !t.Any(char.IsDigit))
            .Distinct()
            .ToList();
    }

    // "VSCode" -> "VS Code", "ChromeSetup" -> "Chrome Setup", "x64Setup" -> "x64 Setup".
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex WordSplit();
}
