using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>
/// Phase 64 -- what every restaurant request checks first, on top of <see cref="PosTill"/>'s refusals:
/// the location runs a <b>Restaurant</b> till, and the caller may act at it.
/// </summary>
internal static class PosRestaurant
{
    public static async Task<PosTillContext> LoadAsync(
        IAppDbContext db, Guid organizationId, Guid locationId, CancellationToken cancellationToken)
    {
        var till = await PosTill.LoadAsync(db, organizationId, locationId, cancellationToken);

        if (till.Location.PosMode != PosMode.Restaurant)
        {
            throw new ConflictException(
                $"'{till.Location.Name}' runs a {till.Location.PosMode} till, which has no tables or kitchen. "
                + "Set its POS mode to Restaurant under Configurations > Point of Sale.");
        }

        return till;
    }

    /// <summary>
    /// The branch boundary (docs/phase-64-status.md Decision F). <c>Pos.Order.Operate</c> is
    /// organization-wide, and an order is billed as an Invoice at its location, so acting on one needs
    /// <c>Sales.Invoice.Create</c> there -- phase 61's rule for opening a drawer, applied to a tab. A
    /// waiter scoped to one branch cannot open, send or serve another branch's orders.
    /// </summary>
    public static Task EnsureMayOrderAtAsync(
        IAppDbContext db, Guid organizationId, Guid userId, Guid locationId, CancellationToken cancellationToken) =>
        GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, organizationId, userId, PermissionKeys.InvoiceCreate, locationId, cancellationToken);

    /// <summary>The order with everything its quantities are summed from: lines, tickets, ticket lines.</summary>
    public static async Task<PosOrder> LoadOrderAsync(
        IAppDbContext db, Guid organizationId, Guid orderId, CancellationToken cancellationToken) =>
        await db.PosOrders
            .Include(x => x.Lines)
            .Include(x => x.Tickets).ThenInclude(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.OrganizationId == organizationId, cancellationToken)
        ?? throw new NotFoundException("Order not found.");

    /// <summary>
    /// Loads the order and refuses unless its till still takes orders and the caller may act there:
    /// the order's own location, never one the request names.
    /// </summary>
    public static async Task<(PosOrder Order, PosTillContext Till)> LoadOrderForActionAsync(
        IAppDbContext db, Guid organizationId, Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(db, organizationId, orderId, cancellationToken);
        var till = await LoadAsync(db, organizationId, order.BillingLocationId, cancellationToken);
        await EnsureMayOrderAtAsync(db, organizationId, userId, order.BillingLocationId, cancellationToken);
        return (order, till);
    }

    /// <summary>
    /// A table an order may be seated at: on this location's floor, active, in an active area. Whether
    /// it is free is the filtered unique index's answer, at save, not a read here.
    /// </summary>
    public static async Task<PosTable> LoadSeatableTableAsync(
        IAppDbContext db, Guid organizationId, Guid locationId, Guid tableId, string field,
        CancellationToken cancellationToken)
    {
        var row = await (
                from table in db.PosTables
                join area in db.PosAreas on table.PosAreaId equals area.Id
                where table.Id == tableId && table.OrganizationId == organizationId
                select new { table, AreaActive = area.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null || row.table.BillingLocationId != locationId)
        {
            throw new FluentValidation.ValidationException(
                [new FluentValidation.Results.ValidationFailure(field, "That table is not on this location's floor.")]);
        }

        if (!row.table.IsActive || !row.AreaActive)
        {
            throw new ConflictException($"Table {row.table.Name} is inactive, so no order can be seated at it.");
        }

        return row.table;
    }

    /// <summary>
    /// The friendly half of "one open order per table": a 409 naming the order already seated. The
    /// filtered unique index on <c>PosOrder</c> is the other half, for two waiters who both pass this
    /// read before either saves (OpenPosSession's arrangement).
    /// </summary>
    public static async Task EnsureTableFreeAsync(
        IAppDbContext db, Guid organizationId, PosTable table, CancellationToken cancellationToken)
    {
        if (await OpenOrderCodeAtAsync(db, organizationId, table.Id, cancellationToken) is { } code)
        {
            throw new ConflictException($"Table {table.Name} already has order {code} open. Open it, or seat the guests elsewhere.");
        }
    }

    /// <summary>Whose table it is, for the 409 a lost race on the unique index turns into.</summary>
    public static async Task<string?> OpenOrderCodeAtAsync(
        IAppDbContext db, Guid organizationId, Guid tableId, CancellationToken cancellationToken) =>
        await db.PosOrders
            .Where(x => x.OrganizationId == organizationId && x.PosTableId == tableId && x.Status == PosOrderStatus.Open)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);
}
