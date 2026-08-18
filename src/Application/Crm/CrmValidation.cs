using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm;

/// <summary>Shared existence/validity checks reused by CreateDeal/UpdateDeal/MoveDealToStage --
/// mirrors Workflow.WorkflowValidation's precedent.</summary>
internal static class CrmValidation
{
    /// <summary>A Deal is a pre-sale/sales-pipeline concept -- Customer and Lead contacts can have
    /// one, a Supplier cannot (explicit judgment call, documented rather than left implicit: no
    /// erp-module-scan.md evidence pins this down either way). Existence uses a 404
    /// (NotFoundException); the wrong-Contact-Type case uses a 409 (ConflictException) since the
    /// Contact *does* exist but is the wrong kind for this operation -- the same distinction
    /// SalesValidation/PurchasingValidation draw elsewhere in this codebase between "doesn't exist"
    /// and "exists but violates a business rule".</summary>
    public static async Task EnsureContactCanHaveDealAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        if (contact.Type == ContactType.Supplier)
        {
            throw new ConflictException("A Deal cannot be created against a Supplier contact.");
        }
    }

    public static async Task EnsureLeadSourceExistsAsync(
        IAppDbContext db, Guid organizationId, Guid? leadSourceId, CancellationToken cancellationToken)
    {
        if (leadSourceId is not { } id)
        {
            return;
        }

        var exists = await db.LeadSources.AnyAsync(
            x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Lead source not found.");
        }
    }

    public static async Task EnsureDealStageExistsAsync(
        IAppDbContext db, Guid organizationId, Guid dealStageId, CancellationToken cancellationToken)
    {
        var exists = await db.DealStages.AnyAsync(
            x => x.Id == dealStageId && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Deal stage not found.");
        }
    }

    /// <summary>Every AssigneeUserId, when supplied, must be an Accepted
    /// OrganizationMembership.UserId in this Organization -- mirrors
    /// WorkflowValidation.EnsureAssigneeIsAcceptedMemberAsync's precedent, extended for a plural
    /// assignee set.</summary>
    public static async Task EnsureAssigneesAreAcceptedMembersAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyCollection<Guid> assigneeUserIds, CancellationToken cancellationToken)
    {
        if (assigneeUserIds.Count == 0)
        {
            return;
        }

        var distinctIds = assigneeUserIds.Distinct().ToList();

        var acceptedCount = await db.OrganizationMemberships.CountAsync(
            x => x.OrganizationId == organizationId
                && x.UserId != null
                && distinctIds.Contains(x.UserId.Value)
                && x.Status == MembershipStatus.Accepted,
            cancellationToken);

        if (acceptedCount != distinctIds.Count)
        {
            throw new NotFoundException("One or more assignees are not members of this organization.");
        }
    }
}
