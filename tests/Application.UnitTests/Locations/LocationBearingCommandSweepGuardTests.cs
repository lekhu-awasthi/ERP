using System.Reflection;
using ErpApp.Application.Common.Locations;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 32's command-side sweep guard -- the counterpart of
/// <c>LocationBearingAggregateSweepGuardTests</c>, and the half that actually catches the likely
/// mistake. An aggregate can carry <c>LocationId</c> perfectly well while its Create command has no
/// way to set one; nothing fails, and that document type silently records no location forever.
///
/// <para>So this asserts, by reflection over the Application assembly, that every one of
/// <see cref="DocumentMechanisms.LocationBearing"/>'s 17 types has a command able to write the
/// field -- and, in the other direction, that nothing implements
/// <see cref="ILocationBearingCommand"/> without belonging to that list. Two directions, phase-30's
/// rule: a guard asserted one way only proves half of what it claims.</para>
/// </summary>
public class LocationBearingCommandSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(ILocationBearingCommand).Assembly;

    private static IReadOnlyList<Type> LocationBearingCommands => [.. ApplicationAssembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ILocationBearingCommand).IsAssignableFrom(t))
        .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>
    /// The command-name stem for each location-bearing type. The two opening-balance kinds are a
    /// single <c>CreateOrUpdate...</c> command apiece rather than a Create/Update pair, because
    /// neither has a Draft/Approve lifecycle -- they are keyed by their own natural key and edited
    /// in place, which is why <see cref="RequiresCreateAndUpdatePair"/> excludes them below.
    /// </summary>
    private static readonly IReadOnlyDictionary<DocumentType, string> CommandStems =
        new Dictionary<DocumentType, string>
        {
            [DocumentType.Quotation] = "Quotation",
            [DocumentType.SalesOrder] = "SalesOrder",
            [DocumentType.Invoice] = "Invoice",
            [DocumentType.CreditNote] = "CreditNote",
            [DocumentType.Payment] = "Payment",
            [DocumentType.PurchaseOrder] = "PurchaseOrder",
            [DocumentType.PurchaseBill] = "PurchaseBill",
            [DocumentType.Expense] = "Expense",
            [DocumentType.DebitNote] = "DebitNote",
            [DocumentType.JournalVoucher] = "JournalVoucher",
            [DocumentType.CashTransfer] = "CashTransfer",
            [DocumentType.WarehouseTransfer] = "WarehouseTransfer",
            [DocumentType.InventoryAdjustment] = "InventoryAdjustment",
            [DocumentType.ProductionOrder] = "ProductionOrder",
            [DocumentType.ProductionJournal] = "ProductionJournal",
            [DocumentType.OpeningBalance] = "OpeningBalanceLine",
            [DocumentType.OpeningStock] = "OpeningStockLine",
        };

    private static bool RequiresCreateAndUpdatePair(DocumentType documentType) =>
        documentType is not (DocumentType.OpeningBalance or DocumentType.OpeningStock);

    public static TheoryData<DocumentType> LocationBearingTypes => [.. DocumentMechanisms.LocationBearing];

    [Fact]
    public void Every_location_bearing_document_type_has_a_command_stem()
    {
        Assert.Equal(
            DocumentMechanisms.LocationBearing.OrderBy(x => x),
            CommandStems.Keys.OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(LocationBearingTypes))]
    public void Has_commands_that_can_write_a_location(DocumentType documentType)
    {
        var stem = CommandStems[documentType];
        var names = LocationBearingCommands.Select(t => t.Name).ToList();

        if (RequiresCreateAndUpdatePair(documentType))
        {
            Assert.Contains($"Create{stem}Command", names);
            Assert.Contains($"Update{stem}Command", names);
        }
        else
        {
            Assert.Contains($"CreateOrUpdate{stem}Command", names);
        }
    }

    /// <summary>
    /// The other direction: nothing implements the marker that is not one of the 32 commands the
    /// 17 types above account for. Catches a command that was given the interface by copy-paste onto
    /// a type that has no location column to write into.
    /// </summary>
    [Fact]
    public void Nothing_outside_the_sweep_implements_the_marker()
    {
        var expected = DocumentMechanisms.LocationBearing
            .SelectMany(documentType =>
            {
                var stem = CommandStems[documentType];
                return RequiresCreateAndUpdatePair(documentType)
                    ? new[] { $"Create{stem}Command", $"Update{stem}Command" }
                    : [$"CreateOrUpdate{stem}Command"];
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, LocationBearingCommands.Select(t => t.Name).ToList());
    }

    [Theory]
    [MemberData(nameof(LocationBearingTypes))]
    public void The_location_is_optional_on_every_command(DocumentType documentType)
    {
        var stem = CommandStems[documentType];

        foreach (var command in LocationBearingCommands.Where(t => t.Name.Contains(stem, StringComparison.Ordinal)))
        {
            var property = command.GetProperty(nameof(ILocationBearingCommand.LocationId));

            // Nullable throughout: null means "the tenant's default", never "no location". Making it
            // required would break every existing caller and every single-location client, which is
            // the compatibility property phase 28's null-means-base-currency established.
            Assert.NotNull(property);
            Assert.Equal(typeof(Guid?), property.PropertyType);
        }
    }
}
