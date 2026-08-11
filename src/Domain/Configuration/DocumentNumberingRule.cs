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
    public bool LocationWiseNumbering { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private DocumentNumberingRule()
    {
    }

    public static DocumentNumberingRule CreateDefault(Guid organizationId, DocumentType documentType)
    {
        return new DocumentNumberingRule
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            DocumentType = documentType,
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
