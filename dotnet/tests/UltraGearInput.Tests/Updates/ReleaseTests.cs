using System.Text.Json;
using UltraGearInput.Core;
using UltraGearInput.Core.Updates;

namespace UltraGearInput.Tests.Updates;

public sealed class ReleaseTests
{
    private static readonly string[] Languages = ["en", "pt", "es"];
    [Theory]
    [InlineData("1.2.10", "1.2.9", true)]
    [InlineData("v2.0.0", "1.99.99", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.0.0", "2.0.0", false)]
    public void ComparesVersionsNumerically(string latest, string current, bool newer) =>
        Assert.Equal(newer, ReleaseVersion.Parse(latest) > ReleaseVersion.Parse(current));

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3-preview")]
    [InlineData("../1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.999999999999999999999")]
    public void RejectsInvalidVersions(string value) => Assert.Throws<UpdateException>(() => ReleaseVersion.Parse(value));

    [Theory]
    [InlineData(UpdateTarget.WindowsX64, "UltraGearInput-windows-x64-2.3.4.exe")]
    [InlineData(UpdateTarget.LinuxX64, "ultragear-input-2.3.4-linux-x64.tar.gz")]
    [InlineData(UpdateTarget.LinuxArm64, "ultragear-input-2.3.4-linux-arm64.tar.gz")]
    [InlineData(UpdateTarget.MacOS, "UltraGearInput-macos-2.3.4.zip")]
    public void SelectsAnExactPlatformPackageAndChecksum(UpdateTarget target, string expected)
    {
        var release = UpdateRelease.Parse(Fixture(), target);
        Assert.Equal(expected, release.Package.Name);
        Assert.Equal(expected + ".sha256", release.Checksum.Name);
        Assert.Equal("https://github.com/gabsdk/ultragear-input/releases/download/v2.3.4/" + expected, release.Package.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("\"draft\":false", "\"draft\":true")]
    [InlineData("\"prerelease\":false", "\"prerelease\":true")]
    [InlineData("https://github.com/", "http://github.com/")]
    [InlineData("gabsdk/ultragear-input", "someone/another-repository")]
    [InlineData(".zip.sha256", ".zip.wrong")]
    [InlineData("\"size\":64", "\"size\":0")]
    public void RejectsUntrustedOrIncompleteReleases(string original, string replacement) =>
        Assert.Throws<UpdateException>(() => UpdateRelease.Parse(Fixture().Replace(original, replacement, StringComparison.Ordinal), UpdateTarget.MacOS));

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("pt_BR", "pt")]
    [InlineData("pt-PT", "pt")]
    [InlineData("es-MX", "es")]
    [InlineData("fr", "en")]
    [InlineData("es.UTF-8", "es")]
    [InlineData("pt_BR.UTF-8", "pt")]
    public void ResolvesRegionalLanguages(string locale, string expected) => Assert.Equal(expected, UpdateText.Language(locale));

    [Theory]
    [InlineData(0x0416, "pt")]
    [InlineData(0x0816, "pt")]
    [InlineData(0x0C0A, "es")]
    [InlineData(0x0409, "en")]
    public void ResolvesWindowsLanguages(ushort languageId, string language) => Assert.Equal(language, UpdateText.WindowsLanguage(languageId));

    [Fact]
    public void CatalogsHaveTheSameKeysAndPlaceholders()
    {
        var catalogs = Languages.Select(language =>
        {
            using var stream = typeof(UpdateText).Assembly.GetManifestResourceStream($"updates.{language}.json")!;
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal);
        }).ToList();
        foreach (var catalog in catalogs)
        {
            Assert.Equal(catalogs[0].Keys.Order(), catalog.Keys.Order());
            foreach (var (key, text) in catalog)
            {
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.Equal(catalogs[0][key].Contains("{0}", StringComparison.Ordinal), text.Contains("{0}", StringComparison.Ordinal));
            }
        }
        Assert.Equal("Instalar 2.3.4…", UpdateText.Get("install", "pt", "2.3.4"));
        Assert.Equal("Instalar 2.3.4…", UpdateText.Get("install", "es", "2.3.4"));
        Assert.Equal("Install 2.3.4…", UpdateText.Get("install", "en", "2.3.4"));
    }

    [Fact]
    public void MenuKeepsTheUpdateActionDisabledWhileBusy()
    {
        var entries = TrayMenu.Build([], new SettingsLoad(new Settings(), null), new TrayCommandLabels("Start", "Quit"), false, "Busy", updating: true);
        var command = Assert.Single(entries.OfType<TrayCommand>(), item => item.Kind == TrayCommandKind.Update);
        Assert.Equal("Busy", command.Text);
        Assert.False(command.IsEnabled);
    }

    internal static string Fixture()
    {
        var assets = Enum.GetValues<UpdateTarget>().SelectMany(target =>
        {
            var name = UpdateRelease.PackageName(ReleaseVersion.Parse("2.3.4"), target);
            return new[] { name, name + ".sha256" }.Select(file => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = file,
                ["browser_download_url"] = "https://github.com/gabsdk/ultragear-input/releases/download/v2.3.4/" + file,
                ["size"] = 64,
            });
        });
        return JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["tag_name"] = "v2.3.4",
            ["draft"] = false,
            ["prerelease"] = false,
            ["assets"] = assets,
        });
    }
}
