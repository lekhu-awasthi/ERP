using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.CreateDeal;

public sealed class CreateDealCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CreateDealCommand, CreateDealResult>
{
    public async Task<CreateDealResult> Handle(CreateDealCommand request, CancellationToken cancellationToken)
    {
        await CrmValidation.EnsureContactCanHaveDealAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await CrmValidation.EnsureLeadSourceExistsAsync(db, request.OrganizationId, request.LeadSourceId, cancellationToken);
        await CrmValidation.EnsureAssigneesAreAcceptedMembersAsync(
            db, request.OrganizationId, request.AssigneeUserIds, cancellationToken);

        var deal = Deal.Create(
            request.OrganizationId,
            request.ContactId,
            request.Title,
            request.Description,
            request.LeadSourceId,
            request.ExpectedRevenue,
            request.ExpectedClosingDate,
            request.IsPrivate,
            currentUser.UserId);

        foreach (var userId in request.AssigneeUserIds.Distinct())
        {
            deal.AddAssignee(userId);
        }

        db.Deals.Add(deal);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDealResult(deal.Id, deal.Title, deal.Status, deal.CreatedAt);
    }
}
