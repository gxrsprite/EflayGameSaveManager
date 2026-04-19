using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

public sealed class S3CompatibleCloudStorageClient
{
    private readonly HttpClient _httpClient;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750)
    ];

    public S3CompatibleCloudStorageClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyQuery = new Dictionary<string, string>();

    public async Task UploadFileAsync(
        CloudBackendSettings backend,
        string objectKey,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var payloadHash = await ComputeHashAsync(stream, cancellationToken);
        stream.Position = 0;

        await SendRequestAsync(
            backend,
            HttpMethod.Put,
            objectKey,
            EmptyQuery,
            new StreamContent(stream),
            payloadHash,
            "application/octet-stream",
            cancellationToken);
    }

    public async Task UploadUtf8JsonAsync(
        CloudBackendSettings backend,
        string objectKey,
        string json,
        CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await using var stream = new MemoryStream(bytes, writable: false);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        await SendRequestAsync(
            backend,
            HttpMethod.Put,
            objectKey,
            EmptyQuery,
            new StreamContent(stream),
            payloadHash,
            "application/json; charset=utf-8",
            cancellationToken);
    }

    public async Task<IReadOnlyList<CloudObjectInfo>> ListObjectsAsync(
        CloudBackendSettings backend,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(
            backend,
            HttpMethod.Get,
            string.Empty,
            new Dictionary<string, string>
            {
                ["list-type"] = "2",
                ["prefix"] = prefix
            },
            null,
            EmptyPayloadHash,
            null,
            cancellationToken,
            allowNotFound: false);

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://s3.amazonaws.com/doc/2006-03-01/";

        return (document.Root?
            .Elements(ns + "Contents")
            .Select(item => new CloudObjectInfo(
                item.Element(ns + "Key")?.Value ?? string.Empty,
                long.TryParse(item.Element(ns + "Size")?.Value, out var size) ? size : 0,
                DateTimeOffset.TryParse(item.Element(ns + "LastModified")?.Value, out var value) ? value : null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray())
            ?? [];
    }

    public async Task<string?> TryDownloadUtf8StringAsync(
        CloudBackendSettings backend,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(
            backend,
            HttpMethod.Get,
            objectKey,
            EmptyQuery,
            null,
            EmptyPayloadHash,
            null,
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task DownloadFileAsync(
        CloudBackendSettings backend,
        string objectKey,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(
            backend,
            HttpMethod.Get,
            objectKey,
            EmptyQuery,
            null,
            EmptyPayloadHash,
            null,
            cancellationToken,
            allowNotFound: false);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public async Task DeleteObjectAsync(
        CloudBackendSettings backend,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(
            backend,
            HttpMethod.Delete,
            objectKey,
            EmptyQuery,
            null,
            EmptyPayloadHash,
            null,
            cancellationToken,
            allowNotFound: true);

        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        CloudBackendSettings backend,
        HttpMethod method,
        string objectKey,
        IReadOnlyDictionary<string, string> query,
        HttpContent? content,
        string payloadHash,
        string? contentType,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        ValidateBackend(backend);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var endpoint = new Uri(backend.Endpoint, UriKind.Absolute);
                var canonicalUri = BuildCanonicalUri(endpoint, backend.Bucket, objectKey);
                var canonicalQueryString = BuildCanonicalQueryString(query);
                var requestUri = new Uri(endpoint, canonicalUri + (canonicalQueryString.Length == 0 ? string.Empty : "?" + canonicalQueryString));
                var timestamp = DateTimeOffset.UtcNow;
                var amzDate = timestamp.ToString("yyyyMMdd'T'HHmmss'Z'");
                var shortDate = timestamp.ToString("yyyyMMdd");
                var region = string.IsNullOrWhiteSpace(backend.Region) ? "us-east-1" : backend.Region.Trim();
                var hostHeader = requestUri.IsDefaultPort ? requestUri.Host : $"{requestUri.Host}:{requestUri.Port}";
                var canonicalHeaders = $"host:{hostHeader}\n" +
                                       $"x-amz-content-sha256:{payloadHash}\n" +
                                       $"x-amz-date:{amzDate}\n";
                const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
                var canonicalRequest = $"{method.Method}\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
                var credentialScope = $"{shortDate}/{region}/s3/aws4_request";
                var stringToSign =
                    $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
                var signature = CreateSignature(backend.SecretAccessKey, shortDate, region, stringToSign);
                AppLogger.Info(
                    $"Cloud signing: method={method.Method}, endpoint={backend.Endpoint}, bucket={backend.Bucket}, objectKey={objectKey}, canonicalUri={canonicalUri}, canonicalQuery={canonicalQueryString}, host={hostHeader}, scope={credentialScope}, amzDate={amzDate}, payloadHash={payloadHash}");

                var request = new HttpRequestMessage(method, requestUri);
                if (content is not null)
                {
                    request.Content = content;
                    if (!string.IsNullOrWhiteSpace(contentType))
                    {
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    }
                }

                request.Headers.Host = hostHeader;
                request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
                request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
                request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    $"AWS4-HMAC-SHA256 Credential={backend.AccessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}");

                AppLogger.Info($"Cloud request: {method.Method} {requestUri} (attempt {attempt + 1})");
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return response;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var requestId = response.Headers.TryGetValues("x-amz-request-id", out var requestIds)
                        ? requestIds.FirstOrDefault()
                        : string.Empty;
                    var extendedRequestId = response.Headers.TryGetValues("x-amz-id-2", out var extendedIds)
                        ? extendedIds.FirstOrDefault()
                        : string.Empty;
                    AppLogger.Error(
                        $"Cloud request failed: {method.Method} {requestUri} -> {(int)response.StatusCode} {response.StatusCode}. request-id={requestId}, id-2={extendedRequestId}. Body: {body}");
                    if (ContainsSignatureDoesNotMatch(body))
                    {
                        AppLogger.Error(
                            "Cloud signature mismatch diagnostics: " +
                            $"method={method.Method}, requestUri={requestUri}, canonicalUri={canonicalUri}, canonicalQuery={canonicalQueryString}, " +
                            $"host={hostHeader}, signedHeaders={signedHeaders}, credentialScope={credentialScope}, amzDate={amzDate}, payloadHash={payloadHash}, " +
                            $"canonicalRequest={canonicalRequest}, stringToSign={stringToSign}");
                    }
                    response.Dispose();
                    throw new InvalidOperationException(
                        $"Cloud request failed for '{objectKey}'. Status {(int)response.StatusCode}: {body}");
                }

                return response;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                AppLogger.Error(
                    $"Cloud request retrying: method={method.Method}, endpoint={backend.Endpoint}, bucket={backend.Bucket}, objectKey={objectKey}, attempt={attempt + 1}",
                    ex);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
            catch (Exception ex)
            {
                AppLogger.Error(
                    $"Cloud request exception: method={method.Method}, endpoint={backend.Endpoint}, bucket={backend.Bucket}, objectKey={objectKey}",
                    ex);
                throw;
            }
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 4,
            EnableMultipleHttp2Connections = false
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static bool ShouldRetry(Exception ex, int attempt)
    {
        if (attempt >= RetryDelays.Length)
        {
            return false;
        }

        return ex is HttpRequestException || ex is IOException || ex.InnerException is IOException;
    }

    private static string BuildCanonicalUri(Uri endpoint, string bucket, string objectKey)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(endpoint.AbsolutePath) && endpoint.AbsolutePath != "/")
        {
            segments.AddRange(endpoint.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        segments.Add(bucket);
        segments.AddRange(objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries));

        return "/" + string.Join("/", segments.Select(EncodeUriPathSegment));
    }

    private static string BuildCanonicalQueryString(IReadOnlyDictionary<string, string> query)
    {
        return string.Join(
            "&",
            query
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
    }

    private static string EncodeUriPathSegment(string value)
    {
        return Uri.EscapeDataString(value).Replace("%2F", "/");
    }

    private static async Task<string> ComputeHashAsync(Stream content, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(content, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string CreateSignature(string secretAccessKey, string shortDate, string region, string stringToSign)
    {
        var signingKey = HmacSha256(
            HmacSha256(
                HmacSha256(
                    HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secretAccessKey}"), shortDate),
                    region),
                "s3"),
            "aws4_request");

        return Convert.ToHexStringLower(HmacSha256(signingKey, stringToSign));
    }

    private static byte[] HmacSha256(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static void ValidateBackend(CloudBackendSettings backend)
    {
        if (!string.Equals(backend.Type, "S3", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported cloud backend type: {backend.Type}");
        }

        if (string.IsNullOrWhiteSpace(backend.Endpoint) ||
            string.IsNullOrWhiteSpace(backend.Bucket) ||
            string.IsNullOrWhiteSpace(backend.AccessKeyId) ||
            string.IsNullOrWhiteSpace(backend.SecretAccessKey))
        {
            throw new InvalidOperationException("Cloud backend configuration is incomplete.");
        }
    }

    private static bool ContainsSignatureDoesNotMatch(string body)
    {
        return body.Contains("SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase);
    }

    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
}
