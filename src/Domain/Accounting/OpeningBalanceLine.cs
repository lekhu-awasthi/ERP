using ErpApp.Domain.Common;

namespace ErpApp.Domain.Accounting;

/// <summary>
/// Phase 17 (Configurations §18, docs/phase-17-status.md) -- a "day-zero" per-account opening
/// balance, one row per (OrganizationId, AccountId). No Location field -- live-confirmed against
/// the Tigg reference product's own Opening Balances screen, which showed none either (that
/// tenant's Location entitlement isn't on, and this codebase has no Location concept at all).
/// Debit/Credit mirrors JournalVoucherLine's own shape (exactly one non-zero) rather than an
/// Amount+DR/CR-toggle pair -- same "one of two columns nonzero" convention already established
/// for manual GL entry.
///
/// Unlike every ApprovableTransaction, there is no Draft/Approve lifecycle -- the confirmed live
/// screen is a single inline "Save Changes" form with no separate approval step (matches FR-3.4's
/// View/Edit-only permission shape, no Approve key). Saving posts a balanced GlJournalEntry
/// immediately (CreateOrUpdateOpeningBalanceLineCommandHandler); editing an existing line reverses
/// its own prior posting first (GlJournalEntry.PostReversalOf, the same Phase 16a mechanism, not a
/// hand-derived reversal) before posting the corrected one.
/// </summary>
public sealed class OpeningBalanceLine
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Phase 28 (FR-2.5). The confirmed-live Opening Balances row form carries <b>Currency +
    /// Conversion Rate</b> beside Amount and DR/CR, and the Conversion Rate control is the
    /// identical widget the Invoice and Customer Payment forms use for Exchange Rate To NPR --
    /// manually entered, disabled and pinned to 1 while the currency is NPR (confirmed live
    /// 2026-09-04). So it is a per-row <i>document</i> rate, not a separate as-at revaluation rate,
    /// and it is named ExchangeRate here rather than ConversionRate so the eleven transactional
    /// aggregates and this one can be read with one vocabulary.
    /// </summary>
    public string CurrencyCode { get; private set; } = CurrencyCatalog.BaseCode;

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal ExchangeRate { get; private set; } = ExchangeRates.BaseRate;

    /// <summary>
    /// Phase 32 (FR-2.3/FR-3.3). The billing location this opening balance belongs to. Confirmed
    /// live 2026-09-07 on a location-enabled tenant: the Opening Balances &gt; Account inline row
    /// form reads <b>Location, Currency, Conversion Rate, Amount, DR/CR</b> -- Location is its
    /// <i>first</i> field, and it is absent entirely on a tenant without the entitlement, which is
    /// why no earlier phase could see it.
    ///
    /// <para>Nullable and set through Create/Update rather than a draft-guarded mutator, because
    /// unlike the fifteen transactional aggregates this row has no Draft/Approve lifecycle at all --
    /// it is a "day zero" figure keyed by (OrganizationId, AccountId), editable in place forever.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

    private OpeningBalanceLine()
    {
    }

    public static OpeningBalanceLine Create(
        Guid organizationId, Guid accountId, decimal debit, decimal credit,
        string? currencyCode = null, decimal? exchangeRate = null, Guid? locationId = null)
    {
        ValidateSides(debit, credit);
        var (code, rate) = ExchangeRates.Validate(currencyCode, exchangeRate);

        var now = DateTimeOffset.UtcNow;
        return new OpeningBalanceLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
            CurrencyCode = code,
            ExchangeRate = rate,
            LocationId = locationId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        decimal debit, decimal credit, string? currencyCode = null, decimal? exchangeRate = null,
        Guid? locationId = null)
    {
        ValidateSides(debit, credit);
        var (code, rate) = ExchangeRates.Validate(currencyCode, exchangeRate);
        Debit = debit;
        Credit = credit;
        CurrencyCode = code;
        ExchangeRate = rate;
        LocationId = locationId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateSides(decimal debit, decimal credit)
    {
        if (debit < 0 || credit < 0)
        {
            throw new InvalidOperationException("An opening balance's Debit/Credit cannot be negative.");
        }

        if ((debit > 0) == (credit > 0))
        {
            throw new InvalidOperationException("An opening balance must have exactly one of Debit/Credit greater than zero.");
        }
    }
}
