namespace ErpApp.Domain.Payments;

/// <summary>architecture-spec.md §4.6 -- Received=Customer Payment (this phase), Paid=Supplier
/// Payment (Phase 6, near-zero-new-code reuse of this same aggregate per the roadmap).</summary>
public enum PaymentDirection
{
    Received,
    Paid,
}
