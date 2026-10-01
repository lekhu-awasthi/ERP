namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 60 -- a payment mode offered at one location's till: the vendor's Location Settings &gt;
/// Payment Mode tab (<c>/pos/linked-payment-modes</c>, <c>/pos/location-payment-modes</c>,
/// <c>/pos/unlink-payment-modes</c>). A row per (location, mode), unique; no row means the mode is
/// not offered there, so a new location offers nothing until an Admin links something -- the till
/// must not guess which of a tenant's modes a branch actually accepts.
///
/// <para>Its own table rather than a collection on <see cref="PosLocationSettings"/>: the set is
/// replaced wholesale on every save, and doing that through the child <c>DbSet</c> is the remedy
/// phases 4 and 24 already paid for. Deleting the payment mode cascades its links away; the mode's
/// own delete is the decision, and a link to nothing would be a till tab that cannot post.</para>
/// </summary>
public sealed class PosLocationPaymentMode
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BillingLocationId { get; private set; }
    public Guid PaymentModeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PosLocationPaymentMode()
    {
    }

    public static PosLocationPaymentMode Create(Guid organizationId, Guid billingLocationId, Guid paymentModeId)
    {
        return new PosLocationPaymentMode
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BillingLocationId = billingLocationId,
            PaymentModeId = paymentModeId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
