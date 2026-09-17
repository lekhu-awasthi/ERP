namespace ErpApp.Domain.Common;

/// <summary>
/// The single conversion point between the unit a document line was <b>entered</b> in and the
/// product's <b>primary</b> unit, which is the only unit the FIFO ledger, the GL and every stock
/// report speak. Deliberately the same shape as <see cref="ExchangeRates"/> and
/// <see cref="NepalTime"/>: one tiny static class the whole codebase funnels through, so the
/// rounding rule cannot drift between the eight line types that carry a unit.
///
/// <para><b>What the live read settled (2026-09-17, Moonbeam UAT).</b> The reference product
/// applies the factor <i>on the way in</i> and then freezes it. A Purchase Bill approved for
/// <c>2 BTL</c> of a product whose Carton-to-Piece rate was 12 produced a stock movement carrying
/// <c>quantity 2, unit BTL, primary_quantity 24, ValuationRate 100</c>. The rate was then changed
/// to 6 -- and, because the vendor offers no edit, that meant <b>deleting the unit row entirely</b>
/// and re-adding it. The approved bill still read <c>2 BTL @ 1200</c> and the movement still read
/// <c>primary_quantity 24</c>. A live lookup would have said 12. So the conversion's <i>effect</i>
/// is part of the document's own history, not a view of the catalogue.</para>
///
/// <para><b>Why the line stores the factor and not the converted quantity -- a deliberate
/// divergence.</b> The vendor stores the product (<c>primary_quantity</c>). This codebase stores
/// <c>ConversionFactor</c> and derives the quantity through <see cref="ToPrimary"/>, which gets the
/// same immutability for one fewer column: both inputs are frozen on the same row, so the
/// derivation reads nothing that can change underneath it. Storing the product as well would be a
/// <i>second quantity</i> that <c>Quantity</c> and the factor can contradict -- precisely the shape
/// phase 51 refused for <c>ProductBatch</c> and phase 37 found drifting between three views. The
/// factor is also the half a human reads on a printed bill ("1 CTN = 12 PIS"); the product is
/// not.</para>
///
/// <para><b>What is deliberately NOT converted.</b> The <b>money</b>. Confirmed live in both
/// directions: choosing a secondary unit sets the line's Rate from that unit row's own selling or
/// purchase price, and Amount stays <c>Quantity x Rate</c> in the entered unit -- 2 BTL at 1200 is
/// 2,400, not 28,800. A secondary unit is two independent things at once, a price-book row and a
/// stock conversion, and they do not interact. Any code multiplying an amount by a conversion
/// factor is wrong.</para>
/// </summary>
public static class UnitConversion
{
    /// <summary>
    /// Scale of a quantity in the primary unit. Four, because that is exactly the precision of the
    /// columns it lands in -- <c>StockLedgerEntry.QuantityIn</c>, <c>QuantityRemaining</c> and
    /// <c>StockMovement.Quantity</c> are all <c>decimal(18,4)</c> -- so rounding here rounds once,
    /// at the boundary, rather than letting the database do it invisibly a layer later.
    /// </summary>
    public const int QuantityScale = 4;

    /// <summary>
    /// Scale a conversion factor is stored and quoted at. Six, for <see cref="ExchangeRates.RateScale"/>'s
    /// reason: a factor is a ratio, and six places covers a fine one without inviting precision
    /// nobody measures to.
    /// </summary>
    public const int FactorScale = 6;

    /// <summary>
    /// The factor a line entered in the product's own primary unit always carries. A primary-unit
    /// line is not a special case anywhere in the code -- it is simply a line whose factor is one,
    /// so <see cref="ToPrimary"/> is an identity for it and no branch is needed. This is the same
    /// call <see cref="ExchangeRates.BaseRate"/> makes.
    /// </summary>
    public const decimal PrimaryFactor = 1m;

    /// <summary>
    /// Converts a quantity as the user entered it into the product's primary unit, rounded once to
    /// <see cref="QuantityScale"/>. Away-from-zero to match every other rounding in this codebase.
    /// </summary>
    public static decimal ToPrimary(decimal quantityAsEntered, decimal conversionFactor) =>
        Math.Round(quantityAsEntered * conversionFactor, QuantityScale, MidpointRounding.AwayFromZero);

    /// <summary>Normalises a factor to <see cref="FactorScale"/> so the value frozen on a line is
    /// the same value every later conversion uses.</summary>
    public static decimal NormaliseFactor(decimal conversionFactor) =>
        Math.Round(conversionFactor, FactorScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The one invariant every line-bearing aggregate shares: a conversion factor is strictly
    /// positive. It is deliberately <b>not</b> required to be at least one -- the reference tenant
    /// has a product (<c>Maida 50 kg</c>, primary <c>bag</c>) whose secondary unit <c>NOS</c>
    /// converts at <b>0.02</b>, so a smaller-than-primary unit is ordinary and a
    /// <c>factor &gt;= 1</c> rule would reject real data.
    /// </summary>
    public static decimal Validate(decimal? conversionFactor)
    {
        var factor = conversionFactor ?? PrimaryFactor;

        if (factor <= 0)
        {
            throw new InvalidOperationException(
                "A line's unit conversion factor must be greater than zero.");
        }

        return NormaliseFactor(factor);
    }
}
