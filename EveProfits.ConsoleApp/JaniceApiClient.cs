using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace EveProfits.ConsoleApp;

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
    decimal buyPriceTotal,
    decimal sellPriceTotal);

internal sealed record JaniceItem(
    long amount,
    decimal totalVolume,
    JanicePrices effectivePrices,
    JaniceItemType itemType);

internal sealed record JaniceItemType(
    string name,
    decimal volume);

internal sealed record JaniceResponse(
    string input,
    JaniceSummaryPrices effectivePrices,
    JaniceItem[] items);

public interface IJaniceApiClient
{
    IAsyncEnumerable<JaniceItemAppraisal> GetItemAppraisalsByIdAsync(string id);
}

public sealed class JaniceApiClient : IJaniceApiClient
{
    private const string ApiKey = "G9KwKq3465588VPd6747t95Zh94q3W2E";
    private readonly IHttpClientFactory _httpClientFactory;

    public JaniceApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async IAsyncEnumerable<JaniceItemAppraisal> GetItemAppraisalsByIdAsync(string id)
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

        foreach (var item in janiceResponse.items)
        {
            var appraisal = new JaniceItemAppraisal(
                item.itemType.name,
                item.amount,
                item.totalVolume,
                item.effectivePrices.buyPriceTotal,
                item.effectivePrices.sellPriceTotal);

            yield return appraisal;
        }
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

    public async IAsyncEnumerable<JaniceItemAppraisal> GetItemAppraisalsByIdAsync(string id)
    {
        var fileName = $"{FileNamePrefix}_{id}";
        if (File.Exists(fileName))
        {
            var items = JsonSerializer.Deserialize<JaniceItemAppraisal[]>(await File.ReadAllTextAsync(fileName).ConfigureAwait(false));
            if (items == null)
                throw new InvalidOperationException("Could not deserialize cached janice item.");

            foreach (var item in items)
            {
                yield return item;
            }
        }

        var newItems = new List<JaniceItemAppraisal>();
        await foreach (var item in _client.GetItemAppraisalsByIdAsync(id))
        {
            newItems.Add(item);
        }

        await File.WriteAllTextAsync(fileName, JsonSerializer.Serialize(newItems))
            .ConfigureAwait(false);

        foreach (var item in newItems)
        {
            yield return item;
        }
    }
}
