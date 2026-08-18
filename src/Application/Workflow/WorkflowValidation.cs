using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow;

/// <summary>Shared existence checks reused by CreateTask/UpdateTask -- mirrors Sales.SalesValidation's
/// precedent.</summary>
internal static class WorkflowValidation
{
    /// <summary>Confirms ParentId actually resolves to an existing row of the given ParentType
    /// within this Organization -- a WorkTask must not silently attach to a nonexistent Contact or
    /// (in principle, though OrganizationId is already the caller's own) a different Organization.</summary>
    public static async Task EnsureParentExistsAsync(
        IAppDbContext db, Guid organizationId, TaskParentType parentType, Guid parentId, CancellationToken cancellationToken)
    {
        // Organization has no separate lookup -- its only valid ParentId is the command's own
        // (already-membership-checked) OrganizationId, so this is a comparison, not a query.
        var exists = parentType switch
        {
            TaskParentType.Contact => await db.Contacts.AnyAsync(
                x => x.Id == parentId && x.OrganizationId == organizationId, cancellationToken),
            TaskParentType.Organization => parentId == organizationId,
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
