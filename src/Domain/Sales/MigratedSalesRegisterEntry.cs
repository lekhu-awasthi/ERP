namespace ErpApp.Domain.Sales;

/// <summary>
/// One historical Sales Book line carried over from a prior system at cutover
/// (product-requirements.md FR-2.10, closing FR-9.4's "migrated" register variants). Tenant-scoped
/// by <see cref="OrganizationId"/> like every other aggregate here -- there is no EF global query
/// filter in this codebase, so every handler filters manually.
///
/// <para><b>The invariant, stated as an invariant, because the next person to touch this file will
/// otherwise try to give it a lifecycle.</b> A migrated register entry is <i>real enough to appear
/// in a statutory tax report and deliberately not real enough to be anything else</i>. It:
/// <list type="bullet">
/// <item>posts <b>no</b> <c>GlJournalEntry</c> and no <c>GlLine</c> -- FR-2.10's "without needing to
/// recreate every historical transaction as a full document" is precisely this, and the books it
/// would otherwise double-count are the prior system's, already closed;</item>
/// <item>creates <b>no</b> <c>StockLedgerEntry</c>, <c>StockMovement</c> or <c>Payment</c>, and no
/// <c>ContactLedger</c> movement;</item>
/// <item>draws <b>no</b> document number from <c>DocumentNumberGenerator</c> --
/// <see cref="DocumentCode"/> is the <i>prior</i> system's own number, copied verbatim;</item>
/// <item>has <b>no</b> Draft/Approve/Void lifecycle, so it never reaches the approval queue and
/// there is nothing to approve, void or reverse. There is no status property on purpose;</item>
/// <item>is <b>not</b> lock-date sensitive: <c>CreateMigratedSalesRegisterEntryCommand</c>
/// implements neither <c>ILockDateSensitive</c> nor <c>ILockDateSensitiveDocument</c>, so
/// <c>LockDateBehavior</c> skips it by construction. That is an explicit decision, not an
/// oversight -- a migrated row is by definition dated before the tenant's accounting start date and
/// therefore before any plausible lock date, and gating it would make the feature unusable for its
/// only purpose. It is safe precisely because of every bullet above: there are no books to
/// retro-edit;</item>
/// <item>never appears in the <i>live</i> Sales Register, VAT Summary, Annex 5, Annex 13 or the TDS
/// report -- only <c>MigratedSalesRegisterQuery</c> reads this table (docs/phase-21c-status.md,
/// Decision F).</item>
/// </list></para>
///
/// <para><b>The party is free text, not a Contact</b> (Decision A). <see cref="PartyName"/> and
/// <see cref="PartyPan"/> are what the prior system printed on the statutory register, which is the
/// only thing a cutover can promise; <see cref="ContactId"/> is filled only when the importer found
/// an <i>existing</i> Contact with exactly that PAN, and is null otherwise. Nothing here ever mints
/// a Contact -- inventing master data to satisfy a report column would put junk in the customer
/// list of every tenant that migrated.</para>
///
/// <para><b>A sales return is a negative row</b>, not a second entity type: the live Sales Register
/// already renders CreditNotes as negated rows in the same register (Phase 19 decision #3), so a
/// migrated return carrying negative values produces a byte-identical register shape with no return
/// modelling at all. The import template says so in its instructions.</para>
/// </summary>
public sealed class MigratedSalesRegisterEntry
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>The prior system's own transaction date -- never derived from the clock. See
    /// docs/phase-19-status.md's GlDateBoundary gotcha for why that distinction is load-bearing
    /// everywhere else in this tree; here there is no posting at all, so the business date is the
    /// only date the register ever uses.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The prior system's own document number, copied verbatim. Unique per organization
    /// (see MigratedSalesRegisterEntryConfiguration) -- that index is what makes a second identical
    /// upload reject every row instead of silently doubling a tenant's statutory sales.</summary>
    public string DocumentCode { get; private set; } = null!;

    public string PartyName { get; private set; } = null!;

    public string? PartyPan { get; private set; }

    /// <summary>Set only when an existing Contact carried exactly this PAN at import time; never
    /// created, never guessed by name. Null is the expected value for most migrations.</summary>
    public Guid? ContactId { get; private set; }

    public decimal TotalValue { get; private set; }
    public decimal TaxExemptValue { get; private set; }
    public decimal TaxableValue { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>
    /// The four export columns the <i>live</i> Sales Register ships hardcoded to 0/null, because
    /// this codebase's Invoice has no export-sale flag yet (FR-5.8, deferred to Phase 23 -- Phase 19
    /// decision #3). A migrated row has no such gap: the prior system knew, and the spreadsheet can
    /// carry it. Accepting these four columns costs four columns and is the only statutory data a
    /// cutover would otherwise lose outright, so the migrated register may legitimately populate
    /// what its live sibling always leaves empty.
    /// </summary>
    public decimal ExportValue { get; private set; }

    public string? ExportCountry { get; private set; }
    public string? ExportDeclarationNo { get; private set; }
    public DateOnly? ExportDeclarationDate { get; private set; }

    /// <summary>When the row was imported -- provenance only, never a business date.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    private MigratedSalesRegisterEntry()
    {
    }

    public static MigratedSalesRegisterEntry Create(
        Guid organizationId,
        DateOnly date,
        string documentCode,
        string partyName,
        string? partyPan,
        Guid? contactId,
        decimal totalValue,
        decimal taxExemptValue,
        decimal taxableValue,
        decimal vatAmount,
        decimal exportValue,
        string? exportCountry,
        string? exportDeclarationNo,
        DateOnly? exportDeclarationDate,
        DateTimeOffset now)
    {
        return new MigratedSalesRegisterEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Date = date,
            DocumentCode = documentCode,
            PartyName = partyName,
            PartyPan = partyPan,
            ContactId = contactId,
            TotalValue = totalValue,
            TaxExemptValue = taxExemptValue,
            TaxableValue = taxableValue,
            VatAmount = vatAmount,
            ExportValue = exportValue,
            ExportCountry = exportCountry,
            ExportDeclarationNo = exportDeclarationNo,
            ExportDeclarationDate = exportDeclarationDate,
            CreatedAt = now,
        };
    }
}
