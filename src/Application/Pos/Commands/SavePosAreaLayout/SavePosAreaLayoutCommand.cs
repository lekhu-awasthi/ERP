using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosFloorPlan;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.SavePosAreaLayout;

/// <summary>One table in a layout save; <paramref name="Id"/> null adds a table.</summary>
public sealed record PosTableLayoutInput(
    Guid? Id, string Name, int Capacity, PosTableShape Shape, int X, int Y, int Width, int Height, bool IsActive);

/// <summary>
/// Phase 64 -- saves an area's whole layout at once, as the vendor's <i>Save Changes</i> does
/// (<c>POST /pos/floorplans {area_id, table:[...]}</c>, which replaces the area's table set). Every
/// existing table must be in it: a table is never deleted, only made inactive (see
/// <see cref="PosArea.ApplyLayout"/>).
/// </summary>
public sealed record SavePosAreaLayoutCommand(
    Guid OrganizationId, Guid AreaId, IReadOnlyList<PosTableLayoutInput> Tables)
    : IRequest<PosFloorPlanDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosFloorPlanManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class SavePosAreaLayoutCommandValidator : AbstractValidator<SavePosAreaLayoutCommand>
{
    public SavePosAreaLayoutCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AreaId).NotEmpty();
        RuleFor(x => x.Tables).NotNull()
            .Must(x => x is null || x.Count <= PosArea.MaxTables)
            .WithMessage($"An area holds at most {PosArea.MaxTables} tables.");
        RuleForEach(x => x.Tables).ChildRules(table =>
        {
            table.RuleFor(x => x.Name).NotEmpty().MaximumLength(PosArea.MaxNameLength);
            table.RuleFor(x => x.Capacity).InclusiveBetween(1, PosTable.MaxCapacity);
            table.RuleFor(x => x.Shape).IsInEnum();
            table.RuleFor(x => x.Width).InclusiveBetween(PosTable.MinSize, PosArea.CanvasWidth);
            table.RuleFor(x => x.Height).InclusiveBetween(PosTable.MinSize, PosArea.CanvasHeight);
            table.RuleFor(x => x.X).GreaterThanOrEqualTo(0);
            table.RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
            table.RuleFor(x => x)
                .Must(x => x.X + x.Width <= PosArea.CanvasWidth && x.Y + x.Height <= PosArea.CanvasHeight)
                .WithName("Position")
                .WithMessage($"Every table must sit inside the {PosArea.CanvasWidth} by {PosArea.CanvasHeight} floor.");
        });
    }
}

public sealed class SavePosAreaLayoutCommandHandler(IAppDbContext db)
    : IRequestHandler<SavePosAreaLayoutCommand, PosFloorPlanDto>
{
    public async Task<PosFloorPlanDto> Handle(SavePosAreaLayoutCommand request, CancellationToken cancellationToken)
    {
        var area = await db.PosAreas
                .Include(x => x.Tables)
                .SingleOrDefaultAsync(x => x.Id == request.AreaId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Area not found.");

        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, area.BillingLocationId, cancellationToken);

        // A table's name is unique at its location, across every area (PosTable's remarks); the other
        // areas' names are the half the aggregate cannot see.
        var elsewhere = await db.PosTables
            .Where(x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == area.BillingLocationId
                && x.PosAreaId != area.Id)
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);
        var taken = request.Tables
            .Select(x => x.Name.Trim())
            .Where(name => elsewhere.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (taken.Count > 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Tables),
                $"'{till.Location.Name}' already has a table called {string.Join(", ", taken)} in another area. A table's name is unique at its location.")]);
        }

        // An occupied table cannot leave the floor: its order would be seated nowhere.
        var deactivated = request.Tables
            .Where(x => x.Id is not null && !x.IsActive)
            .Select(x => x.Id!.Value)
            .Where(id => area.Tables.Any(t => t.Id == id && t.IsActive))
            .ToList();
        if (deactivated.Count > 0)
        {
            var seated = await db.PosOrders
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PosOrderStatus.Open
                    && x.PosTableId != null && deactivated.Contains(x.PosTableId.Value))
                .Select(x => x.Code)
                .ToListAsync(cancellationToken);
            if (seated.Count > 0)
            {
                throw new ConflictException(
                    $"A table you made inactive has an open order ({string.Join(", ", seated)}). Settle or move it first.");
            }
        }

        IReadOnlyList<PosTable> added;
        try
        {
            added = area.ApplyLayout([.. request.Tables.Select(x => new PosTableLayout(
                x.Id, x.Name, x.Capacity, x.Shape, x.X, x.Y, x.Width, x.Height, x.IsActive))]);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Tables), ex.Message)]);
        }

        db.PosTables.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);

        return await PosFloorPlanReader.ReadAsync(db, request.OrganizationId, till.Location, cancellationToken);
    }
}
