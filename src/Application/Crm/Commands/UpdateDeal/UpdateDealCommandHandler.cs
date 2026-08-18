using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Crm;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.UpdateDeal;

public sealed class UpdateDealCommandHandler(IAppDbContext db) : IRequestHandler<UpdateDealCommand, UpdateDealResult>
{
    public async Task<UpdateDealResult> Handle(UpdateDealCommand request, CancellationToken cancellationToken)
    {
        var deal = await db.Deals.Include(x => x.Assignees).SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Deal not found.");

        if (deal.Status != DealStatus.Pending)
        {
            throw new ConflictException($"A {deal.Status} deal can no longer be edited.");
        }

        await CrmValidation.EnsureLeadSourceExistsAsync(db, request.OrganizationId, request.LeadSourceId, cancellationToken);
        await CrmValidation.EnsureAssigneesAreAcceptedMembersAsync(
            db, request.OrganizationId, request.AssigneeUserIds, cancellationToken);

        deal.Update(
            request.Title, request.Description, request.LeadSourceId, request.ExpectedRevenue,
            request.ExpectedClosingDate, request.IsPrivate);

        // Explicit add/remove diff against the desired assignee set -- not a Clear+re-Add, per
        // CLAUDE.md's own InMemory-provider-mistracking gotcha (a same-count Clear+re-Add of an
        // encapsulated child collection can get mis-tracked as Modified/Deleted instead of
        // Added/Deleted). Mirrors Phase 14's UpdateRolePermissionsCommandHandler diff-and-save
        // discipline, applied here to a real child collection rather than independent rows.
        var desired = request.AssigneeUserIds.Distinct().ToHashSet();
        var current = deal.Assignees.Select(x => x.UserId).ToHashSet();

        foreach (var userId in current.Except(desired))
        {
            deal.RemoveAssignee(userId);
        }

        foreach (var userId in desired.Except(current))
        {
            deal.AddAssignee(userId);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateDealResult(deal.Id, deal.Title, deal.Status);
    }
}
