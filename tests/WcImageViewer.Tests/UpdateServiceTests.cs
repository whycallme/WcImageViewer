using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WcImageViewer.Models;
using WcImageViewer.Services;

namespace WcImageViewer.Tests;

public class UpdateServiceTests
{
    private const string Digest = "sha256:0123456789abcdef";

    [Theory]
    [InlineData(UpdateService.PortableAssetName, false)]
    [InlineData(UpdateService.LiteAssetName, true)]
    public void ParseReleaseSelectsAssetForFlavor(string assetName, bool isLite)
    {
        var release = UpdateService.ParseRelease(CreateFeed("v0.4.1", assetName), isLite);

        Assert.Equal(assetName, release.AssetName);
        Assert.Equal("v0.4.1", release.Tag);
        Assert.Equal("Release v0.4.1", release.Name);
        Assert.Equal("notes", release.Body);
        Assert.Equal(1024, release.AssetSize);
        Assert.Equal(Digest, release.AssetDigest);
        Assert.StartsWith("https://example.test/", release.AssetUrl);
    }

    [Fact]
    public void ParseReleaseFailsWhenFlavorAssetIsMissing()
    {
        var json = CreateFeed("v0.4.1", UpdateService.LiteAssetName);

        Assert.Throws<InvalidOperationException>(
            () => UpdateService.ParseRelease(json, isLite: false));
    }

    [Theory]
    [InlineData(null, 1024)]
    [InlineData("", 1024)]
    [InlineData("not-a-digest", 1024)]
    [InlineData(Digest, 0)]
    public void ParseReleaseRequiresDigestAndSize(string? digest, long size)
    {
        var json = CreateFeed("v0.4.1", digest: digest, size: size);

        Assert.Throws<InvalidOperationException>(
            () => UpdateService.ParseRelease(json, isLite: false));
    }

    [Fact]
    public void ParseReleaseRejectsBrokenJson()
    {
        Assert.ThrowsAny<JsonException>(
            () => UpdateService.ParseRelease("{ broken", isLite: false));
    }

    [Fact]
    public void ParseTagVersionAcceptsPrefixAndRejectsGarbage()
    {
        Assert.Equal(new Version(0, 4, 1), UpdateService.ParseTagVersion("v0.4.1"));
        Assert.Equal(new Version(0, 4, 1), UpdateService.ParseTagVersion(" 0.4.1 "));
        Assert.Null(UpdateService.ParseTagVersion("release-latest"));
        Assert.Null(UpdateService.ParseTagVersion(null));
    }

    [Fact]
    public async Task CheckReportsAvailableForNewerRelease()
    {
        var service = CreateService(CreateFeed("v0.4.1"));

        var result = await service.CheckAsync(new Version(0, 3, 0), manual: true);

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.NotNull(result.Release);
        Assert.Equal("v0.4.1", service.AvailableRelease?.Tag);
        Assert.NotNull(service.LastCheckUtc);
        Assert.Null(service.LastError);
        Assert.False(service.IsChecking);
    }

    [Fact]
    public async Task CheckReportsUpToDateForSameVersion()
    {
        var service = CreateService(CreateFeed("v0.4.1"));

        var result = await service.CheckAsync(new Version(0, 4, 1), manual: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(service.AvailableRelease);
        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task CheckReportsFailureForHttpError()
    {
        var service = CreateService(CreateFeed("v0.4.1"), HttpStatusCode.InternalServerError);

        var result = await service.CheckAsync(new Version(0, 3, 0), manual: true);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.False(string.IsNullOrWhiteSpace(service.LastError));
    }

    [Fact]
    public async Task AutoCheckFailureIsSilent()
    {
        var service = CreateService("{ not json");

        var result = await service.CheckAsync(new Version(0, 3, 0), manual: false);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(string.Empty, result.Message);
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public async Task CheckReturnsBusyWhileAnotherCheckIsRunning()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateFeed("v0.4.1"))
            };
        });
        var service = new UpdateService(new HttpClient(handler), "https://feed.test/latest.json");

        var first = service.CheckAsync(new Version(0, 3, 0), manual: false);
        var second = await service.CheckAsync(new Version(0, 3, 0), manual: true);

        Assert.Equal(UpdateCheckStatus.Busy, second.Status);

        gate.SetResult(true);
        var firstResult = await first;

        Assert.Equal(UpdateCheckStatus.Available, firstResult.Status);
    }

    [Fact]
    public async Task DownloadWritesPackageWhenSizeAndDigestMatch()
    {
        var payload = CreatePayload(4096);
        var tag = "v-download-ok-" + Guid.NewGuid().ToString("N");
        var release = CreateDownloadRelease(tag, payload);
        var service = CreateDownloadService(payload);
        var progress = new RecordingProgress();

        var path = await service.DownloadAsync(release, progress);

        try
        {
            Assert.StartsWith(
                Path.Combine(UpdateInstaller.WorkRoot, "update-" + tag + "-"),
                Path.GetDirectoryName(path)!);
            Assert.True(File.Exists(path));
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.Equal(1d, progress.Values[^1]);
            Assert.True(progress.Values.Count > 1);
            for (var i = 1; i < progress.Values.Count; i++)
            {
                Assert.True(progress.Values[i] >= progress.Values[i - 1]);
            }
        }
        finally
        {
            DeletePackageDirectory(path);
        }
    }

    [Fact]
    public async Task DownloadRejectsContentLengthMismatchAndDeletesPackage()
    {
        var payload = CreatePayload(1024);
        var tag = "v-download-size-" + Guid.NewGuid().ToString("N");
        var release = CreateDownloadRelease(tag, payload, size: payload.Length + 1);
        var service = CreateDownloadService(payload);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(release));

        Assert.Contains("크기", error.Message);
        AssertNoPackageDirectory(tag);
    }

    [Fact]
    public async Task DownloadRejectsDigestMismatchAndDeletesPackage()
    {
        var payload = CreatePayload(2048);
        var tag = "v-download-digest-" + Guid.NewGuid().ToString("N");
        var release = CreateDownloadRelease(tag, payload, digest: "sha256:" + new string('0', 64));
        var service = CreateDownloadService(payload);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(release));

        Assert.Contains("체크섬", error.Message);
        AssertNoPackageDirectory(tag);
    }

    [Fact]
    public async Task DownloadCancellationDeletesPartialPackage()
    {
        var payload = CreatePayload(1024);
        var tag = "v-download-cancel-" + Guid.NewGuid().ToString("N");
        var release = CreateDownloadRelease(tag, payload);
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });
        var service = new UpdateService(new HttpClient(handler), "https://feed.test/latest.json");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync(release, cancellationToken: cancellation.Token));

        AssertNoPackageDirectory(tag);
    }

    [Fact]
    public async Task DownloadRejectsMalformedDigest()
    {
        var release = CreateDownloadRelease("v-bad-digest", CreatePayload(16), digest: "md5:abc");
        var service = CreateDownloadService(Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(release));
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    private static UpdateRelease CreateDownloadRelease(
        string tag,
        byte[] payload,
        long? size = null,
        string? digest = null)
    {
        return new UpdateRelease
        {
            Tag = tag,
            AssetName = UpdateService.PortableAssetName,
            AssetUrl = "https://example.test/" + tag + ".zip",
            AssetSize = size ?? payload.Length,
            AssetDigest = digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()
        };
    }

    private static UpdateService CreateDownloadService(byte[] payload)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        }));
        return new UpdateService(new HttpClient(handler), "https://feed.test/latest.json");
    }

    private static void DeletePackageDirectory(string packagePath)
    {
        var directory = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertNoPackageDirectory(string tag)
    {
        if (!Directory.Exists(UpdateInstaller.WorkRoot))
        {
            return;
        }

        Assert.Empty(Directory.GetDirectories(UpdateInstaller.WorkRoot, "update-" + tag + "-*"));
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value)
        {
            Values.Add(value);
        }
    }

    private static UpdateService CreateService(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        }));
        return new UpdateService(new HttpClient(handler), "https://feed.test/latest.json");
    }

    private static string CreateFeed(
        string tag,
        string assetName = UpdateService.PortableAssetName,
        string? digest = Digest,
        long size = 1024)
    {
        var digestProperty = digest is null
            ? string.Empty
            : $"\"digest\": \"{digest}\",";

        return $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Release {{tag}}",
          "body": "notes",
          "html_url": "https://example.test/releases/{{tag}}",
          "assets": [
            {
              "name": "{{assetName}}",
              "browser_download_url": "https://example.test/{{assetName}}",
              "size": {{size}},
              {{digestProperty}}
              "content_type": "application/zip"
            }
          ]
        }
        """;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _send(request, cancellationToken);
        }
    }
}
