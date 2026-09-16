using ErpApp.Infrastructure.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ErpApp.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Phase 51 — the two new tenant-scoped entities, asserted against the traps phase 50 laid.
///
/// <para><see cref="ModelFixture"/> builds the real model, so this class already inherits the
/// convention's two throws: an unindexed tenant-scoped entity, and one carrying a required date
/// nobody has classified. What follows is the part a throw cannot express.</para>
///
/// <para><b>Why the batch's dates being nullable needs a test at all.</b> They are nullable because
/// the read establishes neither as required and a lot number with no expiry is ordinary — that is a
/// modelling decision, taken on its merits. A <i>consequence</i> of it is that
/// <c>TenantIndexConvention.RequiredDateProperty</c> never looks at them, so <c>ProductBatch</c>
/// falls into the master-data branch. Phase 50's whole finding was that
/// <c>StockLedgerEntry</c> and <c>StockMovement</c> passed that same branch <b>by luck</b> — a
/// hand-written composite happened to lead with <c>OrganizationId</c> and nobody knew a
/// classification had been missed. So the two halves are pinned separately here: that the dates are
/// nullable, and that the tenant index exists. A later phase making either date required will be
/// told, by a failing test naming this file, that it now owes the convention a declaration in
/// <c>DeclaredBusinessDates</c> or an excuse in <c>DatesThatAreNotBusinessDates</c>.</para>
/// </summary>
[Collection(nameof(ModelCollection))]
public class TraceabilityModelTests(ModelFixture fixture)
{
    [Fact]
    public void ProductBatch_carries_no_required_date_so_it_classifies_as_master_data()
    {
        var entity = fixture.Entity("ProductBatch");

        var requiredDates = entity.GetProperties()
            .Where(p => p.ClrType == typeof(DateOnly) && !p.IsNullable)
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            requiredDates.Count == 0,
            "ProductBatch now carries a required DateOnly: " + string.Join(", ", requiredDates) + ". "
            + "TenantIndexConvention classifies such an entity as a document and will fail the model "
            + "build until it is declared in DeclaredBusinessDates or excused in "
            + "DatesThatAreNotBusinessDates. Decide which it is -- phase 51 made both dates nullable "
            + "on purpose, and this test is the notice that the decision has changed.");
    }

    [Fact]
    public void ProductBatch_is_reachable_by_tenant_by_rule_and_not_by_luck()
    {
        var entity = fixture.Entity("ProductBatch");

        // The per-tenant uniqueness rule IS the tenant index here, exactly as it is for every other
        // master-data table -- phase 34c's finding that the eighteen unindexed tables were precisely
        // the ones with no uniqueness constraint.
        var unique = entity.GetIndexes().SingleOrDefault(i => i.IsUnique);

        Assert.NotNull(unique);
        Assert.Equal(
            new[] { "OrganizationId", "ProductId", "BatchNo" },
            unique!.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void DocumentLineSerial_is_reachable_by_tenant()
    {
        var entity = fixture.Entity("DocumentLineSerial");

        Assert.Contains(
            entity.GetIndexes(),
            i => i.Properties[0].Name == "OrganizationId");
    }

    /// <summary>
    /// The filtered unique index is the whole of serial uniqueness, and both halves of its filter
    /// are load-bearing in different ways — so both are asserted, because a filter that silently
    /// lost a clause would still be a valid index and would still pass every handler test (InMemory
    /// enforces neither half).
    /// </summary>
    [Fact]
    public void A_serial_is_unique_among_in_stock_layers_only()
    {
        var entity = fixture.Entity("StockLedgerEntry");

        var index = entity.GetIndexes().SingleOrDefault(i =>
            i.IsUnique && i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "OrganizationId", "ProductId", "SerialNo" }));

        Assert.NotNull(index);

        var filter = index!.GetFilter();
        Assert.NotNull(filter);

        // Without this, every non-serialised layer in the tenant collides with every other:
        // SQL Server treats NULLs as equal in a unique index (CLAUDE.md's standing rule).
        Assert.Contains("[SerialNo] IS NOT NULL", filter!, StringComparison.Ordinal);

        // Without this, a serial that is issued and later returned by a Credit Note could never
        // re-enter stock -- the old, fully-consumed layer would still hold the number. Uniqueness
        // has to be over what is in stock, not over all of history.
        Assert.Contains("[QuantityRemaining] > 0", filter!, StringComparison.Ordinal);
    }
}
