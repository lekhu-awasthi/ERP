using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Sales;

/// <param name="UnitId">Phase 52 -- the unit this line is entered in. Null means the product's
/// own primary unit, which is what every line written before this phase meant and what a client
/// that does not send the field means, so an unaware caller behaves exactly as before. A unit
/// that is neither the product's primary nor one of its secondary units is a 400 naming the
/// line. The conversion factor is <b>not</b> accepted from the client: it is resolved from the
/// catalogue once, by <c>DocumentLineUnitResolver</c>, and frozen on the line.</param>
/// <param name="SerialNumbers">Phase 91 -- the units of a serial-tracked product this line brings back, each one its
/// invoice sold (<c>CreditNoteSerials</c>); the conversion template prefills those not yet returned.</param>
public sealed record CreditNoteLineInput(
    Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct = 0,
    Guid? UnitId = null, IReadOnlyList<string>? SerialNumbers = null);
