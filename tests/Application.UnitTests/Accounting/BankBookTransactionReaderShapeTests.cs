using System.Reflection;
using ErpApp.Application.Accounting.Reports;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 56 — a guard on the <b>shape</b> of <see cref="BankBookTransactionReader"/>, because the
/// bug it prevents is one no handler test can see.
///
/// <para><b>What happened.</b> The reader first exposed its joined query already projected, as
/// <c>IQueryable&lt;BookMovement&gt;</c>, and each handler then ordered, counted and summed over
/// that. Every handler test passed. Against SQL Server the list endpoint returned <b>500</b>: EF
/// cannot translate <c>OrderBy(x =&gt; new BookMovement(…).PostedAt)</c>, because the ordering key
/// is a member of a record the client would have to construct. InMemory evaluates the whole
/// expression in C# and never notices. Only the manual E2E saw it.</para>
///
/// <para>That is the same door phase 25 found with a captured <c>Func</c> in a validator, phase 34b
/// with a static call in a predicate, and phase 42 with a <c>Sum</c> over an already-projected
/// record — the fourth time. The fix was structural rather than a careful rewrite of three call
/// sites: the reader hands back <i>answers</i>, projects only after <c>ToListAsync</c>, and exposes
/// no query for a caller to compose over.</para>
///
/// <para><b>This test pins the structure, not the symptom</b> (phase 50's rule: a guard asserting a
/// rule's concrete consequences is not a guard on the rule). A future change that exposes an
/// <c>IQueryable</c> again fails here, in a suite that runs without Docker, rather than at the next
/// E2E — or, worse, in production.</para>
/// </summary>
public class BankBookTransactionReaderShapeTests
{
    [Fact]
    public void The_reader_hands_back_answers_and_never_a_composable_query()
    {
        var leaky = typeof(BankBookTransactionReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => Mentions(m.ReturnType, typeof(IQueryable<>)))
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            leaky.Count == 0,
            "These members hand a caller a query to compose over, which is how phase 56 shipped an "
            + "ORDER BY that SQL Server could not translate while every InMemory handler test "
            + "passed. Return the answer instead, and project after ToListAsync:\n  "
            + string.Join("\n  ", leaky));
    }

    /// <summary>
    /// The premise half, so the assertion above cannot become vacuously true by the reader losing
    /// the methods it is supposed to have (phase 54's <c>UnitlessOutputHeaders</c> lesson).
    /// </summary>
    [Fact]
    public void The_reader_still_answers_the_four_questions_the_module_asks_of_it()
    {
        var names = typeof(BankBookTransactionReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CountAsync", names);
        Assert.Contains("SumAsync", names);
        Assert.Contains("PageAsync", names);
        Assert.Contains("DescribeAsync", names);
        Assert.Contains("ForReconciliationAsync", names);
    }

    private static bool Mentions(Type type, Type openGeneric) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric)
        || type.GetGenericArguments().Any(arg => Mentions(arg, openGeneric));
}
