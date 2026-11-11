using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace UltraGearInput.Core.Updates;

public sealed class UpdateClient(HttpClient client)
{
    public const long MaximumPackageSize = 256L * 1024 * 1024;
    private const int MaximumMetadataSize = 1024 * 1024;

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public async Task<UpdateRelease> LatestAsync(UpdateTarget target, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await GetAsync(UpdateRelease.Latest, timeout.Token).ConfigureAwait(false);
        var bytes = await ReadAsync(response, MaximumMetadataSize, timeout.Token).ConfigureAwait(false);
        return UpdateRelease.Parse(Encoding.UTF8.GetString(bytes), target);
    }

    public async Task<UpdateRelease?> CheckAsync(UpdateTarget target, string currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await GetAsync(UpdateRelease.Latest, timeout.Token).ConfigureAwait(false);
        var bytes = await ReadAsync(response, MaximumMetadataSize, timeout.Token).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(bytes);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
        {
            throw new UpdateException("release");
        }
        var version = ReleaseVersion.Parse(root.GetProperty("tag_name").GetString()!);
        return version.CompareTo(ReleaseVersion.Parse(currentVersion)) > 0 ? UpdateRelease.Parse(json, target) : null;
    }

    public async Task DownloadAsync(UpdateRelease release, string destination, CancellationToken cancellationToken)
    {
        using var checksumResponse = await GetAsync(release.Checksum.Url, cancellationToken).ConfigureAwait(false);
        var checksumBytes = await ReadAsync(checksumResponse, 1024, cancellationToken).ConfigureAwait(false);
        var expected = ParseChecksum(Encoding.UTF8.GetString(checksumBytes), release.Package.Name);
        using var response = await GetAsync(release.Package.Url, cancellationToken).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            length += count;
            if (length > release.Package.Size || length > MaximumPackageSize)
            {
                throw new UpdateException("package");
            }
            hash.AppendData(buffer.AsSpan(0, count));
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (length != release.Package.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expected))
        {
            throw new UpdateException("checksum");
        }
    }

    public static byte[] ParseChecksum(string text, string fileName)
    {
        var parts = text.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts is not [var digest, var name] || (name != fileName && name != "*" + fileName) || digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        {
            throw new UpdateException("checksum");
        }
        return Convert.FromHexString(digest);
    }

    private async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (!TrustedUrl(url))
            {
                throw new UpdateException("release");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("UltraGearInput-Updater/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(url.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream"));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                {
                    throw new UpdateException("release");
                }
                url = new Uri(url, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new UpdateException("network");
            }
            return response;
        }
        throw new UpdateException("network");
    }

    private static bool TrustedUrl(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0
        && url.Host is "api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static async Task<byte[]> ReadAsync(HttpResponseMessage response, int maximum, CancellationToken cancellationToken)
    {
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum)
            {
                throw new UpdateException("release");
            }
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
