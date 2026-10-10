using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 77 -- what an order's bills took, as <see cref="PosOrder.Transfer"/> and <see cref="PosOrder.MarkTakeAway"/>
/// read it: each line's quantity and its share of the order's Amount discount. Most tests bill only quantities.
/// </summary>
internal static class PosBills
{
    public static readonly IReadOnlyDictionary<Guid, PosOrderLineBilled> None = new Dictionary<Guid, PosOrderLineBilled>();

    public static IReadOnlyDictionary<Guid, PosOrderLineBilled> Of(IReadOnlyDictionary<Guid, decimal> quantities) =>
        quantities.ToDictionary(x => x.Key, x => new PosOrderLineBilled(x.Value, PosLineArithmetic.Figures.Zero));
}
