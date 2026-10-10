namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 74 -- how a till discount is given: the vendor's <i>Type of Discount</i> toggle, <b>Percent (%) | Amount</b>,
/// on a line and on a whole bill at both its tills (read live 2026-10-10). A line's Amount is rupees off each unit; a
/// bill's or an order's is rupees off the whole, spread over its lines.
/// </summary>
public enum PosDiscountKind
{
    Percent = 1,
    Amount = 2,
}
