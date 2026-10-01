namespace ErpApp.Domain.Common;

/// <summary>
/// Phase 62 -- a rupee amount in English words, grouped the way Nepal counts: hundreds, thousands,
/// lakhs (1,00,000), crores (1,00,00,000) and arabs (1,00,00,00,000), never millions. A tax invoice
/// in the VAT Rules' Schedule 5 format states its total in words, and so does the till's receipt.
///
/// <para>Paisa are read as a second number ("and Twenty Paisa"). The amount is rounded to the paisa
/// first, away from zero, which is the scale every till figure is already stored at.</para>
/// </summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens =
        ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    /// <summary>Largest group first. Each divides what is left after the larger ones.</summary>
    private static readonly (long Size, string Name)[] Groups =
    [
        (1_00_00_00_000, "Arab"),
        (1_00_00_000, "Crore"),
        (1_00_000, "Lakh"),
        (1_000, "Thousand"),
        (100, "Hundred"),
    ];

    /// <summary>"Rupees Six Hundred Thirty Three Only"; "Rupees Twelve and Fifty Paisa Only".</summary>
    public static string Rupees(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "An amount in words is never negative.");
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = (long)decimal.Truncate(rounded);
        var paisa = (int)((rounded - rupees) * 100m);

        var words = $"Rupees {Words(rupees)}";

        if (paisa > 0)
        {
            words += $" and {Words(paisa)} Paisa";
        }

        return words + " Only";
    }

    private static string Words(long number)
    {
        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>();
        var rest = number;

        foreach (var (size, name) in Groups)
        {
            if (rest < size)
            {
                continue;
            }

            // An arab count above 99 reads as words in its own right ("One Hundred Arab").
            parts.Add($"{Words(rest / size)} {name}");
            rest %= size;
        }

        if (rest > 0)
        {
            parts.Add(UnderHundred((int)rest));
        }

        return string.Join(' ', parts);
    }

    private static string UnderHundred(int number) =>
        number < 20
            ? Ones[number]
            : number % 10 == 0
                ? Tens[number / 10]
                : $"{Tens[number / 10]} {Ones[number % 10]}";
}
