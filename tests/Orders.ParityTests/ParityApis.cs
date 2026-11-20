using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Orders.ParityTests;

public enum ApiTarget
{
    LegacyPhp,
    DotNet,
}

public sealed record ApiResponse(int Status, string? ContentType, string Body, IReadOnlyDictionary<string, string> Headers)
{
    public bool HasJson => !string.IsNullOrWhiteSpace(Body);

    public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// The two running APIs. Defaults match CI and the README:
/// legacy PHP on :8080 (php -S) and the .NET port on :5080 (dotnet run).
/// </summary>
public static class ParityApis
{
    public static readonly Uri LegacyBaseUrl = new(Env("LEGACY_BASE_URL", "http://127.0.0.1:8080"));
    public static readonly Uri DotNetBaseUrl = new(Env("DOTNET_BASE_URL", "http://127.0.0.1:5080"));

    private static readonly HttpClient Legacy = Create(LegacyBaseUrl);
    private static readonly HttpClient DotNet = Create(DotNetBaseUrl);

    public static async Task<ApiResponse> SendAsync(ApiTarget target, string method, string path, string? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await (target == ApiTarget.LegacyPhp ? Legacy : DotNet).SendAsync(request);
        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);

        return new ApiResponse(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            await response.Content.ReadAsStringAsync(),
            headers);
    }

    public static async Task<(ApiResponse Legacy, ApiResponse DotNet)> SendToBothAsync(string method, string path, string? body = null) =>
        (await SendAsync(ApiTarget.LegacyPhp, method, path, body), await SendAsync(ApiTarget.DotNet, method, path, body));

    public static async Task ResetBothAsync()
    {
        var (legacy, dotnet) = await SendToBothAsync("POST", "/api/test/reset");
        if (legacy.Status != 204 || dotnet.Status != 204)
        {
            throw new InvalidOperationException(
                $"Reset failed (legacy {legacy.Status}, .NET {dotnet.Status}). Start both APIs with ENABLE_TEST_ENDPOINTS=true / EnableTestEndpoints=true.");
        }
    }

    /// <summary>"application/json; charset=utf-8" and "application/json; charset=UTF-8" are the same.</summary>
    public static string? NormalizeContentType(string? value) =>
        value is null ? null : MediaTypeHeaderValue.Parse(value).ToString().ToLowerInvariant().Replace(" ", string.Empty);

    private static HttpClient Create(Uri baseUrl) =>
        new(new HttpClientHandler { UseProxy = false }) { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(15) };

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
