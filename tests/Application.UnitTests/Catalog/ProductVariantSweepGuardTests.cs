using System.Text.RegularExpressions;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// Phase 24, <b>Decision D -- proving the sweep is complete, mechanically.</b>
///
/// Phase 24's whole server-side sweep is one rule: a request that names a ProductId must refuse a
/// variant *parent* (see ProductVariantRules). Because a variant IS a Product, that is the only
/// thing every line-taking handler had to learn -- but "only one thing" is exactly the kind of
/// claim that rots. The failure mode is Phase 25 adding a Production Journal that takes raw-material
/// ProductIds and forgetting the check, which no compiler and no existing test would catch: stock
/// would simply start accumulating on a bucket nothing can ever sell.
///
/// A paragraph of intent does not survive that. This does: it reads every command handler in the
/// Application layer off disk at test time and fails the build on a new one that takes product ids
/// from its request without going through the rule. An exemption must be added below <i>with its
/// reason</i>, which makes it a deliberate, reviewed act rather than silent drift.
///
/// Modelled directly on phase-23's web/src/app/shared/formatting/sweep-guard.spec.ts, including its
/// two self-checks: that the scan found files at all, and that every exemption still points at a
/// real file.
/// </summary>
public class ProductVariantSweepGuardTests
{
    /// <summary>
    /// Handlers that legitimately name a ProductId without putting one on a document line, each
    /// with the reason it is exempt.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["Catalog/Commands/CreateProductVariant/CreateProductVariantCommandHandler.cs"] =
            "Operates on the parent deliberately -- creating a variant OF it is the whole point.",
        ["Catalog/Commands/GenerateProductVariants/GenerateProductVariantsCommandHandler.cs"] =
            "Same: the parent is the subject, not a line item.",
        ["Catalog/Commands/SetProductVariantAttributes/SetProductVariantAttributesCommandHandler.cs"] =
            "Sets the parent's own attribute pool; it is what makes a product a parent.",
        ["Catalog/Commands/UpdateProductVariant/UpdateProductVariantCommandHandler.cs"] =
            "Edits a variant child's own fields. Rejects a non-variant itself.",
        ["Catalog/Commands/DeleteProductVariant/DeleteProductVariantCommandHandler.cs"] =
            "Deletes a variant child. Rejects a non-variant itself.",

        // Phase 51. Both of these read the product ids of an ALREADY-CREATED document's lines, not
        // ids from their own request -- the variant refusal happens at Create, which is where a
        // product id enters the system, and that is phase 24's design rather than an oversight here.
        //
        // They are listed now because phase 51 gave each of them a
        // StockTrackingRules.EnsureNotTrackedAsync call that projects `x.ProductId` from a line
        // collection, and TakesProductIdsFromRequest is a *heuristic*: it matches `x.ProductId)`
        // within 80 characters of `cancellationToken`. The Inventory Adjustment call landed inside
        // that window and the Production Journal call landed just outside it -- so one tripped the
        // guard and the other was saved by character distance. Both are exempt for the same reason,
        // so both are named, because an exemption that depends on formatting is phase 50's
        // right-by-luck problem in a second place.
        ["Inventory/Commands/ApproveInventoryAdjustment/ApproveInventoryAdjustmentCommandHandler.cs"] =
            "Approves an existing adjustment: its lines' product ids were refused-if-parent at Create "
            + "(InventoryValidation.EnsureProductsExistAsync). Takes no product id from its request.",
        ["Manufacturing/Commands/ApproveProductionJournal/ApproveProductionJournalCommandHandler.cs"] =
            "Approves an existing journal: its raw-material, by-product and output ids were "
            + "refused-if-parent at Create (ManufacturingValidation). Takes no product id from its request.",
    };

    /// <summary>The sanctioned ways through the rule. Both funnel into ProductVariantRules.</summary>
    private static readonly string[] SanctionedCalls =
    [
        "EnsureProductsExistAsync",
        "ProductVariantRules.",
    ];

    /// <summary>A handler is in scope when it takes product ids from its own request -- either a
    /// single request.ProductId or a ProductId off its request's line collection.</summary>
    private static readonly Regex TakesProductIdsFromRequest = new(
        @"request\.ProductId|request\.Lines[\s\S]{0,200}?ProductId|x\.ProductId\)[\s\S]{0,80}?cancellationToken",
        RegexOptions.Compiled);

    private static string ApplicationRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ErpApp.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Application");
    }

    private static List<(string Relative, string Source)> CommandHandlers()
    {
        var root = ApplicationRoot();

        return Directory
            .EnumerateFiles(root, "*CommandHandler.cs", SearchOption.AllDirectories)
            .Select(path => (
                Relative: Path.GetRelativePath(root, path).Replace('\\', '/'),
                Source: File.ReadAllText(path)))
            .OrderBy(x => x.Relative, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void The_scan_finds_command_handlers_at_all()
    {
        // Without this, a broken path would make every assertion below pass vacuously -- the classic
        // way a guard test quietly stops guarding anything.
        Assert.True(CommandHandlers().Count > 80);
    }

    [Fact]
    public void Every_handler_taking_product_ids_from_its_request_goes_through_the_variant_rule()
    {
        var offenders = CommandHandlers()
            .Where(x => !Allowed.ContainsKey(x.Relative))
            .Where(x => TakesProductIdsFromRequest.IsMatch(x.Source))
            .Where(x => !SanctionedCalls.Any(call => x.Source.Contains(call, StringComparison.Ordinal)))
            .Select(x => x.Relative)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These handlers accept product ids from their request without refusing a variant parent. " +
            "Call ProductVariantRules (usually via the module's EnsureProductsExistAsync), or add an " +
            "exemption with its reason to ProductVariantSweepGuardTests.Allowed:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_exemption_still_points_at_a_real_file()
    {
        // An allow-list entry for a deleted or renamed file is a silently-widened exemption.
        var root = ApplicationRoot();
        var stale = Allowed.Keys
            .Where(relative => !File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These allow-list entries no longer name a real handler and should be removed:\n  " +
            string.Join("\n  ", stale));
    }

    [Fact]
    public void The_three_shared_validation_helpers_all_route_through_the_rule()
    {
        // The four call sites ProductVariantRules' doc comment claims. Three are the module
        // validation helpers; the fourth (CreateOrUpdateOpeningStockLine) is covered by the
        // per-handler scan above, since it reads its product directly.
        var root = ApplicationRoot();

        foreach (var helper in new[] { "Sales/SalesValidation.cs", "Purchasing/PurchasingValidation.cs", "Inventory/InventoryValidation.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, helper.Replace('/', Path.DirectorySeparatorChar)));

            Assert.True(
                source.Contains("ProductVariantRules.", StringComparison.Ordinal),
                $"{helper} no longer routes its product-existence check through ProductVariantRules.");
        }
    }
}
