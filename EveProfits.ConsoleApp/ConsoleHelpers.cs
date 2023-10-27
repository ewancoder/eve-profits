using System;
using System.Text;

namespace EveProfits.ConsoleApp;

public static class ConsoleHelpers
{
    public static ConsoleKeyInfo ReadSingleKey()
    {
        while (true)
        {
            var count = 0;
            var key = Console.ReadKey(true);

            while (Console.KeyAvailable)
            {
                count++;
                key = Console.ReadKey(true);
            }

            if (count > 0)
            {
#pragma warning disable IDE0059 // False positive.
#pragma warning disable S1854 // False positive.
                count = 0;
#pragma warning restore S1854
#pragma warning restore IDE0059
                continue;
            }

            return key;
        }
    }

    public static string ReadInput()
    {
        var sb = new StringBuilder();

        while (Console.KeyAvailable)
        {
            sb.AppendLine(Console.ReadLine());
        }

        return sb.ToString();
    }
}
