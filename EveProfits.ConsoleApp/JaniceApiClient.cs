using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EveProfits.ConsoleApp;

public sealed record JaniceAppraisal(
    decimal totalBuyPrice,
    IEnumerable<JaniceItemAppraisal> items);

public sealed record JaniceItemAppraisal(
    string type,
    decimal amount,
    decimal volume,
    decimal buyPrice,
    decimal sellPrice);

internal sealed record JaniceSummaryPrices(
    decimal totalBuyPrice,
    decimal totalSplitPrice,
    decimal totalSellPrice);

internal sealed record JanicePrices(
    decimal buyPrice,
    decimal sellPrice,
    decimal buyPriceTotal,
    decimal sellPriceTotal);

internal sealed record JaniceItem(
    long amount,
    decimal totalVolume,
    JanicePrices effectivePrices,
    JanicePrices immediatePrices,
    JaniceItemType itemType);

internal sealed record JaniceItemType(
    string name,
    decimal volume);

internal sealed record JaniceResponse(
    string input,
    JaniceSummaryPrices effectivePrices,
    JaniceItem[] items);

public sealed record Price(string Type, decimal BuyPrice, decimal SellPrice)
{
    public decimal SplitPrice => (BuyPrice + SellPrice) / 2m;
}

public interface IJaniceApiClient
{
    ValueTask<IEnumerable<Price>> GetAllOrePricesAsync();
    ValueTask<JaniceAppraisal> GetAppraisalByIdAsync(string id);
}

public sealed class JaniceApiClient : IJaniceApiClient
{
    private const string ApiKey = "G9KwKq3465588VPd6747t95Zh94q3W2E";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly string[] _allOres = new[]
    {
        "Compressed Bitumens",
        "Compressed Brimful Bitumens",
        "Compressed Glistening Bitumens",
        "Compressed Zeolites",
        "Compressed Brimful Zeolites",
        "Compressed Glistening Zeolites",
        "Compressed Coesite",
        "Compressed Brimful Coesite",
        "Compressed Glistening Coesite",
        "Compressed Sylvite",
        "Compressed Brimful Sylvite",
        "Compressed Glistening Sylvite",
        "Compressed Veldspar",
        "Compressed Dense Veldspar",
        "Compressed Concentrated Veldspar"
    };

    public JaniceApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async ValueTask<IEnumerable<Price>> GetAllOrePricesAsync()
    {
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ApiKey", ApiKey);

        var ores = string.Join('\n', _allOres);
        using var content = new StringContent(ores);

        var response = await client.PostAsync("https://janice.e-351.com/api/rest/v2/pricer?market=2", content)
            .ConfigureAwait(false);

        var readContent = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);

        var janiceResponse = JsonSerializer.Deserialize<IEnumerable<JaniceItem>>(readContent);
        if (janiceResponse == null)
            throw new InvalidOperationException("Could not deserialize Janice response.");

        return janiceResponse.Select(x => new Price(x.itemType.name, x.immediatePrices.buyPrice, x.immediatePrices.sellPrice))
            .ToList();
    }

    public async ValueTask<JaniceAppraisal> GetAppraisalByIdAsync(string id)
    {
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ApiKey", ApiKey);

        var response = await client.GetAsync($"https://janice.e-351.com/api/rest/v2/appraisal/{id}")
            .ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);

        var janiceResponse = JsonSerializer.Deserialize<JaniceResponse>(content);
        if (janiceResponse == null)
            throw new InvalidOperationException("Could not deserialize Janice response.");

        var items = janiceResponse.items.Select(item => new JaniceItemAppraisal(
            item.itemType.name,
            item.amount,
            item.totalVolume,
            item.effectivePrices.buyPriceTotal,
            item.effectivePrices.sellPriceTotal)).ToList();

        return new JaniceAppraisal(janiceResponse.effectivePrices.totalBuyPrice, items);
    }
}

public sealed class CachedJaniceApiClient : IJaniceApiClient
{
    private const string FileNamePrefix = "janice";
    private readonly IJaniceApiClient _client;

    public CachedJaniceApiClient(IJaniceApiClient client)
    {
        _client = client;
    }

    public async ValueTask<IEnumerable<Price>> GetAllOrePricesAsync()
    {
        var fileName = $"{FileNamePrefix}_all_{DateTime.UtcNow.Year}{DateTime.UtcNow.Month}{DateTime.UtcNow.Day}{DateTime.UtcNow.Hour}";
        if (File.Exists(fileName))
        {
            var prices = JsonSerializer.Deserialize<IEnumerable<Price>>(await File.ReadAllTextAsync(fileName).ConfigureAwait(false));
            if (prices == null)
                throw new InvalidOperationException("Could not deserialize cached prices.");

            return prices;
        }

        var newPrices = await _client.GetAllOrePricesAsync()
            .ConfigureAwait(false);

        await File.WriteAllTextAsync(fileName, JsonSerializer.Serialize(newPrices))
            .ConfigureAwait(false);

        return newPrices;
    }

    public async ValueTask<JaniceAppraisal> GetAppraisalByIdAsync(string id)
    {
        var fileName = $"{FileNamePrefix}_{id}";
        if (File.Exists(fileName))
        {
            var appraisal = JsonSerializer.Deserialize<JaniceAppraisal>(await File.ReadAllTextAsync(fileName).ConfigureAwait(false));
            if (appraisal == null)
                throw new InvalidOperationException("Could not deserialize cached janice item.");

            return appraisal;
        }

        var newAppraisal = await _client.GetAppraisalByIdAsync(id)
            .ConfigureAwait(false);

        await File.WriteAllTextAsync(fileName, JsonSerializer.Serialize(newAppraisal))
            .ConfigureAwait(false);

        return newAppraisal;
    }
}
