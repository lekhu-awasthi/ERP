using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Sales;


/// <param name="BatchNo">Phase 51 -- the batch this line receives into or issues from, by number.
/// A <i>number</i> rather than an id because on a receipt the batch may not exist yet: naming it is
/// how it comes into being, which is what the one Item Batch column on both grids means. Null for a
/// product that is not batch-tracked, and refused with a 400 if that product is not.</param>
/// <param name="ManufactureDate">Phase 51 -- carried only when the batch is being created (or when
/// filling a blank on an existing one). Nullable, because the read establishes neither date as
/// required and a lot number with no expiry is ordinary.</param>
/// <param name="ExpiryDate">See <paramref name="ManufactureDate"/>.</param>
/// <param name="SerialNumbers">Phase 51 -- one per physical unit, so exactly
/// <c>Quantity</c> of them when the product is serial-tracked and none otherwise. A list and not a
/// single value because the Serial Number tab is a row per unit; the batch is a single value
/// because the grid shows a single control. The asymmetry is forced by the data, not chosen.</param>
/// <param name="UnitId">Phase 52 -- the unit this line is entered in. Null means the product's
/// own primary unit, so an unaware caller behaves exactly as before. A unit that is neither the
/// product's primary nor one of its secondary units is a 400 naming the line. The conversion
/// factor is <b>not</b> accepted from the client: it is resolved from the catalogue once, by
/// <c>DocumentLineUnitResolver</c>, and frozen on the line.</param>
public sealed record InvoiceLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    VatRate VatRate,
    decimal DiscountPct = 0,
    string? BatchNo = null,
    DateOnly? ManufactureDate = null,
    DateOnly? ExpiryDate = null,
    IReadOnlyList<string>? SerialNumbers = null,
    Guid? UnitId = null);
