namespace ErpApp.Domain.Imports;

/// <summary>
/// One spreadsheet row's outcome, and simultaneously the claim that stops that row being processed
/// twice. See <see cref="ImportJobRowStatus"/> for the claim-then-act ordering and what it costs.
///
/// <para>The unique index on (ImportJobId, RowNumber) is this type's load-bearing constraint, the
/// direct analogue of AlertSendLog's (definition, occurrence, recipient). RowNumber is the
/// <b>spreadsheet's own 1-based row number</b>, header included, so an error message points at the
/// line the user actually sees in Excel -- matching the reference product, whose validation step
/// renders errors as "Row: {LineNo} {Header} {Message}".</para>
/// </summary>
public sealed class ImportJobRow
{
    public Guid Id { get; private set; }
    public Guid ImportJobId { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>1-based spreadsheet row number including the header row, so row 2 is the first data row.</summary>
    public int RowNumber { get; private set; }

    public ImportJobRowStatus Status { get; private set; }

    /// <summary>The column the failure is about, when it is about one -- mirrors the reference
    /// product's per-error "Header" field. Null for whole-row failures.</summary>
    public string? ColumnName { get; private set; }

    public string? Message { get; private set; }

    /// <summary>The Product/Contact this row created or updated. Null unless Succeeded.</summary>
    public Guid? TargetId { get; private set; }

    /// <summary>The created/matched record's business code, shown in the results grid.</summary>
    public string? TargetCode { get; private set; }

    private ImportJobRow()
    {
    }

    /// <summary>Claims the row. Commit this before sending the create/update command.</summary>
    public static ImportJobRow Claim(Guid importJobId, Guid organizationId, int rowNumber)
    {
        return new ImportJobRow
        {
            Id = Guid.NewGuid(),
            ImportJobId = importJobId,
            OrganizationId = organizationId,
            RowNumber = rowNumber,
            Status = ImportJobRowStatus.Pending,
        };
    }

    public void MarkSucceeded(Guid targetId, string? targetCode)
    {
        Status = ImportJobRowStatus.Succeeded;
        ColumnName = null;
        Message = null;
        TargetId = targetId;
        TargetCode = targetCode;
    }

    /// <summary>Message is truncated rather than rejected: it can carry an arbitrary validator or
    /// provider string, and losing its tail must never fail the save that records the failure.</summary>
    public void MarkFailed(string? columnName, string message)
    {
        const int MaxMessageLength = 1000;
        const int MaxColumnNameLength = 100;

        Status = ImportJobRowStatus.Failed;
        ColumnName = columnName is { Length: > MaxColumnNameLength } ? columnName[..MaxColumnNameLength] : columnName;
        Message = message.Length > MaxMessageLength ? message[..MaxMessageLength] : message;
    }
}
