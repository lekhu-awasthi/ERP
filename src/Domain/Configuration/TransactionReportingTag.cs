using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// The many-to-many join architecture-spec.md §3.8 always specified but Phase 2 never built --
/// document-level only (Phase 19 decision #1, live-confirmed: a real Approved Quotation's
/// "REPORTING TAGS" control appears once per document, in the detail-page sidebar, not once per
/// line, and the create form itself carries no such field -- tagging is a post-creation
/// detail-page edit action). DocumentType/DocumentId reuse the same polymorphic pair every other
/// cross-cutting join in this codebase uses (WorkTask/Attachment's ParentType/ParentId) -- not a
/// real FK, since it points at whichever document table DocumentType names.
/// </summary>
public sealed class TransactionReportingTag
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid TagOptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TransactionReportingTag()
    {
    }

    public static TransactionReportingTag Create(Guid organizationId, DocumentType documentType, Guid documentId, Guid tagOptionId)
    {
        return new TransactionReportingTag
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            DocumentType = documentType,
            DocumentId = documentId,
            TagOptionId = tagOptionId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
