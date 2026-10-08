using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 60 -- one billing location's point-of-sale settings: the vendor's Location Settings &gt;
/// General tab, read in phase 59 (<c>erp-module-scan.md</c>, "Configuration, as read"). Service
/// charge (rate and account), round-off (account), cash verification with its note
/// denominations, the default tab, and which documents print.
///
/// <para><b>Per location, not per tenant</b>, because the vendor's are and because each is a fact
/// about one till: two branches of one tenant charge different service charges, and a drawer's
/// denominations are the notes that drawer holds (phase-60-status.md Decision C).</para>
///
/// <para><b>Its own row, not columns on <see cref="BillingLocation"/>.</b> A location exists for
/// every tenant; these settings mean something only where a till runs, and nine POS columns on a
/// table every ERP document joins to would be read by none of them. A location with no row reads as
/// <see cref="CreateDefault"/>'s values -- the query projects the defaults rather than a null, and
/// the first save creates the row.</para>
///
/// <para><b>An account left empty falls back to the tenant default</b> (Service Charge Income,
/// Rounding; phase 59 Decision I), resolved at posting time by phase 61. That is what makes the
/// tenant defaults worth having beside a per-location field, and why neither account is required
/// here even when its feature is on.</para>
/// </summary>
public sealed class PosLocationSettings
{
    /// <summary>The vendor's own default and "Reset to default" list, in Nepali rupees.</summary>
    public static readonly IReadOnlyList<int> DefaultDenominations = [1000, 500, 100, 50, 20, 10, 5, 2, 1];

    /// <summary>More than any real note-and-coin series; a bound, not a rule.</summary>
    public const int MaxDenominations = 20;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BillingLocationId { get; private set; }

    /// <summary>The vendor's "Service Charge Applicable" switch. A till line carries service charge
    /// only when this is on <i>and</i> the product's own flag is (phase 59 Decision G).</summary>
    public bool ServiceChargeEnabled { get; private set; }

    /// <summary>Percent, e.g. 10.00. Zero exactly when <see cref="ServiceChargeEnabled"/> is off, so
    /// the pair can never disagree about whether a charge applies.</summary>
    public decimal ServiceChargeRate { get; private set; }

    public Guid? ServiceChargeAccountId { get; private set; }

    /// <summary>
    /// Phase 68 -- whether food parcelled from a dine-in line keeps that line's service charge (the user's
    /// answer of 2026-10-03). On by default, which is what a dine-in line carried before marking existed.
    /// Read once, when a quantity is marked, and frozen on the take-away line, so changing it reprices
    /// nothing already marked. Meaningful only while <see cref="ServiceChargeEnabled"/> is on; stored
    /// regardless, so switching service charge off and on again loses nothing. A Take Away <i>order</i>
    /// never carries service charge whatever this says (phase 64 Decision H).
    /// </summary>
    public bool ServiceChargeOnTakeAway { get; private set; }

    /// <summary>The vendor's "Round off": the till rounds a bill to the rupee and books the difference
    /// to <see cref="RoundOffAccountId"/> (live: 632.80 billed as 633, the 0.20 credited to the
    /// account the location names). The rounding rule itself is phase 61's.</summary>
    public bool RoundOffEnabled { get; private set; }

    public Guid? RoundOffAccountId { get; private set; }

    /// <summary>The vendor's "Require Cash Verification": a session is opened and closed by
    /// counting notes (<see cref="Denominations"/>) rather than typing a total.</summary>
    public bool CashVerificationRequired { get; private set; }

    /// <summary>Note and coin values, largest first, distinct and positive. Always present: the
    /// session's denomination tab uses them whether or not verification is required.</summary>
    public IReadOnlyList<int> Denominations { get; private set; } = [];

    /// <summary>Null means "the mode's first tab". Validated against the location's mode when saved;
    /// a later mode change can leave it stale, which <see cref="EffectiveDefaultTab"/> absorbs.</summary>
    public PosTab? DefaultTab { get; private set; }

    public bool PrintEstimateBill { get; private set; }
    public bool PrintInvoice { get; private set; }
    public bool PrintCreditNote { get; private set; }

    /// <summary>Meaningful only at a Restaurant location, where KOTs exist (the vendor gates the
    /// toggle on <c>type === "Bar"</c>); stored regardless so a mode change loses nothing.</summary>
    public bool PrintKot { get; private set; }

    /// <summary>
    /// Phase 62 -- this till may issue an <b>abbreviated tax invoice</b> (संक्षिप्त कर बीजक) instead
    /// of a full one. Off by default, because it is not the business's choice alone: VAT Rules 2053
    /// Rule 18(1) (as amended by the 21st Amendment, 2076) allows it only to a registered person who
    /// sells by retail <b>with the Tax Officer's permission</b>, and Rule 18(6) caps it at
    /// <see cref="Domain.Sales.Invoice.AbbreviatedTaxInvoiceLimit"/> per transaction. Switching it on
    /// records that the permission is held; which bills then qualify is decided per sale
    /// (phase-62-status.md Decision A).
    /// </summary>
    public bool AbbreviatedTaxInvoiceEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private PosLocationSettings()
    {
    }

    /// <summary>The values a location with no settings row reads as: nothing charged, nothing
    /// rounded, no counting required, the vendor's denominations, and every document printed.</summary>
    public static PosLocationSettings CreateDefault(Guid organizationId, Guid billingLocationId)
    {
        return new PosLocationSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BillingLocationId = billingLocationId,
            ServiceChargeEnabled = false,
            ServiceChargeRate = 0m,
            ServiceChargeAccountId = null,
            ServiceChargeOnTakeAway = true,
            RoundOffEnabled = false,
            RoundOffAccountId = null,
            CashVerificationRequired = false,
            Denominations = DefaultDenominations.ToList(),
            DefaultTab = null,
            PrintEstimateBill = true,
            PrintInvoice = true,
            PrintCreditNote = true,
            PrintKot = true,
            AbbreviatedTaxInvoiceEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Replaces every setting at once, as the vendor's single Save does. <paramref name="mode"/> is
    /// the location's current <see cref="PosMode"/>, needed only to check the default tab.
    /// </summary>
    public void Update(
        PosMode mode,
        bool serviceChargeEnabled,
        decimal serviceChargeRate,
        Guid? serviceChargeAccountId,
        bool serviceChargeOnTakeAway,
        bool roundOffEnabled,
        Guid? roundOffAccountId,
        bool cashVerificationRequired,
        IEnumerable<int> denominations,
        PosTab? defaultTab,
        bool printEstimateBill,
        bool printInvoice,
        bool printCreditNote,
        bool printKot,
        bool abbreviatedTaxInvoiceEnabled)
    {
        if (serviceChargeEnabled && (serviceChargeRate <= 0m || serviceChargeRate > 100m))
        {
            throw new InvalidOperationException("A service charge rate must be more than 0% and at most 100%.");
        }

        if (defaultTab is { } tab && !PosTabs.For(mode).Contains(tab))
        {
            throw new InvalidOperationException(
                $"'{tab}' is not a tab of a {mode} till. It has: {string.Join(", ", PosTabs.For(mode))}.");
        }

        ServiceChargeEnabled = serviceChargeEnabled;
        ServiceChargeRate = serviceChargeEnabled ? decimal.Round(serviceChargeRate, 2) : 0m;
        ServiceChargeAccountId = serviceChargeEnabled ? serviceChargeAccountId : null;
        ServiceChargeOnTakeAway = serviceChargeOnTakeAway;
        RoundOffEnabled = roundOffEnabled;
        RoundOffAccountId = roundOffEnabled ? roundOffAccountId : null;
        CashVerificationRequired = cashVerificationRequired;
        Denominations = NormalizeDenominations(denominations);
        DefaultTab = defaultTab;
        PrintEstimateBill = printEstimateBill;
        PrintInvoice = printInvoice;
        PrintCreditNote = printCreditNote;
        PrintKot = printKot;
        AbbreviatedTaxInvoiceEnabled = abbreviatedTaxInvoiceEnabled;
    }

    /// <summary>The tab the till opens on: the saved one when it still belongs to
    /// <paramref name="mode"/>, else the mode's first. Null only when no till runs here.</summary>
    public PosTab? EffectiveDefaultTab(PosMode mode)
    {
        var tabs = PosTabs.For(mode);
        if (tabs.Count == 0)
        {
            return null;
        }

        return DefaultTab is { } tab && tabs.Contains(tab) ? tab : tabs[0];
    }

    /// <summary>Distinct, positive, largest first. Duplicates are refused rather than folded: two
    /// "100" rows on a counting screen are a typo the user should see.</summary>
    public static IReadOnlyList<int> NormalizeDenominations(IEnumerable<int> denominations)
    {
        var list = denominations.ToList();

        if (list.Count == 0)
        {
            throw new InvalidOperationException("At least one cash denomination is required.");
        }

        if (list.Count > MaxDenominations)
        {
            throw new InvalidOperationException($"At most {MaxDenominations} cash denominations are allowed.");
        }

        if (list.Any(x => x <= 0))
        {
            throw new InvalidOperationException("Every cash denomination must be a positive amount.");
        }

        if (list.Distinct().Count() != list.Count)
        {
            throw new InvalidOperationException("A cash denomination is listed twice.");
        }

        return list.OrderByDescending(x => x).ToList();
    }
}
