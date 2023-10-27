using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using EveProfits.ConsoleApp;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("=== EVE Profits ===");

var services = new ServiceCollection();
services.AddHttpClient();
services.AddTransient<IPushXApiClient, PushXApiClient>();
services.Decorate<IPushXApiClient, CachedPushXApiClient>();
services.AddTransient<IJaniceApiClient, JaniceApiClient>();
services.Decorate<IJaniceApiClient, CachedJaniceApiClient>();
services.AddTransient<Storage>();
var container = services.BuildServiceProvider();

var pushx = container.GetRequiredService<IPushXApiClient>();
var janice = container.GetRequiredService<IJaniceApiClient>();
var storage = container.GetRequiredService<Storage>();

var allOre = new Dictionary<string, OreInfo>();

while (true)
{
    var input = ReadAddBuybackCommand();
    if (input != null)
        storage.AddBuyback(input);

    var price = await pushx.GetPriceAsync("Inder", "Jita", 150_000, 3_000_000_000, default)
        .ConfigureAwait(false);
    _ = price;

    allOre.Clear();
    foreach (var @event in storage.GetAllEvents())
    {
        if (@event.addBuyback != null)
        {
            var appraisal = await janice.GetAppraisalByIdAsync(@event.addBuyback.Janice)
                .ConfigureAwait(false);

            var boughtFor = @event.addBuyback.Price;
            var jitaBuy = appraisal.totalBuyPrice;
            var boughtForPercentage = 100m * boughtFor / jitaBuy;

            foreach (var item in appraisal.items)
            {
                var itemBoughtFor = item.buyPrice * boughtForPercentage / 100m;

                if (!allOre.ContainsKey(item.type))
                    allOre.Add(item.type, OreInfo.From(item, itemBoughtFor));
                else
                    allOre[item.type] = allOre[item.type].Merge(item, itemBoughtFor);
            }
        }
    }

    Console.Clear();

    foreach (var ore in allOre.Values.OrderBy(x => x.type))
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{ore.type.PadRight(40)}{Math.Floor(ore.amount).ToString().PadRight(10)}{FormatPrice(ore.totalBuyPrice).PadLeft(20)}{FormatPrice(ore.itemBoughtFor).PadLeft(20)} [ {ore.BoughtForPercentage.ToString("0.00")} ]");

        Console.ForegroundColor = ore.totalSellPrice > ore.MinimumSellPrice
            ? ConsoleColor.Green
            : ConsoleColor.Red;

        Console.Write($"{FormatPrice(Math.Ceiling(ore.MinimumSellPrice)).ToString().PadLeft(20)} - {FormatPrice(Math.Ceiling(ore.MinimumSellPriceForOne)).PadRight(10)}");

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{FormatPrice(ore.totalSellPrice)}");
    }
}

static string FormatPrice(decimal price)
{
    return Math.Floor(price).ToString("N0", new NumberFormatInfo
    {
        NumberGroupSizes = new[] { 3 },
        NumberGroupSeparator = " "
    });
}

static AddBuyback? ReadAddBuybackCommand()
{
    var input = Console.ReadLine()?.Split(' ');
    if (input == null || input.Count() != 2)
        return null;

    return new AddBuyback(
        Convert.ToDecimal(input[0]),
        input[1]);
}

public sealed record AddBuyback(
    decimal Price,
    string Janice);

// Todo: figure out small details: placing orders, adjusting orders, all the fees.
// And make sure it's possible to look ahead at what profits look like before you place it / adjust it.
public sealed record SellOre(
    string type,
    decimal amount,
    decimal totalProfit);

// Very hacky implementation to not spend time on type inference now.
public sealed record DomainEvent(
    AddBuyback? addBuyback,
    SellOre? sellOre, // For now not used in any way.
    DateTime HappenedAt);

public sealed class Storage
{
    private const string FileName = "data";

    public void AddBuyback(AddBuyback addBuyback)
    {
        if (!File.Exists(FileName))
            File.WriteAllText(FileName, "[]");

        var data = JsonSerializer.Deserialize<List<DomainEvent>>(
            File.ReadAllText(FileName));

        if (data == null)
            throw new InvalidOperationException("Could not deserialize main database.");

        data.Add(new DomainEvent(addBuyback, null, DateTime.UtcNow));

        File.WriteAllText(FileName, JsonSerializer.Serialize(data));
    }

    public List<DomainEvent> GetAllEvents()
    {
        if (!File.Exists(FileName))
            File.WriteAllText(FileName, "[]");

        var data = JsonSerializer.Deserialize<List<DomainEvent>>(
            File.ReadAllText(FileName));

        if (data == null)
            throw new InvalidOperationException("Could not deserialize main database.");

        return data;
    }
}

public sealed record OreInfo(
    string type,
    decimal amount,
    decimal totalBuyPrice,
    decimal totalSellPrice,
    decimal itemBoughtFor)
{
    public static decimal PushXPriceFor1_5Bil = 44_000_000;

    // TODO: Consider using ACTUAL sell price.
    public decimal PushXPrice => totalSellPrice * PushXPriceFor1_5Bil / 1_500_000_000m;

    public decimal BoughtForPercentage => 100m * itemBoughtFor / totalBuyPrice;

    public decimal BuyPriceForOne => totalBuyPrice / amount;
    public decimal SellPriceForOne => totalSellPrice / amount;

    public decimal MinimumSellPrice => itemBoughtFor / (1m - (4.63m / 100)) + PushXPrice;
    public decimal MinimumSellPriceForOne => MinimumSellPrice / amount;

    public OreInfo Sell(decimal amountParam)
    {
        return this with
        {
            amount = amount - amountParam,
            totalBuyPrice = totalBuyPrice - (amountParam * totalBuyPrice / amount),
            totalSellPrice = totalSellPrice - (amountParam * totalSellPrice / amount),
            itemBoughtFor = itemBoughtFor - (amountParam * itemBoughtFor / amount)
        };
    }

    public OreInfo Merge(JaniceItemAppraisal itemAppraisal, decimal itemBoughtForParam)
    {
        if (itemAppraisal.type != type)
            throw new InvalidOperationException("Could not merge different types.");

        return this with
        {
            amount = amount + itemAppraisal.volume,
            totalBuyPrice = totalBuyPrice + itemAppraisal.buyPrice,
            totalSellPrice = totalSellPrice + itemAppraisal.sellPrice,
            itemBoughtFor = itemBoughtFor + itemBoughtForParam
        };
    }

    public static OreInfo From(JaniceItemAppraisal itemAppraisal, decimal itemBoughtForParam)
    {
        return new OreInfo(itemAppraisal.type, itemAppraisal.amount, itemAppraisal.buyPrice, itemAppraisal.sellPrice, itemBoughtForParam);
    }
}
