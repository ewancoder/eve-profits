using System;
using System.Linq;
using EveProfits.ConsoleApp;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("=== EVE Profits ===");

var services = new ServiceCollection();
services.AddHttpClient();
services.AddTransient<IPushXApiClient, PushXApiClient>();
services.Decorate<IPushXApiClient, CachedPushXApiClient>();
var container = services.BuildServiceProvider();

while (true)
{
    var input = ReadAddBuybackCommand();

    var pushx = container.GetRequiredService<IPushXApiClient>();
    var price = await pushx.GetPriceAsync("Inder", "Jita", 150_000, 3_000_000_000, default)
        .ConfigureAwait(false);

    Console.WriteLine($"Price: {price}");
}

static AddBuyback ReadAddBuybackCommand()
{
    var input = Console.ReadLine()?.Split(' ');
    if (input == null || input.Count() != 2)
        throw new InvalidOperationException("Input is invalid.");

    return new AddBuyback(
        Convert.ToInt64(input[0]),
        input[1],
        DateTime.UtcNow);
}

public sealed record AddBuyback(
    long Price,
    string Janice,
    DateTime BoughtAt);
