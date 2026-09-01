using ErpApp.Application.Imports;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// The cell-coercion rules, which are where a bulk importer silently goes wrong. Every case here is
/// one that would otherwise import <i>something</i> -- a zero, a false, a truncated number -- rather
/// than telling the user their spreadsheet is wrong.
/// </summary>
public class ImportRowReaderTests
{
    [Fact]
    public void Maps_by_header_name_not_by_position()
    {
        // Columns in a different order than the template's, with an extra column of the user's own.
        var reader = Read(
            ["Notes", "Product Name", "Selling Price"],
            ["ignore me", "Salted Cashew", "150"]);

        Assert.Equal("Salted Cashew", reader.GetRequiredString("Product Name"));
        Assert.Equal(150m, reader.GetOptionalDecimal("Selling Price"));
    }

    /// <summary>The template writes required headers with the reference product's "**" marker; the
    /// marker is presentation, so a user who deletes it still gets a working file.</summary>
    [Theory]
    [InlineData("Product Name**")]
    [InlineData("Product Name")]
    [InlineData("  product name  ")]
    public void Header_matching_ignores_the_required_marker_case_and_surrounding_space(string header)
    {
        var reader = Read([header], ["Biscuit"]);

        Assert.Equal("Biscuit", reader.GetRequiredString("Product Name"));
    }

    [Fact]
    public void A_missing_required_value_names_its_own_column()
    {
        var reader = Read(["Product Name"], [" "]);

        var ex = Assert.Throws<ImportRowException>(() => reader.GetRequiredString("Product Name"));
        Assert.Equal("Product Name", ex.ColumnName);
    }

    /// <summary>An exported spreadsheet really does contain "1,500"; rejecting it as non-numeric
    /// would be technically defensible and practically useless.</summary>
    [Fact]
    public void Reads_a_number_that_carries_thousands_separators()
    {
        var reader = Read(["Selling Price"], ["1,500.50"]);

        Assert.Equal(1500.50m, reader.GetOptionalDecimal("Selling Price"));
    }

    [Fact]
    public void A_non_numeric_price_is_a_row_error_not_a_zero()
    {
        var reader = Read(["Selling Price"], ["about a hundred"]);

        var ex = Assert.Throws<ImportRowException>(() => reader.GetOptionalDecimal("Selling Price"));
        Assert.Equal("Selling Price", ex.ColumnName);
        Assert.Contains("not a valid number", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fractional_whole_number_column_is_a_row_error()
    {
        var reader = Read(["Reorder Level"], ["3.5"]);

        Assert.Throws<ImportRowException>(() => reader.GetOptionalInt("Reorder Level"));
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("y", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("No", false)]
    [InlineData("n", false)]
    [InlineData("0", false)]
    public void Reads_the_yes_and_no_spellings_a_human_actually_types(string cell, bool expected)
    {
        var reader = Read(["Track Inventory"], [cell]);

        Assert.Equal(expected, reader.GetOptionalBoolean("Track Inventory", fallback: !expected));
    }

    /// <summary>The failure this protects against: importing every product as "not tracked" because
    /// someone wrote something the parser did not recognise.</summary>
    [Fact]
    public void An_unrecognised_yes_no_value_is_a_row_error_not_a_silent_false()
    {
        var reader = Read(["Track Inventory"], ["maybe"]);

        var ex = Assert.Throws<ImportRowException>(() => reader.GetOptionalBoolean("Track Inventory", fallback: true));
        Assert.Contains("use Yes or No", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_optional_column_falls_back_rather_than_throwing()
    {
        var reader = Read(["Product Name"], ["Biscuit"]);

        Assert.Null(reader.GetOptionalString("HS Code"));
        Assert.Equal(0m, reader.GetOptionalDecimal("Selling Price"));
        Assert.True(reader.GetOptionalBoolean("Track Inventory", fallback: true));
    }

    [Fact]
    public void An_invalid_choice_lists_every_accepted_word()
    {
        var reader = Read(["Product Type"], ["Widget"]);
        var allowed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Goods"] = 1, ["Service"] = 2 };

        var ex = Assert.Throws<ImportRowException>(
            () => reader.GetChoice("Product Type", allowed, required: true, fallback: 0));

        Assert.Contains("Goods", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Service", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Phase 21c added date reading, and this is the assertion that matters: the ambiguous
    /// dd/MM-vs-MM/dd case resolves <b>day-first</b>. 07/08/2024 is a real date under both readings,
    /// so a wrong guess imports the wrong month silently, in statutory data, with nothing to catch
    /// it -- see GetOptionalDate's own comment for why day-first is the right default here.
    /// </summary>
    [Theory]
    [InlineData("2024-07-30", 2024, 7, 30)]
    [InlineData("2024/07/30", 2024, 7, 30)]
    [InlineData("30-07-2024", 2024, 7, 30)]
    [InlineData("30/07/2024", 2024, 7, 30)]
    [InlineData("7/8/2024", 2024, 8, 7)]
    [InlineData("30-Jul-2024", 2024, 7, 30)]
    [InlineData("2024-07-30 00:00:00", 2024, 7, 30)]
    public void A_date_column_accepts_the_formats_a_real_spreadsheet_produces(
        string cell, int year, int month, int day)
    {
        var reader = Read(["Date"], [cell]);

        Assert.Equal(new DateOnly(year, month, day), reader.GetRequiredDate("Date"));
    }

    [Fact]
    public void An_unparseable_date_names_the_column_and_the_expected_format()
    {
        var reader = Read(["Date"], ["last Tuesday"]);

        var ex = Assert.Throws<ImportRowException>(() => reader.GetRequiredDate("Date"));

        Assert.Equal("Date", ex.ColumnName);
        Assert.Contains("yyyy-MM-dd", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_date_is_null_when_optional_and_a_row_error_when_required()
    {
        var reader = Read(["Date", "Export Declaration Date"], ["2024-07-30", null]);

        Assert.Null(reader.GetOptionalDate("Export Declaration Date"));
        Assert.Equal("Export Declaration Date", 
            Assert.Throws<ImportRowException>(() => reader.GetRequiredDate("Export Declaration Date")).ColumnName);
    }

    private static ImportRowReader Read(string[] headers, string?[] cells) =>
        new(
            ImportRowReader.BuildColumnIndexes([.. headers.Select(ImportRowReader.Normalize)]),
            new ImportSheetRow(2, cells));
}
