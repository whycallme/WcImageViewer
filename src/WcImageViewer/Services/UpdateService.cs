using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WcImageViewer.Models;

namespace WcImageViewer.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
    Busy
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, UpdateRelease? Release = null);

public sealed class UpdateService
{
    public const string DefaultFeedUrl = "https://api.github.com/repos/whycallme/WcImageViewer/releases/latest";
    public const string FeedUrlEnvironmentVariable = "WC_UPDATE_FEED";
    public const string PortableAssetName = "WcImageViewer-portable-win-x64.zip";
    public const string LiteAssetName = "WcImageViewer-lite-win-x64.zip";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly Lazy<UpdateService> LazyInstance = new(() =>
        new UpdateService(new HttpClient(), ResolveFeedUrl()));

    private readonly HttpClient _httpClient;
    private readonly string _feedUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateService(HttpClient httpClient, string feedUrl)
    {
        _httpClient = httpClient;
        _feedUrl = feedUrl;
    }

    public static UpdateService Instance => LazyInstance.Value;

    public UpdateRelease? AvailableRelease { get; private set; }

    public DateTime? LastCheckUtc { get; private set; }

    public string? LastError { get; private set; }

    public bool IsChecking { get; private set; }

    public string FeedUrl => _feedUrl;

    public event EventHandler? StateChanged;

    public static string ResolveFeedUrl()
    {
        var overrideUrl = Environment.GetEnvironmentVariable(FeedUrlEnvironmentVariable);
        return string.IsNullOrWhiteSpace(overrideUrl) ? DefaultFeedUrl : overrideUrl.Trim();
    }

    public void Initialize(UpdateRelease? pending, DateTime? lastCheck)
    {
        AvailableRelease = pending;
        LastCheckUtc = lastCheck;
        LastError = null;
        RaiseStateChanged();
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version current,
        bool manual,
        CancellationToken cancellationToken = default)
    {
        if (!_gate.Wait(0))
        {
            return new UpdateCheckResult(UpdateCheckStatus.Busy, "이미 업데이트를 확인하는 중입니다.");
        }

        IsChecking = true;
        RaiseStateChanged();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _feedUrl);
                request.Headers.UserAgent.ParseAdd("WcImageViewer/" + AppVersion.Display);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

                using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"업데이트 서버 응답 오류 ({(int)response.StatusCode})");
                }

                var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                var release = ParseRelease(json, AppVersion.IsLite);
                var version = ParseTagVersion(release.Tag)
                    ?? throw new InvalidOperationException("릴리즈 버전을 해석할 수 없습니다.");

                LastCheckUtc = DateTime.UtcNow;
                LastError = null;

                if (version > current)
                {
                    AvailableRelease = release;
                    return new UpdateCheckResult(
                        UpdateCheckStatus.Available,
                        $"v{FormatVersion(version)} 사용 가능",
                        release);
                }

                AvailableRelease = null;
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, "최신 버전입니다.");
            }
            catch (Exception ex)
            {
                LastCheckUtc = DateTime.UtcNow;
                LastError = DescribeError(ex);
                return new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    manual ? LastError : string.Empty);
            }
        }
        finally
        {
            IsChecking = false;
            _gate.Release();
            RaiseStateChanged();
        }
    }

    public async Task<string> DownloadAsync(
        UpdateRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (string.IsNullOrWhiteSpace(release.AssetUrl))
        {
            throw new InvalidOperationException("업데이트 다운로드 URL이 없습니다.");
        }

        if (release.AssetSize <= 0)
        {
            throw new InvalidOperationException("업데이트 파일 크기 정보가 없습니다.");
        }

        var expectedHash = ParseSha256(release.AssetDigest);
        var directory = Path.Combine(
            UpdateInstaller.WorkRoot,
            $"update-{SanitizeTag(release.Tag)}-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "package.zip");

        Directory.CreateDirectory(directory);
        progress?.Report(0);

        try
        {
            using var response = await _httpClient
                .GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"업데이트 다운로드 오류 ({(int)response.StatusCode})");
            }

            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength != release.AssetSize)
            {
                throw new InvalidOperationException(
                    $"업데이트 파일 크기가 릴리즈 정보와 다릅니다 ({contentLength:N0} / {release.AssetSize:N0}).");
            }

            await using (var source = await response.Content
                             .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             path,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > release.AssetSize)
                    {
                        throw new InvalidOperationException("업데이트 파일이 릴리즈 크기보다 큽니다.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    progress?.Report((double)total / release.AssetSize);
                }
            }

            var fileLength = new FileInfo(path).Length;
            if (fileLength != release.AssetSize)
            {
                throw new InvalidOperationException(
                    $"업데이트 파일 크기가 일치하지 않습니다 ({fileLength:N0} / {release.AssetSize:N0}).");
            }

            var actualHash = ComputeSha256(path);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("업데이트 파일 체크섬이 일치하지 않습니다.");
            }

            progress?.Report(1);
            return path;
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public static UpdateRelease ParseRelease(string json, bool isLite)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("릴리즈 태그가 없습니다.");
        }

        var expectedAsset = isLite ? LiteAssetName : PortableAssetName;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("릴리즈 자산 목록이 없습니다.");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(ReadString(asset, "name"), expectedAsset, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = ReadString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("업데이트 다운로드 URL이 없습니다.");
            }

            if (!asset.TryGetProperty("size", out var sizeElement) ||
                !sizeElement.TryGetInt64(out var size) ||
                size <= 0)
            {
                throw new InvalidOperationException("업데이트 파일 크기 정보가 없습니다.");
            }

            var digest = ReadString(asset, "digest");
            if (!digest.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("업데이트 파일 체크섬 정보가 없습니다.");
            }

            return new UpdateRelease
            {
                Tag = tag.Trim(),
                Name = ReadString(root, "name"),
                Body = ReadString(root, "body"),
                HtmlUrl = ReadString(root, "html_url"),
                AssetName = expectedAsset,
                AssetUrl = url.Trim(),
                AssetSize = size,
                AssetDigest = digest.Trim()
            };
        }

        throw new InvalidOperationException($"릴리즈에 {expectedAsset} 파일이 없습니다.");
    }

    public static Version? ParseTagVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.Trim();
        if (trimmed[0] is 'v' or 'V')
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    public static string FormatVersion(Version version)
    {
        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string DescribeError(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "업데이트 확인 시간이 초과되었습니다.",
            HttpRequestException => "업데이트 서버에 연결할 수 없습니다.",
            JsonException => "업데이트 정보를 해석할 수 없습니다.",
            _ => exception.Message
        };
    }

    private static string ParseSha256(string digest)
    {
        const string prefix = "sha256:";
        var value = digest?.Trim() ?? string.Empty;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("업데이트 파일 체크섬 정보가 없습니다.");
        }

        var hash = value[prefix.Length..].Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("업데이트 파일 체크섬 형식이 올바르지 않습니다.");
        }

        return hash;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string SanitizeTag(string tag)
    {
        var value = string.IsNullOrWhiteSpace(tag) ? "latest" : tag.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
