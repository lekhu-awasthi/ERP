using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.ListKitchenStations;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.UpdateKitchenStation;

/// <summary>Phase 64 -- renames a station or makes it inactive; refused while products still go there.</summary>
public sealed record UpdateKitchenStationCommand(Guid OrganizationId, Guid StationId, string Name, bool IsActive)
    : IRequest<IReadOnlyList<KitchenStationDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class UpdateKitchenStationCommandValidator : AbstractValidator<UpdateKitchenStationCommand>
{
    public UpdateKitchenStationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(KitchenStation.MaxNameLength)
            .Must(x => !string.Equals(x?.Trim(), KitchenStation.DefaultName, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"'{KitchenStation.DefaultName}' is where products without a station already go; choose another name.");
    }
}

public sealed class UpdateKitchenStationCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateKitchenStationCommand, IReadOnlyList<KitchenStationDto>>
{
    public async Task<IReadOnlyList<KitchenStationDto>> Handle(
        UpdateKitchenStationCommand request, CancellationToken cancellationToken)
    {
        var station = await db.KitchenStations.SingleOrDefaultAsync(
                x => x.Id == request.StationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Kitchen station not found.");

        var name = request.Name.Trim();
        if (await db.KitchenStations.AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.Id != station.Id && x.Name == name, cancellationToken))
        {
            throw new ConflictException($"A kitchen station called '{name}' already exists.");
        }

        var assigned = await db.Products.CountAsync(
            x => x.OrganizationId == request.OrganizationId && x.KitchenStationId == station.Id, cancellationToken);

        PosOrderCommands.Run(() => station.Update(name, request.IsActive, assigned));
        await db.SaveChangesAsync(cancellationToken);

        return await KitchenStationReader.ReadAsync(db, request.OrganizationId, cancellationToken);
    }
}
