using ErpApp.Domain.Common;

namespace ErpApp.Domain.Tenancy;

/// <summary>
/// A currency the tenant has activated, i.e. one row of Organization &gt; Features &gt; Multiple
/// Currency's Code/Name/Symbol table (confirmed live 2026-09-04). Every tenant has exactly one of
/// these from the moment its Organization is created -- the base currency
/// (<see cref="CurrencyCatalog.BaseCode"/>) -- and a tenant with the MultiCurrency entitlement can
/// add more from <see cref="CurrencyCatalog"/>.
///
/// <para>Lives in <c>Domain.Tenancy</c> beside <see cref="Warehouse"/> rather than in
/// <c>Domain.Configuration</c> with the CreditTerm/PaymentMode lookups, because the module scan
/// groups it with the Organization/BillingLocation/Warehouse tenant context and the reference
/// product renders it on the Organization's own Features tab, not under Configurations &gt; Apps.
/// It implements <see cref="ITenantLookupEntity"/> anyway, so the generic
/// <c>ListLookupsQuery&lt;Currency&gt;</c> serves the list with no new handler.</para>
///
/// <para><b>Code, not Id, is what a document stores.</b> Documents carry a three-letter
/// <c>CurrencyCode</c> string, never a <c>CurrencyId</c> FK. Same reasoning as phase-27b's
/// <c>Invoice.Terms</c>: a document must keep the currency it was actually issued in even if the
/// tenant later deactivates or deletes that currency row, and the printed output labels the total
/// with the code itself (live: "NPR 3,06,500.00"). The code is also globally meaningful in a way
/// a per-tenant GUID is not, so a report never has to join to read it.</para>
///
/// <para><see cref="Name"/> and <see cref="Symbol"/> are seeded from the catalog entry but stay
/// editable, matching the live dialog where picking a currency pre-fills two fields that remain
/// free text (a tenant that writes cheques in "US Dollars" rather than "US Dollar" can say so).
/// <see cref="Code"/> is not editable -- it is the identity documents reference.</para>
/// </summary>
public sealed class Currency : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Symbol { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True for the one row every tenant is seeded with and can never deactivate or
    /// delete. Not stored -- derived from <see cref="Code"/>, so it cannot drift out of step with
    /// <see cref="CurrencyCatalog.BaseCode"/> or be flipped by a stray update.</summary>
    public bool IsBaseCurrency => CurrencyCatalog.IsBase(Code);

    private Currency()
    {
    }

    public static Currency Create(Guid organizationId, string code, string? name = null, string? symbol = null)
    {
        var normalisedCode = (code ?? string.Empty).Trim().ToUpperInvariant();

        var catalogEntry = CurrencyCatalog.Find(normalisedCode)
            ?? throw new InvalidOperationException($"'{normalisedCode}' is not a currency this product supports.");

        return new Currency
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = catalogEntry.Code,
            Name = string.IsNullOrWhiteSpace(name) ? catalogEntry.Name : name.Trim(),
            Symbol = string.IsNullOrWhiteSpace(symbol) ? catalogEntry.Symbol : symbol.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>The base currency row seeded for every Organization at creation. See
    /// CreateOrganizationCommandHandler.</summary>
    public static Currency CreateBase(Guid organizationId) => Create(organizationId, CurrencyCatalog.BaseCode);

    /// <summary>
    /// Renames/re-symbols a currency, and activates or deactivates it. The base currency may be
    /// renamed but never deactivated: every document defaults to it and every exchange rate is
    /// quoted to it, so a tenant that switched it off would be unable to raise any document at
    /// all -- the phase-20f lesson (check that a flag-off tenant can still function) applied to a
    /// row rather than a flag.
    /// </summary>
    public void Update(string name, string symbol, bool isActive)
    {
        if (IsBaseCurrency && !isActive)
        {
            throw new InvalidOperationException(
                $"{CurrencyCatalog.BaseCode} is the base currency and cannot be deactivated.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A currency's Name is required.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new InvalidOperationException("A currency's Symbol is required.");
        }

        Name = name.Trim();
        Symbol = symbol.Trim();
        IsActive = isActive;
    }
}
