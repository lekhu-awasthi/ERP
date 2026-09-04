namespace ErpApp.Domain.Common;

/// <summary>One row of the standard currency catalog: the ISO 4217 code, its display name and
/// its symbol. Not an entity -- see <see cref="CurrencyCatalog"/>.</summary>
public sealed record CurrencyCatalogEntry(string Code, string Name, string Symbol);

/// <summary>
/// The fixed, product-wide list of currencies a tenant may activate, mirroring the reference
/// product's "Add New Currency" dialog: a Currency picker over a standard catalog, whose choice
/// pre-fills the editable Name and Symbol (confirmed live 2026-09-04 -- and confirmed there as a
/// closed picker, not free text, so a tenant cannot invent a code).
///
/// <para>A static table in Domain rather than a seeded table in the database, for the same reason
/// <see cref="BsCalendar"/> is: it is reference data that belongs to the product, not to any
/// tenant, and nothing about it varies per organization or over the life of an installation. That
/// also means adding a currency to this list is a code change with a test, never a migration.</para>
///
/// <para><b><see cref="BaseCode"/> is not merely the first entry -- it is load-bearing.</b> Every
/// amount this system posts to the general ledger is denominated in it (see
/// <see cref="ExchangeRates"/>), every document's ExchangeRate is quoted *to* it, and a tenant's
/// NPR currency row can never be deactivated or deleted. The product is Nepal-only by scope
/// (docs/product-requirements.md), so this is a constant rather than a per-tenant setting; a
/// tenant-selectable base currency would change what every historical GlLine means and is
/// deliberately not offered.</para>
/// </summary>
public static class CurrencyCatalog
{
    /// <summary>The reporting/base currency. See the class doc comment -- this is a product
    /// constant, not a tenant setting.</summary>
    public const string BaseCode = "NPR";

    private static readonly CurrencyCatalogEntry[] Entries =
    [
        new("NPR", "Nepalese Rupee", "Rs."),
        new("USD", "US Dollar", "$"),
        new("EUR", "Euro", "€"),
        new("GBP", "Pound Sterling", "£"),
        new("INR", "Indian Rupee", "₹"),
        new("CNY", "Chinese Yuan", "¥"),
        new("JPY", "Japanese Yen", "¥"),
        new("AUD", "Australian Dollar", "A$"),
        new("CAD", "Canadian Dollar", "C$"),
        new("CHF", "Swiss Franc", "CHF"),
        new("SGD", "Singapore Dollar", "S$"),
        new("HKD", "Hong Kong Dollar", "HK$"),
        new("AED", "UAE Dirham", "د.إ"),
        new("SAR", "Saudi Riyal", "﷼"),
        new("QAR", "Qatari Riyal", "﷼"),
        new("KWD", "Kuwaiti Dinar", "د.ك"),
        new("MYR", "Malaysian Ringgit", "RM"),
        new("KRW", "South Korean Won", "₩"),
        new("THB", "Thai Baht", "฿"),
        new("BDT", "Bangladeshi Taka", "৳"),
        new("LKR", "Sri Lankan Rupee", "Rs"),
        new("BTN", "Bhutanese Ngultrum", "Nu."),
        new("PKR", "Pakistani Rupee", "₨"),
        new("ZAR", "South African Rand", "R"),
        new("RUB", "Russian Ruble", "₽"),
    ];

    /// <summary>Catalog order, base currency first, then alphabetical by code -- the order the
    /// picker renders.</summary>
    public static IReadOnlyList<CurrencyCatalogEntry> All { get; } =
        Entries.Take(1).Concat(Entries.Skip(1).OrderBy(x => x.Code, StringComparer.Ordinal)).ToArray();

    /// <summary>The base currency's own catalog entry. Never null -- guarded by a Domain test.</summary>
    public static CurrencyCatalogEntry Base { get; } = Entries[0];

    public static bool Contains(string code) => Find(code) is not null;

    public static CurrencyCatalogEntry? Find(string code) =>
        Entries.SingleOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for the one code every tenant always has and can never remove.</summary>
    public static bool IsBase(string code) => string.Equals(code, BaseCode, StringComparison.OrdinalIgnoreCase);
}
