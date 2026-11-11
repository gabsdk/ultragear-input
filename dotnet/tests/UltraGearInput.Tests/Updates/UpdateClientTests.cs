using System.Net;
using System.Security.Cryptography;
using System.Text;
using UltraGearInput.Core.Updates;

namespace UltraGearInput.Tests.Updates;

public sealed class UpdateClientTests
{
    [Fact]
    public async Task DownloadsOnlyThePinnedAssetAndChecksItsHash()
    {
        using var directory = new TemporaryDirectory();
        var release = UpdateRelease.Parse(ReleaseTests.Fixture(), UpdateTarget.LinuxArm64);
        var bytes = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            return Response(request.RequestUri == release.Checksum.Url ? Encoding.UTF8.GetBytes(hash + "  " + release.Package.Name) : bytes);
        }));
        var destination = directory.Combine("download");

        await new UpdateClient(http).DownloadAsync(release, destination, TestContext.Current.CancellationToken);

        Assert.Equal([release.Checksum.Url, release.Package.Url], requests);
        Assert.Equal(bytes, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task RejectsAnIncorrectDownloadHash()
    {
        using var directory = new TemporaryDirectory();
        var release = UpdateRelease.Parse(ReleaseTests.Fixture(), UpdateTarget.WindowsX64);
        using var http = new HttpClient(new Handler(request => Response(request.RequestUri == release.Checksum.Url
            ? Encoding.UTF8.GetBytes(new string('0', 64) + "  " + release.Package.Name) : new byte[64])));
        var error = await Assert.ThrowsAsync<UpdateException>(() => new UpdateClient(http).DownloadAsync(release, directory.Combine("download"), TestContext.Current.CancellationToken));
        Assert.Equal("checksum", error.Reason);
    }

    [Fact]
    public async Task RejectsRedirectsOutsideGithubBeforeRequestingThem()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com/payload") } };
        }));
        await Assert.ThrowsAsync<UpdateException>(() => new UpdateClient(http).LatestAsync(UpdateTarget.WindowsX64, TestContext.Current.CancellationToken));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task ReportsGithubErrors()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<UpdateException>(() => new UpdateClient(http).LatestAsync(UpdateTarget.WindowsX64, TestContext.Current.CancellationToken));
        Assert.Equal("network", error.Reason);
    }

    [Fact]
    public async Task OlderReleasesDoNotRequireAnAssetForANewPlatform()
    {
        using var http = new HttpClient(new Handler(_ => Response(Encoding.UTF8.GetBytes("{\"tag_name\":\"v1.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[]}"))));
        Assert.Null(await new UpdateClient(http).CheckAsync(UpdateTarget.LinuxArm64, "2.0.0", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("0000  package.exe")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  another.exe")]
    public void RejectsMalformedOrUnrelatedChecksums(string text) =>
        Assert.Throws<UpdateException>(() => UpdateClient.ParseChecksum(text, "package.exe"));

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
