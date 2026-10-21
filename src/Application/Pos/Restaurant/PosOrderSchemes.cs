using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Discounts;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>
/// Phase 97 -- discount schemes on a restaurant order, judged at the order's location on the Nepal day the food is ordered
/// (<see cref="DiscountSchemeRates"/>). A dish starts at its scheme percent (<see cref="PosOrderPricing"/>), and the order's
/// own discount follows the slab its total reaches as dishes are sent and discarded, until the first bill fixes it or a
/// cashier sets it by hand (<see cref="PosOrder.ApplySchemeDiscount"/>).
/// </summary>
internal static class PosOrderSchemes
{
    public static DateOnly Today => NepalTime.LocalDate(DateTimeOffset.UtcNow);

    public static Task<DiscountSchemeRates> LoadAsync(
        IAppDbContext db, PosOrder order, CancellationToken cancellationToken) =>
        DiscountSchemeRates.LoadAsync(
            db, order.OrganizationId, order.BillingLocationId, Today, [.. order.Lines.Select(x => x.ProductId)],
            cancellationToken);

    /// <summary>After the order's food changed: its discount moves to the slab its total now reaches, where it may.</summary>
    public static async Task ApplySlabAsync(
        IAppDbContext db, PosOrder order, IReadOnlyDictionary<Guid, decimal> invoiced, CancellationToken cancellationToken)
    {
        if (order.DiscountSetByHand)
        {
            return;
        }

        var rates = await DiscountSchemeRates.LoadAsync(
            db, order.OrganizationId, order.BillingLocationId, Today, [], cancellationToken);
        PosOrderCommands.Run(() => order.ApplySchemeDiscount(rates.ForBillAmount, invoiced));
    }
}
