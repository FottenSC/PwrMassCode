using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Linq; // added for client-side filtering

namespace Community.PowerToys.Run.Plugin.PwrMassCode;

internal sealed class MassCodeClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MassCodeClient(HttpClient http)
    {
        _http = http;
    }

    public static MassCodeClient Create(string? baseUrl)
    {
        const string fallback = "http://localhost:4321";
        Uri uri;
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out uri))
        {
            uri = new Uri(fallback);
        }

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            Proxy = null,
            UseProxy = false,
        };

        var http = new HttpClient(handler)
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PwrMassCode/1.0 (+https://github.com/)");
        return new MassCodeClient(http);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);
    }

    private static async Task<string> GetPreviewAsync(HttpContent content, CancellationToken ct)
    {
        var bytes = await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var len = Math.Min(bytes.Length, 1024);
        var text = Encoding.UTF8.GetString(bytes, 0, len);
        return bytes.Length > 1024 ? text + "…" : text;
    }

    // Backward compatible method signature – defaults to including favorites
    public Task<IReadOnlyList<Snippet>> GetSnippetsAsync(CancellationToken ct)
        => GetSnippetsAsync(excludeFavorites: false, ct);

    // New overload allowing callers to exclude favorited snippets
    public async Task<IReadOnlyList<Snippet>> GetSnippetsAsync(bool excludeFavorites, CancellationToken ct)
    {
        // API only reliably filters when isFavorites=1; perform favorites exclusion client-side instead
        const string url = "snippets?isDeleted=0";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode == false){
            var preview = await GetPreviewAsync(resp.Content, ct).ConfigureAwait(false);
            throw new HttpRequestException($"massCode GET /{url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {preview}");
        }
        var data = await ReadJsonAsync<List<Snippet>>(resp.Content, ct).ConfigureAwait(false) ?? [];

        if (excludeFavorites)
        {
            // Return only non-favorited snippets
            return data.Where(s => s != null && s.IsFavorite == false).ToList();
        }

        return data;
    }
}
