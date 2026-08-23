namespace ErpApp.Domain.Contacts;

/// <summary>
/// Contact-scoped comment (product-requirements.md FR-4.5's "communication/activity log").
/// Deliberately a direct ContactId FK, not a polymorphic (ParentType, ParentId) pair like
/// WorkTask/Attachment -- no second parent type is confirmed live or in scope this phase (see
/// docs/phase-18-status.md decision #3), so a fixed FK is the smaller thing that satisfies today's
/// one caller; generalize to polymorphic only if/when a second parent type is actually needed.
/// No Update/Delete -- the live Tigg comment composer only ever showed "ADD COMMENT" appending to a
/// running feed, never an edit/delete affordance on an existing comment.
/// </summary>
public sealed class Comment
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Content { get; private set; } = null!;
    public Guid AuthorUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Comment()
    {
    }

    public static Comment Create(Guid organizationId, Guid contactId, string content, Guid authorUserId)
    {
        return new Comment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Content = content,
            AuthorUserId = authorUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
