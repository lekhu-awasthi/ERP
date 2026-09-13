using ErpApp.Domain.Common;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// The one row shape Phase 38's two document categories share.
///
/// <para>Sales and Purchase documents carry the same fourteen columns in the same order -- a header
/// identity, a party, and the line's own trade figures -- so they project into one record and render
/// through one <see cref="ToCells"/>. Two copies of this would be two chances for the sheets to
/// drift apart in a way no test would notice, since each reader's own test only ever looks at its
/// own sheet.</para>
///
/// <para>It is a projection target inside an EF query, so every member is a plain value: no
/// formatting, no conversion, nothing the provider would have to translate.</para>
/// </summary>
/// <param name="Quantity">Signed: a return (Credit Note, Debit Note) is negated by its reader, so
/// summing a column of this sheet gives the net figure rather than a gross one somebody has to know
/// to subtract from.</param>
internal sealed record DocumentLineRow(
    string DocumentType,
    string DocumentCode,
    DateOnly Date,
    string Status,
    string? ContactName,
    string? Reference,
    string CurrencyCode,
    string? ProductCode,
    string? ProductName,
    decimal Quantity,
    decimal Rate,
    decimal DiscountPct,
    decimal Amount,
    decimal VatAmount)
{
    public object?[] ToCells() =>
    [
        DocumentType,
        DocumentCode,
        ExportCell.LocalDate(Date),
        Status,
        ContactName,
        Reference,
        CurrencyCode,
        ProductCode,
        ProductName,
        Quantity,
        Rate,
        DiscountPct,
        Amount,
        VatAmount,
    ];
}
