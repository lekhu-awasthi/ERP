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

namespace ErpApp.Application.Pos.Commands.CreateKitchenStation;

/// <summary>Phase 64 -- adds a kitchen station (the vendor's <i>Print Profile</i>: a name and nothing else).</summary>
public sealed record CreateKitchenStationCommand(Guid OrganizationId, string Name)
    : IRequest<IReadOnlyList<KitchenStationDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class CreateKitchenStationCommandValidator : AbstractValidator<CreateKitchenStationCommand>
{
    public CreateKitchenStationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(KitchenStation.MaxNameLength)
            .Must(x => !string.Equals(x?.Trim(), KitchenStation.DefaultName, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"'{KitchenStation.DefaultName}' is where products without a station already go; choose another name.");
    }
}

public sealed class CreateKitchenStationCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateKitchenStationCommand, IReadOnlyList<KitchenStationDto>>
{
    public async Task<IReadOnlyList<KitchenStationDto>> Handle(
        CreateKitchenStationCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (await db.KitchenStations.AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.Name == name, cancellationToken))
        {
            throw new ConflictException($"A kitchen station called '{name}' already exists.");
        }

        db.KitchenStations.Add(PosOrderCommands.Run(() => KitchenStation.Create(request.OrganizationId, name)));
        await db.SaveChangesAsync(cancellationToken);

        return await KitchenStationReader.ReadAsync(db, request.OrganizationId, cancellationToken);
    }
}
