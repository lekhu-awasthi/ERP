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

namespace ErpApp.Application.Pos.Commands.CreatePosArea;

/// <summary>Phase 64 -- adds an area to a Restaurant location's floor (the vendor's <i>New Area</i>: a name).</summary>
public sealed record CreatePosAreaCommand(Guid OrganizationId, Guid LocationId, string Name)
    : IRequest<PosFloorPlanDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosFloorPlanManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class CreatePosAreaCommandValidator : AbstractValidator<CreatePosAreaCommand>
{
    public CreatePosAreaCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PosArea.MaxNameLength);
    }
}

public sealed class CreatePosAreaCommandHandler(IAppDbContext db) : IRequestHandler<CreatePosAreaCommand, PosFloorPlanDto>
{
    public async Task<PosFloorPlanDto> Handle(CreatePosAreaCommand request, CancellationToken cancellationToken)
    {
        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        var name = request.Name.Trim();

        // SQL Server's collation compares names case-insensitively, and so does the unique index.
        if (await db.PosAreas.AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == till.Location.Id
                    && x.Name == name,
                cancellationToken))
        {
            throw new ConflictException($"'{till.Location.Name}' already has an area called '{name}'.");
        }

        db.PosAreas.Add(PosArea.Create(request.OrganizationId, till.Location.Id, name));
        await db.SaveChangesAsync(cancellationToken);

        return await PosFloorPlanReader.ReadAsync(db, request.OrganizationId, till.Location, cancellationToken);
    }
}
