using ErpApp.Domain.Workflow;

namespace ErpApp.Domain.Contacts;

/// <summary>
/// A comment on a Contact or on a transactional document (product-requirements.md FR-4.5's
/// "communication/activity log").
///
/// <para><b>Phase 27a made this polymorphic, on the trigger Phase 18 set for it.</b> Decision #3
/// gave it a fixed <c>ContactId</c> FK rather than the (ParentType, ParentId) pair WorkTask and
/// Attachment use, explicitly "generalize to polymorphic only if/when a second parent type is
/// actually needed." It is now: every transactional detail page's Activity tab opens with a real
/// comment composer above sub-tabs Comments / Activities / Emails -- live-confirmed on Invoice,
/// Journal Voucher and Warehouse Transfer. So a comment on a document is the same concept as a
/// comment on a Contact, and the deferral resolves on evidence rather than by analogy.</para>
///
/// <para>Still no Update/Delete: the live composer only ever appends to a running feed, with no
/// edit or delete affordance on an existing comment. That has not changed.</para>
/// </summary>
public sealed class Comment
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public CommentParentType ParentType { get; private set; }
    public Guid ParentId { get; private set; }
    public string Content { get; private set; } = null!;
    public Guid AuthorUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Comment()
    {
    }

    public static Comment Create(
        Guid organizationId,
        CommentParentType parentType,
        Guid parentId,
        string content,
        Guid authorUserId)
    {
        return new Comment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ParentType = parentType,
            ParentId = parentId,
            Content = content,
            AuthorUserId = authorUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
