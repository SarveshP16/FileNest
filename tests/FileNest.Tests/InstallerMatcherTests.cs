using FileNest.Core.Services;

namespace FileNest.Tests;

public class InstallerMatcherTests
{
    private static readonly IReadOnlyList<InstalledApp> Apps =
    [
        App("Microsoft Visual Studio Code (User)"),
        App("Google Chrome"),
        App("Python 3.13.0 (64-bit)"),
        App("7-Zip 24.08 (x64)"),
        App("Microsoft Teams Meeting Add-in for Microsoft Office"),
        App("Microsoft Teams"),
        App("Discord"),
        App("Microsoft Visual C++ 2015-2022 Redistributable (x64)"),
        App("Brave"),
        App("Git"),
        App("GitHub Desktop"),
    ];

    private static InstalledApp App(string name) => new(name, null, null, null);

    private static InstallerFile File(string name, string? product = null) => new($@"C:\Downloads\{name}", product, 1000, DateTime.Now);

    [Theory]
    [InlineData("VSCodeUserSetup-x64-1.95.3.exe", "Visual Studio Code", "Microsoft Visual Studio Code (User)")]
    [InlineData("python-3.13.0-amd64.exe", "Python 3.13.0 (64-bit)", "Python 3.13.0 (64-bit)")]
    [InlineData("7z2408-x64.exe", "7-Zip", "7-Zip 24.08 (x64)")]
    [InlineData("TeamsSetup.exe", "Microsoft Teams", "Microsoft Teams")]
    public void Matches_by_product_name_with_high_confidence(string file, string product, string expectedApp)
    {
        var match = InstallerMatcher.Match(File(file, product), Apps);

        Assert.NotNull(match);
        Assert.Equal(expectedApp, match.App.Name);
        Assert.Equal(MatchConfidence.High, match.Confidence);
    }

    [Theory]
    [InlineData("ChromeSetup.exe", "Google Chrome")]
    [InlineData("DiscordSetup.exe", "Discord")]
    [InlineData("BraveBrowserSetup.exe", "Brave")]
    [InlineData("GitHubDesktopSetup-x64.exe", "GitHub Desktop")]
    public void Falls_back_to_the_file_name(string file, string expectedApp)
    {
        var match = InstallerMatcher.Match(File(file), Apps);

        Assert.Equal(expectedApp, match?.App.Name);
        Assert.Equal(MatchConfidence.Likely, match?.Confidence);
    }

    [Theory]
    [InlineData("SpotifySetup.exe", null)]
    [InlineData("setup.exe", null)]
    [InlineData("install-x64.msi", null)]
    [InlineData("code.exe", null)]
    [InlineData("zip-tool.exe", null)]
    [InlineData("GitKrakenSetup.exe", null)]
    public void Doesnt_match_unrelated_or_generic_installers(string file, string? product)
    {
        Assert.Null(InstallerMatcher.Match(File(file, product), Apps));
    }

    [Fact]
    public void Tokens_split_camel_case_and_drop_versions_and_noise()
    {
        Assert.Equal(["vs", "code"], InstallerMatcher.Tokens("VSCodeUserSetup-x64-1.95.3"));
        Assert.Equal(["google", "chrome"], InstallerMatcher.Tokens("Google Chrome Installer"));
        Assert.Empty(InstallerMatcher.Tokens("Setup_x64_v2.1"));
    }
}
