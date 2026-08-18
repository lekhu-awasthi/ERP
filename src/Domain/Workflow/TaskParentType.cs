namespace ErpApp.Domain.Workflow;

/// <summary>
/// The two confirmed live parent kinds a WorkTask can attach to (erp-module-scan.md line 106 --
/// "attaches to Contact, Organization, presumably others"). Deliberately not a broader speculative
/// set -- "presumably others" is a hedge, not a confirmed shape, so a third parent type is left as
/// an additive future seam rather than guessed at now. Not DocumentType -- that enum's values are
/// either real ApprovableTransaction types or numbering-pool-only stubs, a different semantic than
/// "what kind of entity can a Task attach to," and it has no Organization entry at all.
/// </summary>
public enum TaskParentType
{
    Contact,
    Organization,
}
