using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class DocumentNumberingRuleTests
{
    [Fact]
    public void CreateDefault_starts_at_number_one_in_auto_mode_with_no_prefix()
    {
        var rule = DocumentNumberingRule.CreateDefault(Guid.NewGuid(), DocumentType.Invoice);

        Assert.Equal(DocumentType.Invoice, rule.DocumentType);
        Assert.Equal(string.Empty, rule.Prefix);
        Assert.Equal(1, rule.NextNumber);
        Assert.Equal(NumberingMode.Auto, rule.Mode);
        Assert.False(rule.ResetEveryFiscalYear);
        Assert.False(rule.IncludeFiscalYearInCode);
        Assert.False(rule.LocationWiseNumbering);
    }

    [Fact]
    public void UpdateSettings_replaces_settings_fields_without_touching_next_number()
    {
        var rule = DocumentNumberingRule.CreateDefault(Guid.NewGuid(), DocumentType.Invoice);

        rule.UpdateSettings("INV-", NumberingMode.Manual, true, true, true);

        Assert.Equal("INV-", rule.Prefix);
        Assert.Equal(NumberingMode.Manual, rule.Mode);
        Assert.True(rule.ResetEveryFiscalYear);
        Assert.True(rule.IncludeFiscalYearInCode);
        Assert.True(rule.LocationWiseNumbering);
        Assert.Equal(1, rule.NextNumber);
    }
}
