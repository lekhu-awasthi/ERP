namespace ErpApp.Domain.Common;

/// <summary>
/// Phase 62 -- a rupee amount in English words, grouped the way Nepal counts: hundreds, thousands,
/// lakhs (1,00,000), crores (1,00,00,000) and arabs (1,00,00,00,000), never millions. A tax invoice
/// in the VAT Rules' Schedule 5 format states its total in words, and so does the till's receipt.
///
/// <para>Paisa are read as a second number ("and Twenty Paisa"). The amount is rounded to the paisa
/// first, away from zero, which is the scale every till figure is already stored at.</para>
///
/// <para>Phase 79 -- and every other currency in <see cref="CurrencyCatalog"/> (<see cref="For"/>), named as the
/// vendor's words name it ("… Nepalese Rupees", read live 2026-10-11). A foreign currency counts in millions and
/// billions, as its own documents do; the Indian rupee keeps lakhs and crores, as the rupee does. A currency with no
/// minor unit (yen, won) reads a whole amount only, and prints no words for a fractional one rather than words that
/// disagree with the figure.</para>
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
    private static readonly (long Size, string Name)[] NepaliGroups =
    [
        (1_00_00_00_000, "Arab"),
        (1_00_00_000, "Crore"),
        (1_00_000, "Lakh"),
        (1_000, "Thousand"),
        (100, "Hundred"),
    ];

    /// <summary>Phase 79 -- the international grouping a foreign currency's own documents count in.</summary>
    private static readonly (long Size, string Name)[] InternationalGroups =
    [
        (1_000_000_000_000, "Trillion"),
        (1_000_000_000, "Billion"),
        (1_000_000, "Million"),
        (1_000, "Thousand"),
        (100, "Hundred"),
    ];

    /// <summary>Phase 79 -- how one currency reads: its major and minor units (plural, as "Rupees" already reads for
    /// one rupee), how many minor digits it has, and whether it counts in lakhs.</summary>
    private sealed record CurrencyWords(string Major, string? Minor, int MinorDigits = 2, bool Lakhs = false);

    /// <summary>One row per <see cref="CurrencyCatalog"/> entry other than the base, which <see cref="Rupees"/>
    /// reads. <c>AmountInWordsTests</c> holds the two lists to each other.</summary>
    private static readonly Dictionary<string, CurrencyWords> Currencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = new("US Dollars", "Cents"),
        ["EUR"] = new("Euros", "Cents"),
        ["GBP"] = new("Pounds Sterling", "Pence"),
        ["INR"] = new("Indian Rupees", "Paise", Lakhs: true),
        ["CNY"] = new("Chinese Yuan", "Fen"),
        ["JPY"] = new("Japanese Yen", null, MinorDigits: 0),
        ["AUD"] = new("Australian Dollars", "Cents"),
        ["CAD"] = new("Canadian Dollars", "Cents"),
        ["CHF"] = new("Swiss Francs", "Rappen"),
        ["SGD"] = new("Singapore Dollars", "Cents"),
        ["HKD"] = new("Hong Kong Dollars", "Cents"),
        ["AED"] = new("UAE Dirhams", "Fils"),
        ["SAR"] = new("Saudi Riyals", "Halalas"),
        ["QAR"] = new("Qatari Riyals", "Dirhams"),
        ["KWD"] = new("Kuwaiti Dinars", "Fils", MinorDigits: 3),
        ["MYR"] = new("Malaysian Ringgit", "Sen"),
        ["KRW"] = new("South Korean Won", null, MinorDigits: 0),
        ["THB"] = new("Thai Baht", "Satang"),
        ["BDT"] = new("Bangladeshi Taka", "Poisha"),
        ["LKR"] = new("Sri Lankan Rupees", "Cents"),
        ["BTN"] = new("Bhutanese Ngultrum", "Chhertum"),
        ["PKR"] = new("Pakistani Rupees", "Paisa"),
        ["ZAR"] = new("South African Rand", "Cents"),
        ["RUB"] = new("Russian Rubles", "Kopecks"),
    };

    /// <summary>The currency codes <see cref="For"/> can read, base included.</summary>
    public static IReadOnlyCollection<string> SupportedCurrencies =>
        [CurrencyCatalog.BaseCode, .. Currencies.Keys];

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

        var words = $"Rupees {Words(rupees, NepaliGroups)}";

        if (paisa > 0)
        {
            words += $" and {Words(paisa, NepaliGroups)} Paisa";
        }

        return words + " Only";
    }

    /// <summary>
    /// Phase 79 -- the amount in words in its own currency: the rupee exactly as <see cref="Rupees"/> reads it, and
    /// "US Dollars One Thousand Two Hundred and Fifty Cents Only" for a foreign one. Null for a negative amount, a
    /// code outside the catalogue, or a fraction of a currency that has none -- no words rather than wrong ones.
    /// </summary>
    public static string? For(decimal amount, string currencyCode)
    {
        if (amount < 0m)
        {
            return null;
        }

        if (CurrencyCatalog.IsBase(currencyCode))
        {
            return Rupees(amount);
        }

        if (!Currencies.TryGetValue(currencyCode, out var currency))
        {
            return null;
        }

        var rounded = decimal.Round(amount, currency.MinorDigits, MidpointRounding.AwayFromZero);
        if (rounded != amount && currency.MinorDigits == 0)
        {
            return null;
        }

        var groups = currency.Lakhs ? NepaliGroups : InternationalGroups;
        var major = (long)decimal.Truncate(rounded);
        var minor = (long)((rounded - major) * Pow10(currency.MinorDigits));

        var words = $"{currency.Major} {Words(major, groups)}";
        if (minor > 0)
        {
            words += $" and {Words(minor, groups)} {currency.Minor}";
        }

        return words + " Only";
    }

    private static decimal Pow10(int digits)
    {
        var result = 1m;
        for (var i = 0; i < digits; i++)
        {
            result *= 10m;
        }

        return result;
    }

    private static string Words(long number, (long Size, string Name)[] groups)
    {
        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>();
        var rest = number;

        foreach (var (size, name) in groups)
        {
            if (rest < size)
            {
                continue;
            }

            // A top group count above 99 reads as words in its own right ("One Hundred Arab").
            parts.Add($"{Words(rest / size, groups)} {name}");
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
