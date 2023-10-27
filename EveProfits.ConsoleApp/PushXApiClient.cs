using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EveProfits.ConsoleApp;

internal sealed record PushXQuote(
    long PriceNormal);

public interface IPushXApiClient
{
    ValueTask<long> GetPriceAsync(
        string startSystemName,
        string endSystemName,
        int volume,
        long collateral,
        CancellationToken cancellationToken);
}

public sealed class PushXApiClient : IPushXApiClient
{
    private const string ApiClient = "EveProfits";
    private readonly IHttpClientFactory _httpClientFactory;

    public PushXApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async ValueTask<long> GetPriceAsync(string startSystemName, string endSystemName, int volume, long collateral, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();

        var response = await client.GetAsync($"https://api.pushx.net/api/quote/json?startSystemName={startSystemName}&endSystemName={endSystemName}&volume={volume}&collateral={collateral}&apiClient={ApiClient}", cancellationToken)
            .ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);

        var quote = JsonSerializer.Deserialize<PushXQuote>(content);
        if (quote == null)
            throw new InvalidOperationException("Could not deserialize PushX API response.");

        return quote.PriceNormal;
    }
}

public sealed class CachedPushXApiClient : IPushXApiClient
{
    private const string FileName = "pushxCache";
    private static readonly int[] _volumes = new[] { 12500, 62500, 360000, 848000, 1126500 };

    private readonly IPushXApiClient _client;

    public CachedPushXApiClient(IPushXApiClient client)
    {
        _client = client;
    }

    public async ValueTask<long> GetPriceAsync(string startSystemName, string endSystemName, int volume, long collateral, CancellationToken cancellationToken)
    {
        volume = _volumes.First(x => x > volume);

        if (!File.Exists(FileName))
            await File.WriteAllTextAsync(FileName, "{}", cancellationToken)
                .ConfigureAwait(false);

        var cache = JsonSerializer.Deserialize<Dictionary<string, long>>(await File.ReadAllTextAsync(FileName, cancellationToken).ConfigureAwait(false));
        if (cache == null)
            throw new InvalidOperationException("Could not deserialize pushx cache.");

        var key = GetKey(startSystemName, endSystemName, volume, collateral);

        if (cache.TryGetValue(key, out var cached))
            return Convert.ToInt64(cached);

        var value = await _client.GetPriceAsync(startSystemName, endSystemName, volume, collateral, cancellationToken)
            .ConfigureAwait(false);

        cache[key] = value;
        await File.WriteAllTextAsync(FileName, JsonSerializer.Serialize(cache), cancellationToken)
            .ConfigureAwait(false);

        return value;
    }

    private string GetKey(string startSystemName, string endSystemName, int volume, long collateral)
    {
        return $"{startSystemName}_{endSystemName}_{volume}_{collateral}";
    }
}
