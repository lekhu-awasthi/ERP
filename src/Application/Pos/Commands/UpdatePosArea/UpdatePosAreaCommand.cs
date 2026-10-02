using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosFloorPlan;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.UpdatePosArea;

/// <summary>Phase 64 -- renames an area, or makes it inactive (never deletes it: its tables carry orders).</summary>
public sealed record UpdatePosAreaCommand(Guid OrganizationId, Guid AreaId, string Name, bool IsActive)
    : IRequest<PosFloorPlanDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosFloorPlanManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class UpdatePosAreaCommandValidator : AbstractValidator<UpdatePosAreaCommand>
{
    public UpdatePosAreaCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AreaId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PosArea.MaxNameLength);
    }
}

public sealed class UpdatePosAreaCommandHandler(IAppDbContext db) : IRequestHandler<UpdatePosAreaCommand, PosFloorPlanDto>
{
    public async Task<PosFloorPlanDto> Handle(UpdatePosAreaCommand request, CancellationToken cancellationToken)
    {
        var area = await db.PosAreas.SingleOrDefaultAsync(
                x => x.Id == request.AreaId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Area not found.");

        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, area.BillingLocationId, cancellationToken);
        var name = request.Name.Trim();

        if (await db.PosAreas.AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == area.BillingLocationId
                    && x.Id != area.Id && x.Name == name,
                cancellationToken))
        {
            throw new ConflictException($"'{till.Location.Name}' already has an area called '{name}'.");
        }

        if (!request.IsActive && area.IsActive)
        {
            var seated = await (
                    from order in db.PosOrders
                    join table in db.PosTables on order.PosTableId equals table.Id
                    where order.OrganizationId == request.OrganizationId && order.Status == PosOrderStatus.Open
                          && table.PosAreaId == area.Id
                    select order.Code)
                .ToListAsync(cancellationToken);

            if (seated.Count > 0)
            {
                throw new ConflictException(
                    $"'{area.Name}' has open orders ({string.Join(", ", seated)}), so it cannot be made inactive.");
            }
        }

        PosOrderCommands.Run(() => area.Update(name, request.IsActive));
        await db.SaveChangesAsync(cancellationToken);

        return await PosFloorPlanReader.ReadAsync(db, request.OrganizationId, till.Location, cancellationToken);
    }
}
