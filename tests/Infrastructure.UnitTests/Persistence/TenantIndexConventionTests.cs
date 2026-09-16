using ErpApp.Infrastructure.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ErpApp.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Phase 50 — the convention's own classification, asserted where it can be seen.
///
/// <para>Most of this convention already fails the model build when it is wrong, which is stronger
/// than a test and needs none: an unindexed tenant-scoped entity throws, and as of this phase so
/// does one carrying a required date nobody has classified. <see cref="ModelFixture"/> builds the
/// real model, so <b>every test in this project inherits those two guards</b> — if either fires,
/// the whole class fails in its constructor with the offending entity named.</para>
///
/// <para>What is left for a test is the part a throw cannot express: that the entities excused from
/// the date rule are still in the state their excuse claims. An allow-list entry whose reason has
/// quietly stopped being true is phase 34a's failure mode, and phase 45's sharper version of it — a
/// reason can be the argument for the opposite conclusion.</para>
/// </summary>
[Collection(nameof(ModelCollection))]
public class TenantIndexConventionTests(ModelFixture fixture)
{
    /// <summary>
    /// The three entities <c>DatesThatAreNotBusinessDates</c> excuses, and the claim each excuse
    /// makes. Restated here rather than read from the convention, deliberately: a test that read the
    /// dictionary would assert the dictionary equals itself. These are the facts the reasons assert,
    /// written down a second time, so a divergence is a failure rather than a coincidence.
    /// </summary>
    public static TheoryData<string, string> ExcusedDates => new()
    {
        // "every reader is keyed by product and warehouse first, which is what the hand-written
        // (OrganizationId, ProductId, WarehouseId, TransactionDate) composite serves"
        { "StockLedgerEntry", "ProductId" },
        { "StockMovement", "ProductId" },

        // "the row carries (OrganizationId, CreatedAt) for the audit list that does"
        { "AlertSendLog", "CreatedAt" },
    };

    [Theory]
    [MemberData(nameof(ExcusedDates))]
    public void An_entity_excused_from_the_date_rule_is_still_covered_the_way_its_reason_says(
        string entityName, string secondColumn)
    {
        var entity = fixture.Entity(entityName);

        Assert.True(
            ModelFixture.HasIndexLeadingOn(entity, secondColumn),
            $"TenantIndexConvention.DatesThatAreNotBusinessDates excuses {entityName} on the grounds "
            + $"that it is read through (OrganizationId, {secondColumn}, ...) instead. That index is "
            + "gone, so the excuse now describes something that is not there and the entity is "
            + "scanning per tenant with nobody watching.");
    }

    /// <summary>
    /// The two the mirror question turned up, and the reason phase 50 calls them luck. Neither was
    /// broken — but neither was <i>covered by the rule</i>: each fell through the name match into the
    /// master-data branch and was waved through by <c>HasLeadingTenantIndex</c> because of a
    /// composite somebody wrote by hand three phases earlier. Had that composite led on anything
    /// else, the convention would have said nothing.
    /// </summary>
    [Fact]
    public void The_stock_tables_were_covered_by_a_hand_written_composite_and_not_by_the_name_rule()
    {
        foreach (var name in new[] { "StockLedgerEntry", "StockMovement" })
        {
            var entity = fixture.Entity(name);

            Assert.False(
                ModelFixture.HasIndexLeadingOn(entity, "TransactionDate"),
                $"{name} has acquired an (OrganizationId, TransactionDate) index. That is a decision, "
                + "not a tidy-up: phase 50 recorded that nothing reads these tables by tenant and date "
                + "alone, so if a reader now does, move the entity into DeclaredBusinessDates and "
                + "measure it the way the Cheque Register was measured.");

            Assert.Contains(
                entity.GetIndexes(),
                i => i.Properties.Select(p => p.Name)
                    .SequenceEqual(["OrganizationId", "ProductId", "WarehouseId", "TransactionDate"]));
        }
    }

    /// <summary>
    /// Every document table carries the range index, which is the half of the convention that ships
    /// eleven GL-posting readers and every dated register. Asserted over the derived set rather than
    /// a list of fourteen names.
    /// </summary>
    [Fact]
    public void Every_tenant_scoped_entity_with_a_business_date_carries_the_range_index()
    {
        var missing = new List<string>();

        foreach (var entity in fixture.Model.GetEntityTypes().Where(IsTenantScoped))
        {
            var date = entity.GetProperties()
                .FirstOrDefault(p => p.ClrType == typeof(DateOnly) && !p.IsNullable && p.Name == "Date")
                ?.Name;

            if (date is not null && !ModelFixture.HasIndexLeadingOn(entity, date))
            {
                missing.Add(entity.ClrType.Name);
            }
        }

        Assert.True(missing.Count == 0, "No (OrganizationId, Date) index on: " + string.Join(", ", missing));
    }

    private static bool IsTenantScoped(IEntityType entity) =>
        !entity.IsOwned() && entity.FindProperty("OrganizationId") is not null;
}
