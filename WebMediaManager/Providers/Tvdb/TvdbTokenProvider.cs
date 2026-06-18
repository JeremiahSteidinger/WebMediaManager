using System.Net.Http.Json;
using WebMediaManager.Core.Providers;
using WebMediaManager.Services;

namespace WebMediaManager.Providers.Tvdb;

/// <summary>
/// Singleton cache for the TVDB v4 bearer token (valid ~1 month). Logs in lazily using the key/PIN from
/// settings and re-logs-in on demand when a request reports the token expired (401).
/// </summary>
public sealed class TvdbTokenProvider(IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory)
{
    public const string LoginClientName = "tvdb-login";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _token is not null)
        {
            return _token;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _token is not null)
            {
                return _token;
            }
            _token = await LoginAsync(ct);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _token = null;

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        string? key;
        string? pin;
        using (var scope = scopeFactory.CreateScope())
        {
            var settings = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync(ct);
            key = settings.Providers.TvdbApiKey;
            pin = settings.Providers.TvdbPin;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new MetadataException("TVDB API key is not set. Add it in Settings → Providers.");
        }

        var client = httpFactory.CreateClient(LoginClientName);
        object body = string.IsNullOrWhiteSpace(pin)
            ? new { apikey = key }
            : new { apikey = key, pin };

        using var response = await client.PostAsJsonAsync("login", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MetadataException($"TVDB login failed ({(int)response.StatusCode}). Check the key/PIN in Settings → Providers.");
        }

        var payload = await response.Content.ReadFromJsonAsync<TvdbLoginResponse>(ct);
        var token = payload?.Data?.Token;
        if (string.IsNullOrEmpty(token))
        {
            throw new MetadataException("TVDB login returned no token.");
        }

        return token;
    }
}
