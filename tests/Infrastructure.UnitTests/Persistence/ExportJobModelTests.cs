using ErpApp.Domain.Exports;
using ErpApp.Infrastructure.UnitTests.TestSupport;

namespace ErpApp.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Phase 80 -- an export that asks for every category stores every category's name, comma-joined, in a column whose
/// width is Infrastructure's while the names are the Domain's. Phase 80 took the list from eight to twelve (155
/// characters against 200). A name that pushed it over would fail only on SQL Server, as a truncation error on the
/// job's insert, with every InMemory test green.
/// </summary>
[Collection(nameof(ModelCollection))]
public class ExportJobModelTests(ModelFixture fixture)
{
    [Fact]
    public void Every_category_selected_at_once_fits_the_stored_column()
    {
        var column = fixture.Entity(nameof(ExportJob)).FindProperty(nameof(ExportJob.Categories))!;
        var everything = string.Join(",", Enum.GetNames<ExportCategory>());

        Assert.Equal(12, Enum.GetNames<ExportCategory>().Length);
        Assert.True(
            everything.Length <= column.GetMaxLength(),
            $"Every category joined is {everything.Length} characters; ExportJob.Categories holds {column.GetMaxLength()}.");
    }
}
