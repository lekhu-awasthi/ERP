using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 52 -- turns the unit a client named on a document line into the pair the aggregate
/// freezes: the unit's own id, and <b>how many primary units one of them is worth at the moment
/// the line is written</b>.
///
/// <para><b>Why the factor is resolved here and never again.</b> Confirmed live 2026-09-17 on the
/// reference tenant: an approved Purchase Bill for <c>2 BTL</c> of a product whose Carton-to-Piece
/// rate was 12 had put <c>24</c> into the ledger; the product's BTL row was then deleted outright
/// and re-added at 6, and the bill still read <c>2 BTL</c> against <c>24</c> primary units. A live
/// lookup would have said 12. So the catalogue is read exactly once, at Create/Update, and the
/// document owns the answer from then on. See <see cref="UnitConversion"/>.</para>
///
/// <para><b>Why this is not part of <see cref="DocumentLineAllocationWriter"/>.</b> They look
/// alike -- both turn what a client sent into what a line stores -- and they are deliberately
/// separate, because a shared class would be one whose two halves never both apply. An allocation
/// is <i>minted</i> (a receipt naming <c>BATCH123</c> brings that batch into being), exists only
/// for a product whose tracking flags are on, and changes which FIFO layer is chosen. A unit is
/// <i>validated</i> against a matrix that must already exist, is present on every line of every
/// product, and changes only how much stock moves. They also cover different sets: four line types
/// carry an allocation and eight carry a unit.</para>
///
/// <para>CLAUDE.md's rule is to retire copies 1..N before writing copy N+1, so this is the one
/// place a line's factor is derived -- all sixteen Create/Update handlers call it, and no handler
/// reads <c>ProductSecondaryUnit</c> for itself.</para>
/// </summary>
public static class DocumentLineUnitResolver
{
    /// <summary>What a client sent for one line, in the order the lines were sent. A null
    /// <paramref name="UnitId"/> means "the product's own primary unit", which is what every line
    /// created before this phase meant and what a client that does not send the field means.</summary>
    public sealed record LineUnitInput(Guid ProductId, Guid? UnitId);

    /// <summary>The pair frozen onto one line.</summary>
    public sealed record ResolvedUnit(Guid? UnitId, decimal ConversionFactor);

    /// <summary>A line entered in the product's own primary unit -- the identity conversion.</summary>
    public static ResolvedUnit Primary { get; } = new(null, UnitConversion.PrimaryFactor);

    /// <summary>
    /// The short name of each unit named, keyed by id -- what a detail DTO renders beside the
    /// quantity (<c>2 BTL</c>), and what the reference product's own grid shows.
    ///
    /// <para>Read back through this one method rather than per handler so eight detail queries
    /// cannot drift in how they answer the same question -- the call
    /// <c>LineAllocationReader.LoadBatchesAsync</c> makes for the batch half. Units live in this
    /// class on both the write and read sides deliberately: splitting them across a resolver and a
    /// reader would put one concept in two places, which is the copy-N+1 shape CLAUDE.md warns
    /// about.</para>
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> LoadUnitNamesAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<Guid?> unitIds,
        CancellationToken cancellationToken)
    {
        var ids = unitIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);
    }

    /// <summary>
    /// Resolves every line positionally. Throws <see cref="ValidationException"/> -- a 400 naming
    /// the offending line -- rather than letting a Domain invariant surface as a 500 (phase 39),
    /// which is the same call <see cref="DocumentLineAllocationWriter"/> makes.
    /// </summary>
    public static async Task<IReadOnlyList<ResolvedUnit>> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<LineUnitInput> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        // A line that named no unit needs nothing loaded at all, which is every line on every
        // tenant that has not set up a unit matrix -- the overwhelmingly common case, and the
        // reason this short-circuits rather than always querying.
        if (lines.All(x => x.UnitId is null))
        {
            return [.. lines.Select(_ => Primary)];
        }

        var productIds = lines.Select(x => x.ProductId).Distinct().ToList();

        var products = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PrimaryUnitId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var secondaries = await db.ProductSecondaryUnits
            .Where(x => productIds.Contains(x.ProductId))
            .Select(x => new { x.ProductId, x.UnitId, x.ConversionRate })
            .ToListAsync(cancellationToken);

        var byProductUnit = secondaries.ToDictionary(x => (x.ProductId, x.UnitId), x => x.ConversionRate);

        var resolved = new ResolvedUnit[lines.Count];
        var failures = new List<ValidationFailure>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.UnitId is not { } unitId)
            {
                resolved[i] = Primary;
                continue;
            }

            if (!products.TryGetValue(line.ProductId, out var product))
            {
                // Existence is somebody else's job (ProductVariantRules) and has already run, or is
                // about to. Skipping keeps the two error reports from contradicting each other --
                // the same call DocumentLineAllocationWriter makes for the same reason.
                resolved[i] = Primary;
                continue;
            }

            // Naming the primary unit explicitly is legal and is not a secondary-unit lookup. The
            // reference product's own dropdown lists the primary alongside the secondaries, so a
            // client echoing back what it was shown must not be an error. The id is kept rather
            // than normalised to null so a detail DTO renders the unit the user actually chose.
            if (unitId == product.PrimaryUnitId)
            {
                resolved[i] = new ResolvedUnit(unitId, UnitConversion.PrimaryFactor);
                continue;
            }

            if (!byProductUnit.TryGetValue((line.ProductId, unitId), out var factor))
            {
                failures.Add(new ValidationFailure(
                    $"Lines[{i}]",
                    "That unit is not on this product's unit list, so there is no conversion rate "
                    + "to apply. Choose the product's primary unit or one of its secondary units."));
                continue;
            }

            resolved[i] = new ResolvedUnit(unitId, UnitConversion.Validate(factor));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return resolved;
    }
}
