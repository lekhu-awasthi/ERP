using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.ListKitchenStations;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.SetKitchenStationProducts;

/// <summary>
/// Phase 64 -- which products send their kitchen tickets to one station: the whole list, every time.
/// A product named here moves to this station (from Default or another one); a product on this station
/// and not named goes back to Default.
///
/// <para><b>Set from the station, not the product form</b> (docs/phase-64-status.md Decision G): one
/// command writing <c>Product.KitchenStationId</c>, rather than a trailing parameter through
/// <c>CreateProduct</c>/<c>UpdateProduct</c>'s 57 callers, which phase 60 found silently drop what they
/// are not passed. It is also how a restaurant thinks of it: "the bar gets the drinks".</para>
/// </summary>
public sealed record SetKitchenStationProductsCommand(Guid OrganizationId, Guid StationId, IReadOnlyList<Guid> ProductIds)
    : IRequest<IReadOnlyList<KitchenStationDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public const int MaxProducts = 1_000;

    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class SetKitchenStationProductsCommandValidator : AbstractValidator<SetKitchenStationProductsCommand>
{
    public SetKitchenStationProductsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.ProductIds).NotNull()
            .Must(x => x is null || x.Count <= SetKitchenStationProductsCommand.MaxProducts)
            .WithMessage($"At most {SetKitchenStationProductsCommand.MaxProducts} products at once.")
            .Must(x => x is null || x.Distinct().Count() == x.Count)
            .WithMessage("A product is listed twice.");
    }
}

public sealed class SetKitchenStationProductsCommandHandler(IAppDbContext db)
    : IRequestHandler<SetKitchenStationProductsCommand, IReadOnlyList<KitchenStationDto>>
{
    public async Task<IReadOnlyList<KitchenStationDto>> Handle(
        SetKitchenStationProductsCommand request, CancellationToken cancellationToken)
    {
        var station = await db.KitchenStations.SingleOrDefaultAsync(
                x => x.Id == request.StationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Kitchen station not found.");

        if (!station.IsActive && request.ProductIds.Count > 0)
        {
            throw new ConflictException($"'{station.Name}' is inactive; make it active before sending products to it.");
        }

        var wanted = request.ProductIds.ToHashSet();

        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId
                && (wanted.Contains(x.Id) || x.KitchenStationId == station.Id))
            .ToListAsync(cancellationToken);

        if (products.Count(x => wanted.Contains(x.Id)) != wanted.Count)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ProductIds), "A product named does not exist.")]);
        }

        foreach (var product in products)
        {
            var target = wanted.Contains(product.Id) ? station.Id : (Guid?)null;
            if (product.KitchenStationId != target)
            {
                try
                {
                    product.AssignKitchenStation(target);
                }
                catch (InvalidOperationException ex)
                {
                    throw new ValidationException([new ValidationFailure(nameof(request.ProductIds), ex.Message)]);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return await KitchenStationReader.ReadAsync(db, request.OrganizationId, cancellationToken);
    }
}
