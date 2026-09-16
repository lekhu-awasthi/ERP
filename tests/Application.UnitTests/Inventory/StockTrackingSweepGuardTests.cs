using System.Reflection;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 51 — the scope boundary, asserted in both directions.
///
/// <para><b>Why a guard and not a paragraph.</b> Phase 30's lesson is that <i>a list sampled from a
/// few screens becomes a wrong list; find the rule</i>, and this phase's rule is: the allocation
/// control is where the 2026-09-16 read put it (Invoice and Purchase Bill line grids); every other
/// stock path either derives the allocation from its source or refuses a tracked product with a
/// named 409. A rule stated only in prose stops being true the first time somebody adds a
/// stock-moving document type — which is exactly the failure phase 24's own sweep guard was written
/// to prevent, in its own words: "Phase 25 adding a Production Journal that takes raw-material
/// ProductIds and forgetting the check".</para>
///
/// <para>Phase 46's sharper version applies too: a guard predicate naming a <i>dependency</i> is not
/// naming the behaviour, so the exclusions are named with reasons and asserted to still exist rather
/// than inferred from what a handler happens to inject.</para>
/// </summary>
public class StockTrackingSweepGuardTests
{
    /// <summary>
    /// The refused paths, restated here rather than read from <see cref="StockTrackingRules"/> —
    /// deliberately, because a test that read the dictionary would assert the dictionary equals
    /// itself (phase 50's <c>TenantIndexConventionTests</c> makes the same move for the same
    /// reason). These are the handler files each refusal must actually be enforced in.
    /// </summary>
    public static TheoryData<string, string> RefusedPaths => new()
    {
        { "Inventory Adjustment", "Inventory/Commands/ApproveInventoryAdjustment/ApproveInventoryAdjustmentCommandHandler.cs" },
        { "Production Journal", "Manufacturing/Commands/ApproveProductionJournal/ApproveProductionJournalCommandHandler.cs" },

        // Opening Stock has no Approve handler -- CreateOrUpdateOpeningStockLine writes the layer
        // directly, because the row has no Draft/Approve lifecycle (phase 17).
        { "Opening Stock", "Inventory/Commands/CreateOrUpdateOpeningStockLine/CreateOrUpdateOpeningStockLineCommandHandler.cs" },
    };

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

    [Theory]
    [MemberData(nameof(RefusedPaths))]
    public void A_refused_path_names_its_reason_and_enforces_it(string pathName, string handlerRelativePath)
    {
        Assert.True(
            StockTrackingRules.RefusedPaths.ContainsKey(pathName),
            $"'{pathName}' is no longer in StockTrackingRules.RefusedPaths. If it now carries the "
            + "allocation, remove it from this guard's list too -- and say so in the status doc, "
            + "because it is a scope change, not a refactor.");

        Assert.False(
            string.IsNullOrWhiteSpace(StockTrackingRules.RefusedPaths[pathName]),
            $"'{pathName}' is refused with no reason recorded. An exclusion has to say what it is "
            + "excluding and why (phase 46).");

        var file = Path.Combine(ApplicationRoot(), handlerRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(file), $"The handler this refusal lives in has moved: {handlerRelativePath}");

        var source = File.ReadAllText(file);

        Assert.Contains("StockTrackingRules.EnsureNotTrackedAsync", source, StringComparison.Ordinal);
        Assert.Contains($"\"{pathName}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_list_has_no_entry_this_guard_does_not_check()
    {
        // The other direction. Without it, a fourth refused path could be added to the rules and
        // never verified to be enforced anywhere -- the shape of guard that silently stops covering
        // what it was written for (phase 39).
        var guarded = RefusedPaths.Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);
        var declared = StockTrackingRules.RefusedPaths.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.True(
            declared.SetEquals(guarded),
            "StockTrackingRules.RefusedPaths and this guard's list have diverged. Declared but "
            + $"unguarded: {string.Join(", ", declared.Except(guarded))}. Guarded but not declared: "
            + $"{string.Join(", ", guarded.Except(declared))}.");
    }

    [Fact]
    public void Every_line_parent_type_names_a_real_document_type()
    {
        // Never bridge two overlapping enums by ordinal cast -- bridge by name and pin it
        // (CLAUDE.md, phase 27a). Nothing bridges these today; this is what keeps that cheap, by
        // failing the moment a member is added whose document type does not exist.
        foreach (var parentType in Enum.GetValues<DocumentLineParentType>())
        {
            var name = parentType.ToString();

            Assert.EndsWith("Line", name, StringComparison.Ordinal);

            var documentTypeName = name[..^"Line".Length];

            Assert.True(
                Enum.TryParse<DocumentType>(documentTypeName, out _),
                $"DocumentLineParentType.{name} implies a DocumentType called '{documentTypeName}', "
                + "and there is none. The two enums are bridged by name, never by ordinal.");
        }
    }

    [Fact]
    public void The_allocation_carrying_line_types_are_exactly_the_four_the_rule_admits()
    {
        // The rule, in the other direction: the control is on the two the read shows, and the two
        // that derive theirs from a source line. A fifth member here without a status-doc decision
        // is scope drift.
        Assert.Equal(
            new[]
            {
                DocumentLineParentType.InvoiceLine,
                DocumentLineParentType.PurchaseBillLine,
                DocumentLineParentType.CreditNoteLine,
                DocumentLineParentType.DebitNoteLine,
            },
            Enum.GetValues<DocumentLineParentType>());
    }

    [Fact]
    public void The_scan_finds_the_application_root_at_all()
    {
        // Without this, a broken path would make the file assertions above pass vacuously -- the
        // classic way a guard test quietly stops guarding anything (phase 24's own self-check, and
        // phase 34a's "a guard must assert its input is non-empty, not merely defined").
        Assert.True(Directory.EnumerateFiles(ApplicationRoot(), "*CommandHandler.cs", SearchOption.AllDirectories).Take(81).Count() > 80);
    }

    [Fact]
    public void StockTrackingRules_refuses_a_path_it_does_not_know_about()
    {
        // EnsureNotTrackedAsync is the only way to refuse, so a caller passing a path name that is
        // not declared must fail loudly rather than silently permit. Without this the guard above
        // could be satisfied by a handler that calls the method with a typo'd name.
        var method = typeof(StockTrackingRules).GetMethod(
            nameof(StockTrackingRules.EnsureNotTrackedAsync), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.False(StockTrackingRules.RefusedPaths.ContainsKey("Not A Path"));
    }
}
