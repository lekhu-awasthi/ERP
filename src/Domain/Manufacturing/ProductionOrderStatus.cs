namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// The house Draft -> Approved -> Converted/Void lifecycle, matching Quotation/SalesOrder/
/// PurchaseOrder exactly.
///
/// <para><b>Live-confirmed rather than assumed (docs/phase-25-status.md Decision E).</b>
/// erp-module-scan.md §9 recorded a "status lifecycle independent of Approved/Draft
/// (Planned/InProgress/Completed)", which would have made Production Order the first document here
/// not to follow Draft -> Approve. The 2026-09-02 pass showed that is not what it is: the list has
/// <i>Approved</i> and <i>Draft</i> tabs like every other document, the create form's Code reads
/// DRAFT until approval, and Planned/InProgress/Completed live in a <i>separate</i> per-row STATUS
/// column in the list grid -- which is Phase 20b's Custom Status feature, not a lifecycle. So the
/// native lifecycle is the house one, and the scan's observation was of a second, orthogonal
/// pipeline layered over it.</para>
///
/// <para><see cref="Converted"/> is this codebase's own addition rather than parity: the reference
/// product still offers "Convert to Production Journal" on an order that has already been
/// converted (verified on PRO0011, whose PJ0013 was created a minute later), which is precisely
/// phase-6 bug #4's failure mode. We refuse the second conversion.</para>
/// </summary>
public enum ProductionOrderStatus
{
    Draft,
    Approved,
    Converted,
    Void,
}
