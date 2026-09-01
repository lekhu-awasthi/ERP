namespace ErpApp.Domain.Workflow;

/// <summary>
/// The inbox lifecycle, live-confirmed as the reference product's own two Pending/Done tabs
/// (erp-module-scan.md's Workflow &gt; Document sub-module). Deliberately two members, not three:
/// there is no Discarded/Ignored state, because a scan nobody wants is deleted (see
/// <see cref="UploadedDocument"/>'s invariant) and a scan somebody filed without posting it is
/// <see cref="Done"/>. See docs/phase-22-status.md, Decision A.
/// </summary>
public enum UploadedDocumentStatus
{
    /// <summary>Uploaded, not yet dealt with. The inbox's working set.</summary>
    Pending,

    /// <summary>
    /// Dealt with -- either converted into a transaction (set automatically by
    /// <see cref="UploadedDocument.LinkTransaction"/>) or filed by hand without one
    /// (<see cref="UploadedDocument.MarkDone"/>, for a receipt the tenant keeps but never posts).
    /// </summary>
    Done,
}
