using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration;
using ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.CreateReportingTagOption;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;

namespace ErpApp.Application.UnitTests.Configuration;

/// <summary>
/// Phase 44 -- the Reporting Tag filter's semantics, pinned on <b>both</b> axes.
///
/// <para><b>Measured live on Moonbeam, 2026-09-15</b>, on Inventory Position's filter drawer, which
/// is the first tenant read in this project with more than one tag category carrying tagged data
/// (it has six). The four runs, and what each one rules out:</para>
///
/// <list type="bullet">
/// <item>BUSINESS{BUSINESS} -> 3 products.</item>
/// <item>BUSINESS{BUSINESS, asdasd} -> 4 products, exactly the union. Adding an option inside a
/// category <i>grew</i> the set, which AND-within cannot do.</item>
/// <item>BUSINESS{BUSINESS} + SERVICE{sdfsdfds} -> <b>2</b> products, a strict subset of the first
/// run. Adding a second category <i>shrank</i> the set, which OR-across cannot do.</item>
/// <item>BUSINESS{both} + SERVICE{sdfsdfds} -> 3.</item>
/// </list>
///
/// <para>So: <b>OR within a category, AND across categories</b> -- and phases 19 and 36's inherited
/// "any of everything selected" was wrong. This fixture reproduces that exact shape with two
/// categories and three documents, so the numbers move the same way the live ones did.</para>
/// </summary>
public class ReportingTagFilterTests
{
    /// <summary>The OR half: a second option in the same category widens the match.</summary>
    [Fact]
    public async Task Two_options_in_one_category_match_a_document_carrying_either()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var one = await ResolveAsync(db, seed, [seed.BusinessOneId]);
        var both = await ResolveAsync(db, seed, [seed.BusinessOneId, seed.BusinessTwoId]);

        Assert.Equal([seed.BothTaggedId, seed.BusinessOnlyId], Ordered(seed, one));
        Assert.Equal([seed.BothTaggedId, seed.BusinessOnlyId, seed.OtherOptionId], Ordered(seed, both));

        // Growing, not shrinking -- the property AND-within could not produce.
        Assert.True(one.IsSubsetOf(both));
        Assert.True(both.Count > one.Count);
    }

    /// <summary>The AND half: selecting in a second category narrows the match to documents tagged
    /// in both, which is the run that falsified the inherited rule.</summary>
    [Fact]
    public async Task A_selection_in_a_second_category_narrows_rather_than_widens()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var businessOnly = await ResolveAsync(db, seed, [seed.BusinessOneId]);
        var bothCategories = await ResolveAsync(db, seed, [seed.BusinessOneId, seed.ServiceOneId]);

        Assert.Equal([seed.BothTaggedId, seed.BusinessOnlyId], Ordered(seed, businessOnly));

        // The document tagged in BUSINESS but not in SERVICE drops out.
        Assert.Equal([seed.BothTaggedId], Ordered(seed, bothCategories));
        Assert.True(bothCategories.IsProperSubsetOf(businessOnly));
    }

    /// <summary>Both axes at once -- the live run's fourth row.</summary>
    [Fact]
    public async Task Or_within_and_and_across_compose()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await ResolveAsync(db, seed, [seed.BusinessOneId, seed.BusinessTwoId, seed.ServiceOneId]);

        // Both documents that carry a SERVICE tag AND one of the two BUSINESS tags.
        Assert.Equal([seed.BothTaggedId, seed.OtherOptionId], Ordered(seed, result));
    }

    /// <summary>
    /// An option id this tenant does not own matches nothing, and -- this is the part worth a test --
    /// it returns an <b>empty</b> set rather than null. Null means "no filter requested", so
    /// returning it here would silently widen the report to every row (phase-42's "a count of zero is
    /// a complete answer", in its filtering form).
    /// </summary>
    [Fact]
    public async Task An_unknown_option_matches_nothing_and_does_not_widen_the_report()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await ResolveAsync(db, seed, [Guid.NewGuid()]);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>No selection at all is null -- "this report is not tag-filtered".</summary>
    [Fact]
    public async Task No_selection_is_null_not_an_empty_set()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        Assert.Null(await ReportingTagFilter.ResolveMatchingDocumentIdsAsync(
            db, DocumentType.Quotation, null, CancellationToken.None, seed.OrganizationId));
        Assert.Null(await ReportingTagFilter.ResolveMatchingDocumentIdsAsync(
            db, DocumentType.Quotation, [], CancellationToken.None, seed.OrganizationId));
    }

    /// <summary>
    /// The multi-type overload the Journal report uses answers identically for the same data, keyed
    /// by (type, id) instead of by id. The two must not drift -- they share one rule.
    /// </summary>
    [Fact]
    public async Task The_multi_type_overload_applies_the_same_rule()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await ReportingTagFilter.ResolveMatchingDocumentsAsync(
            db, seed.OrganizationId, [seed.BusinessOneId, seed.ServiceOneId], CancellationToken.None);

        Assert.NotNull(result);
        var match = Assert.Single(result);
        Assert.Equal((DocumentType.Quotation, seed.BothTaggedId), match);
    }

    private static async Task<HashSet<Guid>> ResolveAsync(IAppDbContext db, Seed seed, IReadOnlyList<Guid> optionIds)
    {
        var result = await ReportingTagFilter.ResolveMatchingDocumentIdsAsync(
            db, DocumentType.Quotation, optionIds, CancellationToken.None, seed.OrganizationId);
        Assert.NotNull(result);
        return result;
    }

    /// <summary>A stable order for comparison -- the filter returns a set, which has none.</summary>
    private static List<Guid> Ordered(Seed seed, HashSet<Guid> ids) =>
        [.. new[] { seed.BothTaggedId, seed.BusinessOnlyId, seed.OtherOptionId }.Where(ids.Contains)];

    private sealed record Seed(
        Guid OrganizationId,
        Guid BusinessOneId, Guid BusinessTwoId, Guid ServiceOneId,
        Guid BothTaggedId, Guid BusinessOnlyId, Guid OtherOptionId);

    /// <summary>
    /// Two categories, three options and three quotations, arranged so that every one of the live
    /// runs has a counterpart here:
    /// <list type="bullet">
    /// <item><c>BothTagged</c> carries BusinessOne + ServiceOne.</item>
    /// <item><c>BusinessOnly</c> carries BusinessOne and nothing in the Service category.</item>
    /// <item><c>OtherOption</c> carries BusinessTwo + ServiceOne.</item>
    /// </list>
    /// </summary>
    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var business = await new CreateReportingTagCategoryCommandHandler(db).Handle(
            new CreateReportingTagCategoryCommand(organizationId, "Business"), CancellationToken.None);
        var service = await new CreateReportingTagCategoryCommandHandler(db).Handle(
            new CreateReportingTagCategoryCommand(organizationId, "Service"), CancellationToken.None);

        var businessOne = await new CreateReportingTagOptionCommandHandler(db).Handle(
            new CreateReportingTagOptionCommand(organizationId, "Business One", business.Id), CancellationToken.None);
        var businessTwo = await new CreateReportingTagOptionCommandHandler(db).Handle(
            new CreateReportingTagOptionCommand(organizationId, "Business Two", business.Id), CancellationToken.None);
        var serviceOne = await new CreateReportingTagOptionCommandHandler(db).Handle(
            new CreateReportingTagOptionCommand(organizationId, "Service One", service.Id), CancellationToken.None);

        var bothTagged = await CreateQuotationAsync(db, organizationId, customer.Id, new DateOnly(2026, 1, 1));
        var businessOnly = await CreateQuotationAsync(db, organizationId, customer.Id, new DateOnly(2026, 1, 2));
        var otherOption = await CreateQuotationAsync(db, organizationId, customer.Id, new DateOnly(2026, 1, 3));

        var tagHandler = new SetTransactionReportingTagsCommandHandler(db);
        await tagHandler.Handle(
            new SetTransactionReportingTagsCommand(
                organizationId, DocumentType.Quotation, bothTagged, [businessOne.Id, serviceOne.Id]),
            CancellationToken.None);
        await tagHandler.Handle(
            new SetTransactionReportingTagsCommand(
                organizationId, DocumentType.Quotation, businessOnly, [businessOne.Id]),
            CancellationToken.None);
        await tagHandler.Handle(
            new SetTransactionReportingTagsCommand(
                organizationId, DocumentType.Quotation, otherOption, [businessTwo.Id, serviceOne.Id]),
            CancellationToken.None);

        return new Seed(
            organizationId, businessOne.Id, businessTwo.Id, serviceOne.Id,
            bothTagged, businessOnly, otherOption);
    }

    private static async Task<Guid> CreateQuotationAsync(
        IAppDbContext db, Guid organizationId, Guid customerId, DateOnly date)
    {
        var quotation = await new CreateQuotationCommandHandler(db).Handle(
            new CreateQuotationCommand(organizationId, customerId, date, null, null, []),
            CancellationToken.None);
        return quotation.Id;
    }
}
