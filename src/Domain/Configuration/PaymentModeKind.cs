namespace ErpApp.Domain.Configuration;

/// <summary>
/// Phase 60 -- the vendor's payment-mode type, read on its POS settings (<c>Cash | Card | E-Payment |
/// Other</c>). The till groups its payment tabs by it and, from phase 61, a <see cref="Cash"/> tender
/// is what moves the session's drawer. Credit is deliberately not a member: the vendor's own wire
/// sends a credit tender as <c>{type:"Credit"}</c> with no mode id, and phase 59 Decision D makes it
/// the unsettled remainder of an invoice rather than a tender at all.
///
/// <para>No member 0, so a zero read from a row that never had the column fails loudly instead of
/// passing as a kind (the phase 55 <c>default(T)</c> lesson); the migration backfills
/// <see cref="Other"/>.</para>
/// </summary>
public enum PaymentModeKind
{
    Cash = 1,
    Card = 2,
    EPayment = 3,
    Other = 4,
}
