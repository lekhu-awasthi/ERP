using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 51 -- where a batch or a serial may be named, where it must be, and where a tracked
/// product is refused outright. This class <b>is</b> the phase's scope boundary, written down once
/// rather than distributed across nine handlers as a habit.
///
/// <para><b>The rule, not a sampled list.</b> Phase 30's lesson is that a list sampled from a few
/// screens becomes a wrong list, and that the fix is to find the rule. The rule here is: <i>the
/// control is where the 2026-09-16 read put it -- the Invoice and Purchase Bill line grids -- and
/// every other stock path either derives the allocation from its source or refuses a tracked
/// product with a named 409.</i> That settles the paths the read never showed a control on without
/// guessing at each one, and it is asserted in both directions by
/// <c>StockTrackingSweepGuardTests</c>.</para>
///
/// <para><b>The three groups.</b>
/// <list type="bullet">
/// <item><b>Named</b> -- Invoice and Purchase Bill lines carry <c>BatchId</c> and a serial list.</item>
/// <item><b>Derived</b> -- a Credit Note line prefills from the Invoice line it returns and a Debit
/// Note line from the Purchase Bill line it returns (phase 19's <c>ExpenditureClassification</c>
/// precedent, which resolves a Debit Note line's attributes from the source bill's matching line);
/// a Warehouse Transfer preserves whatever it consumed, relief by relief, and so needs no field at
/// all.</item>
/// <item><b>Refused</b> -- see <see cref="RefusedPaths"/>, each with its reason.</item>
/// </list></para>
/// </summary>
public static class StockTrackingRules
{
    /// <summary>
    /// The stock paths that refuse a batch- or serial-tracked product, with the reason each one
    /// cannot carry the allocation yet. These are named with reasons rather than left implicit
    /// because phase 46's lesson is that a guard predicate naming a <i>dependency</i> is not naming
    /// the behaviour: an exclusion has to say what it is excluding and why, and be asserted to still
    /// exist.
    ///
    /// <para>Every one of them is a path that <b>creates or destroys</b> a layer with no source
    /// document to inherit an allocation from, and for which the read showed no control. Allowing
    /// them through would create exactly the bucket Decision D refuses on a document line: stock of
    /// a batch-tracked product belonging to no batch, which reconciles against nothing (phase 24's
    /// variant-parent argument).</para>
    ///
    /// <para><b>Re-entry condition</b>, recorded rather than left as an omission: each of these
    /// wants the same <c>Item Batch</c> column the Invoice and Purchase Bill grids now have, and the
    /// work is the control plus one argument at the existing <c>IStockLedgerService</c> call. The
    /// reason it is not in this phase is that the read specifies neither the control's placement nor
    /// whether the vendor allows these documents on a tracked product at all -- and phase 45's
    /// lesson is not to decide an interaction nobody has posed.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RefusedPaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Opening Stock"] =
                "An opening stock line is keyed (Product, Warehouse) and unique on it, so a batch "
                + "would have to join that key -- a unique-index change on a populated table, and a "
                + "nullable column in a unique index is the at-most-one-sentinel trap phase 32 hit. "
                + "The ordinary answer is a back-dated Purchase Bill, which carries the batch today.",
            ["Inventory Adjustment"] =
                "An Increase line creates a layer at a user-entered cost with no source document to "
                + "inherit a batch from, and a Decrease line relieves one. The read showed no Item "
                + "Batch column here and phase 27a found this type has no Custom Fields section "
                + "either, so nothing says how the vendor handles it.",
            ["Production Journal"] =
                "Both sides need one: a batch-tracked raw material must say which batch was "
                + "consumed and a batch-tracked finished good must be given a new batch number. That "
                + "is two controls and a by-product rule, and the reference tenant runs periodic "
                + "inventory so its production journals post nothing to compare against (phase 25).",
        };

    /// <summary>
    /// The refusal itself. Called by every handler on a <see cref="RefusedPaths"/> path, before it
    /// reaches <c>IStockLedgerService</c>.
    ///
    /// <para>A 409 rather than a silent pass, because the failure it prevents is invisible: an
    /// un-batched layer for a batch-tracked product looks exactly like stock until a batch report
    /// disagrees with the product's own on-hand, and by then the documents are approved.</para>
    /// </summary>
    public static async Task EnsureNotTrackedAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<Guid> productIds,
        string pathName,
        CancellationToken cancellationToken)
    {
        if (!RefusedPaths.ContainsKey(pathName))
        {
            throw new InvalidOperationException(
                $"'{pathName}' is not one of StockTrackingRules.RefusedPaths -- add it there with its "
                + "reason, or call the allocation-carrying overload instead.");
        }

        var distinct = productIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var tracked = await db.Products
            .Where(x => x.OrganizationId == organizationId && distinct.Contains(x.Id)
                && (x.BatchTracking || x.SerialTracking))
            .Select(x => new { x.Name, x.BatchTracking })
            .FirstOrDefaultAsync(cancellationToken);

        if (tracked is null)
        {
            return;
        }

        var dimension = tracked.BatchTracking ? "batch-tracked" : "serial-tracked";

        throw new ConflictException(
            $"'{tracked.Name}' is {dimension}, and {pathName} cannot record which "
            + $"{(tracked.BatchTracking ? "batch" : "serial numbers")} it moves. "
            + "Use a Purchase Bill or an Invoice, which carry the allocation.");
    }

    /// <summary>
    /// Validates one line's allocation against its product's flags, for a path that <i>does</i>
    /// carry it. Returns the refusal message, or null when the line is valid.
    ///
    /// <para>Returned rather than thrown so a validator can attach it to the right field and
    /// produce a 400 that names one, instead of a Domain invariant surfacing as a 500 that tells the
    /// caller nothing (phase 39). The Domain keeps its own backstop.</para>
    /// </summary>
    /// <param name="isReceipt">
    /// True for a path that creates stock (a Purchase Bill line, a Credit Note's restock), false for
    /// one that consumes it. This is the asymmetry Decision D turns on: a receipt <b>must</b> name
    /// the batch, an issue <b>may</b>.
    /// </param>
    public static string? ValidateLineAllocation(
        Product product,
        Guid? batchId,
        IReadOnlyCollection<string> serialNumbers,
        bool isReceipt)
    {
        if (!product.BatchTracking && batchId is not null)
        {
            return $"'{product.Name}' is not batch-tracked, so a line for it cannot name a batch.";
        }

        if (!product.SerialTracking && serialNumbers.Count > 0)
        {
            return $"'{product.Name}' is not serial-tracked, so a line for it cannot name serial numbers.";
        }

        if (product.BatchTracking && isReceipt && batchId is null)
        {
            return $"'{product.Name}' is batch-tracked, so a line receiving it must name a batch.";
        }

        if (product.SerialTracking && serialNumbers.Count == 0)
        {
            return $"'{product.Name}' is serial-tracked, so a line for it must name its serial numbers.";
        }

        if (product.SerialTracking)
        {
            var duplicate = serialNumbers
                .GroupBy(x => x.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate is not null)
            {
                return $"Serial number '{duplicate.Key}' is named twice on the same line.";
            }
        }

        return null;
    }

    /// <summary>
    /// The quantity rule for a serialised line, kept apart from
    /// <see cref="ValidateLineAllocation"/> because it needs the line's quantity and the message
    /// names a different field.
    /// </summary>
    public static string? ValidateSerialCount(Product product, decimal quantity, int serialCount)
    {
        if (!product.SerialTracking)
        {
            return null;
        }

        if (quantity != Math.Truncate(quantity))
        {
            return $"'{product.Name}' is serial-tracked, so its quantity must be a whole number of units.";
        }

        return quantity != serialCount
            ? $"'{product.Name}' is serial-tracked, so the line needs exactly {quantity} serial number(s), "
                + $"and {serialCount} were given."
            : null;
    }
}
