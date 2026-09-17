namespace ErpApp.Domain.Common;

/// <summary>
/// A quantity expressed in a product's <b>primary</b> unit -- the only unit
/// <c>StockLedgerEntry</c>, <c>StockMovement</c>, the GL and every stock report speak.
///
/// <para><b>Why this is a type and not a <c>decimal</c>.</b> Phase 51 threaded two arguments
/// through <c>IStockLedgerService</c> and got exactly half a sweep for free. <c>ConsumeAsync</c>'s
/// <i>return type</i> changed, so the compiler enumerated all five consume sites and that half was
/// correct from the first green build. <c>IncrementAsync</c> only gained <b>optional
/// parameters</b>, so nothing failed to compile -- and <c>ApprovePurchaseBillCommandHandler</c>
/// shipped un-swept past a clean build, 1,900 green tests and a correct detail DTO, creating an
/// un-batched layer for every receipt of a batch-tracked product. The general lesson was recorded
/// as <i>a sweep driven by the compiler stops exactly where the compiler stops</i>.
///
/// <para>A unit on the line is the same shape of change, so this phase does not rely on having
/// remembered. Making the ledger's quantity a distinct type means <b>every</b> call site that used
/// to pass a raw line quantity fails to compile and has to say, in code, which kind of quantity it
/// holds. There is no implicit conversion from <see cref="decimal"/>, by design: the compiler
/// error is the feature.</para>
///
/// <para>It also outlives this phase. A later phase adding another ledger-touching document cannot
/// reintroduce the bug, because it cannot reach <c>IncrementAsync</c> without answering the
/// question first.</para></para>
/// </summary>
public readonly record struct PrimaryQuantity
{
    private PrimaryQuantity(decimal value) => Value = value;

    /// <summary>The quantity, in the product's primary unit.</summary>
    public decimal Value { get; }

    /// <summary>Nothing moved. <c>IncrementAsync</c> and <c>ConsumeAsync</c> both treat this as a
    /// no-op, which is what a Service line reaching a stock path has always meant.</summary>
    public static PrimaryQuantity Zero => new(0m);

    /// <summary>True when nothing moved -- the guard every ledger path already had, now spelled
    /// on the value rather than on a loose decimal.</summary>
    public bool IsZero => Value == 0m;

    /// <summary>
    /// The line path: what the user typed, times the factor <b>frozen on that line</b> at
    /// Create/Update. Rounded once, at <see cref="UnitConversion.QuantityScale"/>.
    ///
    /// <para>This reads nothing from the catalogue, which is the whole point -- editing (or
    /// deleting) the product's unit row afterwards cannot reach back and change what an approved
    /// document did to stock. See <see cref="UnitConversion"/> for the live evidence.</para>
    /// </summary>
    public static PrimaryQuantity FromEntered(decimal quantityAsEntered, decimal conversionFactor) =>
        new(UnitConversion.ToPrimary(quantityAsEntered, conversionFactor));

    /// <summary>
    /// For a quantity that is <b>already</b> in the primary unit and was never entered in another.
    /// Legitimate in exactly three situations, and the name is deliberately awkward so a fourth has
    /// to argue for itself:
    /// <list type="bullet">
    /// <item>a <b>reversal</b> reading a figure back off the layer it is undoing (a Void restocking
    /// at <c>CogsUnitCost</c>, <c>ReverseIncrementAsync</c>'s own reads) -- the layer was written in
    /// primary units, so converting it again would double-apply the factor, which is
    /// <see cref="ExchangeRates"/>'s never-convert-twice rule in another currency;</item>
    /// <item>a <b>serialised</b> unit, which is a layer of quantity one by definition (phase 51);</item>
    /// <item>a line type that <b>carries no unit</b>, whose quantity is therefore primary by
    /// construction -- Opening Stock, Inventory Adjustment and the Production Journal's three
    /// collections, all four of which the 2026-09-17 read could not see because that tenant had
    /// none of them.</item>
    /// </list>
    /// </summary>
    public static PrimaryQuantity AlreadyPrimary(decimal quantity) => new(quantity);

    public override string ToString() => Value.ToString();
}
