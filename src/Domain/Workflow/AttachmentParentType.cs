namespace ErpApp.Domain.Workflow;

/// <summary>
/// docs/phase-18-status.md decision #2: deliberately NOT TaskParentType, despite Attachment being
/// architecturally the same (ParentType, ParentId) polymorphic shape as WorkTask. Live-confirmed
/// against both the Tigg reference product's Contact "Documents" tab (a flat, plain attachment
/// list -- drag-and-drop upload, no extraction/conversion state) and its Workflow "Document" tab
/// (an AI-extraction inbox with Pending/Done status and a "+ ADD AS" transaction-conversion menu):
/// the two are visually and functionally distinct screens in the reference product itself, not one
/// feature reused twice. Conflating them into one enum now would force Phase 22's future
/// UploadedDocument (extraction status, ConvertToTransaction, linked transaction) into the same
/// shape as a plain Contact file attachment -- the same kind of awkward-fit Phase 13 avoided by not
/// reusing Task/TaskStatus for WorkTask. Starts with just Contact -- the only confirmed live parent
/// this phase -- an additive future seam, not a speculative broader set.
/// </summary>
public enum AttachmentParentType
{
    Contact,
}
