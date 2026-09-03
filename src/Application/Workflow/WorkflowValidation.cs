using ErpApp.Application.Common.Documents;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow;

/// <summary>Shared existence checks reused by CreateTask/UpdateTask -- mirrors Sales.SalesValidation's
/// precedent.</summary>
public static class WorkflowValidation
{
    /// <summary>
    /// Confirms ParentId actually resolves to an existing row of the given ParentType within this
    /// Organization -- nothing must silently attach to a nonexistent parent or one in a different
    /// tenant.
    ///
    /// <para>Generic over the parent enum since Phase 27a, so WorkTask, Attachment and Comment share
    /// one implementation across all three of theirs. A document parent delegates to
    /// <see cref="DocumentExistenceReader"/> -- the single 17-arm switch every document-attached
    /// mechanism goes through -- resolved by member <i>name</i> via
    /// <see cref="DocumentParentTypes"/>, never by ordinal. The two non-document parents are handled
    /// here, and <c>DocumentMechanismSweepGuardTests</c> pins that Contact and Organization are the
    /// only two there will ever be without someone noticing.</para>
    /// </summary>
    public static async Task EnsureParentExistsAsync<TParentType>(
        IAppDbContext db, Guid organizationId, TParentType parentType, Guid parentId, CancellationToken cancellationToken)
        where TParentType : struct, Enum
    {
        if (DocumentParentTypes.TryToDocumentType(parentType) is { } documentType)
        {
            await DocumentExistenceReader.EnsureExistsAsync(db, organizationId, documentType, parentId, cancellationToken);
            return;
        }

        // Organization has no separate lookup -- its only valid ParentId is the command's own
        // (already-membership-checked) OrganizationId, so this is a comparison, not a query.
        var exists = parentType.ToString() switch
        {
            nameof(TaskParentType.Contact) => await db.Contacts.AnyAsync(
                x => x.Id == parentId && x.OrganizationId == organizationId, cancellationToken),
            nameof(TaskParentType.Organization) => parentId == organizationId,
            _ => throw new ArgumentOutOfRangeException(nameof(parentType), parentType, null),
        };

        if (!exists)
        {
            throw new NotFoundException($"{parentType} not found.");
        }
    }

    /// <summary>An AssignedToUserId, when supplied, must be an Accepted OrganizationMembership.UserId
    /// in this Organization -- not just any Guid the client could pass, which would let a Task be
    /// assigned to a user with no actual relationship to the Organization at all.</summary>
    public static async Task EnsureAssigneeIsAcceptedMemberAsync(
        IAppDbContext db, Guid organizationId, Guid? assignedToUserId, CancellationToken cancellationToken)
    {
        if (assignedToUserId is not { } userId)
        {
            return;
        }

        var isMember = await db.OrganizationMemberships.AnyAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId && x.Status == MembershipStatus.Accepted,
            cancellationToken);

        if (!isMember)
        {
            throw new NotFoundException("Assigned user is not a member of this organization.");
        }
    }

    public static async Task EnsureTaskTypeExistsAsync(
        IAppDbContext db, Guid organizationId, Guid taskTypeId, CancellationToken cancellationToken)
    {
        var exists = await db.TaskTypes.AnyAsync(
            x => x.Id == taskTypeId && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Task type not found.");
        }
    }
}
