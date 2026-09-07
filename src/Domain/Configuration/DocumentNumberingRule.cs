using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>Auto vs Manual numbering, per DocumentNumberingRule row (architecture-spec.md §3.1).</summary>
public enum NumberingMode
{
    Auto,
    Manual,
}

/// <summary>
/// One row per (OrganizationId, DocumentType) pair (architecture-spec.md §3.1), read by
/// IDocumentNumberGenerator when a document is Approved (never at Create -- numbers are assigned
/// at Approve, confirmed live). Rows are created lazily by
/// IDocumentNumberGenerator.GetNextNumberAsync on first use rather than eagerly seeded per
/// Organization -- see phase-2-status.md's scope decisions.
///
/// NextNumber is intentionally NOT mutated through this class -- the generator increments it via
/// a raw-SQL atomic UPDATE...OUTPUT statement that bypasses the EF change tracker entirely (see
/// Infrastructure.Persistence.DocumentNumberGenerator), because relying on RowVersion-based
/// optimistic concurrency for the increment itself would allow the exact duplicate-number race
/// the spec warns against. RowVersion here only protects the admin-edited settings fields
/// (Prefix/Mode/...), consistent with Organization.RowVersion's existing precedent.
/// </summary>
public sealed class DocumentNumberingRule
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public string Prefix { get; private set; } = null!;
    public int NextNumber { get; private set; }
    public NumberingMode Mode { get; private set; }
    public bool ResetEveryFiscalYear { get; private set; }
    public bool IncludeFiscalYearInCode { get; private set; }
    /// <summary>
    /// Live-labelled <b>"Enable Location-wise Next Number"</b> on the per-rule dialog, and visible
    /// only on a tenant whose Billing Location entitlement is on -- which is why it sat here, written
    /// by the settings screen but read by nothing, from phase 2 until phase 32 confirmed the control
    /// live (2026-09-07).
    ///
    /// <para>It lives on the settings row (<see cref="LocationId"/> null) and is read from there for
    /// every location, so turning it on or off is one decision per document type rather than per
    /// branch.</para>
    /// </summary>
    public bool LocationWiseNumbering { get; private set; }

    /// <summary>
    /// Phase 32 -- which counter this row is. <b>Null means the settings row</b>: exactly one per
    /// (Organization, DocumentType), carrying <see cref="Prefix"/>, <see cref="Mode"/> and the three
    /// flags, and also serving as the shared counter while
    /// <see cref="LocationWiseNumbering"/> is off. A non-null value means a per-location counter row,
    /// created lazily the first time a document is approved from that location while the flag is on;
    /// its settings columns are ignored, since the generator always reads those from the settings row.
    ///
    /// <para>SQL Server treats NULLs as <i>equal</i> in a unique index, which is normally the trap
    /// CLAUDE.md warns about and here is exactly the property wanted: the unique index on
    /// (OrganizationId, DocumentType, LocationId) admits one and only one settings row per pair, with
    /// no filtered index needed.</para>
    ///
    /// <para>Turning the flag off does not delete the per-location rows -- they simply stop being
    /// consulted, and resume where they left off if it is turned back on. Deleting them would restart
    /// a branch's numbering at 1 and reissue numbers that already exist on approved documents.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private DocumentNumberingRule()
    {
    }

    public static DocumentNumberingRule CreateDefault(
        Guid organizationId, DocumentType documentType, Guid? locationId = null)
    {
        return new DocumentNumberingRule
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            DocumentType = documentType,
            LocationId = locationId,
            Prefix = string.Empty,
            NextNumber = 1,
            Mode = NumberingMode.Auto,
            ResetEveryFiscalYear = false,
            IncludeFiscalYearInCode = false,
            LocationWiseNumbering = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateSettings(
        string prefix,
        NumberingMode mode,
        bool resetEveryFiscalYear,
        bool includeFiscalYearInCode,
        bool locationWiseNumbering)
    {
        Prefix = prefix;
        Mode = mode;
        ResetEveryFiscalYear = resetEveryFiscalYear;
        IncludeFiscalYearInCode = includeFiscalYearInCode;
        LocationWiseNumbering = locationWiseNumbering;
    }
}
