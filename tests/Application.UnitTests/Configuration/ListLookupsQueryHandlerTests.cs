using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.UnitTests.Configuration;

/// <summary>Exercises the generic list handler through CreditTerm -- the pattern is identical for every lookup type.</summary>
public class ListLookupsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_only_the_requested_organizations_rows_ordered_by_name()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CreditTerms.Add(CreditTerm.Create(organizationId, "Net 60", 60));
        db.CreditTerms.Add(CreditTerm.Create(organizationId, "Net 30", 30));
        db.CreditTerms.Add(CreditTerm.Create(Guid.NewGuid(), "Other Org's Term", 15));
        await db.SaveChangesAsync();

        var handler = new ListLookupsQueryHandler<CreditTerm>(db);

        var result = await handler.Handle(new ListLookupsQuery<CreditTerm>(organizationId), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(["Net 30", "Net 60"], result.Items.Select(x => x.Name));
    }
}
