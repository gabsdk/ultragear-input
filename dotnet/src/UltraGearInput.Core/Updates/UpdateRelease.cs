using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UltraGearInput.Core.Updates;

public sealed class UpdateException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

public sealed record ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion Parse(string value)
    {
        var match = Regex.Match(value, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out var patch))
        {
            throw new UpdateException("release");
        }
        return new ReleaseVersion(major, minor, patch);
    }

    public int CompareTo(ReleaseVersion? other) => other is null ? 1
        : Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}

public enum UpdateTarget
{
    WindowsX64,
    LinuxX64,
    LinuxArm64,
    MacOS,
}

public sealed record UpdateAsset(string Name, Uri Url, long Size);

public sealed record UpdateRelease(ReleaseVersion Version, UpdateAsset Package, UpdateAsset Checksum)
{
    public const string Repository = "gabsdk/ultragear-input";
    public static Uri Latest { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static string PackageName(ReleaseVersion version, UpdateTarget target) => target switch
    {
        UpdateTarget.WindowsX64 => $"UltraGearInput-windows-x64-{version}.exe",
        UpdateTarget.LinuxX64 => $"ultragear-input-{version}-linux-x64.tar.gz",
        UpdateTarget.LinuxArm64 => $"ultragear-input-{version}-linux-arm64.tar.gz",
        _ => $"UltraGearInput-macos-{version}.zip",
    };

    public static UpdateRelease Parse(string json, UpdateTarget target)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            {
                throw new UpdateException("release");
            }
            var tag = root.GetProperty("tag_name").GetString()!;
            var version = ReleaseVersion.Parse(tag);
            if (tag != "v" + version)
            {
                throw new UpdateException("release");
            }
            var name = PackageName(version, target);
            return new UpdateRelease(version, Asset(name), Asset(name + ".sha256"));

            UpdateAsset Asset(string expectedName)
            {
                var matches = root.GetProperty("assets").EnumerateArray().Where(item => item.GetProperty("name").GetString() == expectedName).ToList();
                if (matches.Count != 1)
                {
                    throw new UpdateException("release");
                }
                var item = matches[0];
                var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{expectedName}";
                if (item.GetProperty("browser_download_url").GetString() != expectedUrl
                    || item.GetProperty("size").GetInt64() is <= 0 or > UpdateClient.MaximumPackageSize)
                {
                    throw new UpdateException("release");
                }
                return new UpdateAsset(expectedName, new Uri(expectedUrl), item.GetProperty("size").GetInt64());
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {
            throw new UpdateException("release");
        }
    }
}
