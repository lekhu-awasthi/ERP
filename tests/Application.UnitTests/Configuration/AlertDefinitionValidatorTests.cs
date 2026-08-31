using ErpApp.Application.Configuration.Commands.CreateAlertDefinition;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.UnitTests.Configuration;

/// <summary>
/// Recipient-format validation lives at definition time on purpose: the dispatcher runs unattended,
/// so a malformed address discovered there can only ever become a Failed row nobody is watching.
/// See AlertDefinitionValidation.
/// </summary>
public class AlertDefinitionValidatorTests
{
    [Theory]
    [InlineData("ops@example.test")]
    [InlineData("a@example.test, b@example.test")]
    [InlineData(" a@example.test ,b@example.test ")]
    [InlineData("a@example.test;b@example.test")]
    // A trailing separator is not an error -- ParseRecipients strips the empty entry, so this is a
    // valid single-recipient list, and rejecting it would fail an admin for a stray keystroke.
    [InlineData("a@example.test, ")]
    public void Accepts_valid_recipient_lists(string recipients)
    {
        Assert.True(Validate(recipients).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@example.test, not-an-email")]
    // A display-name form would be accepted by MailAddress but is not a bare address; the grid
    // column and the send log both store one address per row.
    [InlineData("Ops Team <ops@example.test>")]
    public void Rejects_invalid_recipient_lists(string recipients)
    {
        Assert.False(Validate(recipients).IsValid);
    }

    [Fact]
    public void Rejects_a_recipient_list_longer_than_the_column()
    {
        var tooLong = string.Join(", ", Enumerable.Range(0, 200).Select(i => $"user{i}@example.test"));

        Assert.False(Validate(tooLong).IsValid);
    }

    private static FluentValidation.Results.ValidationResult Validate(string recipients) =>
        new CreateAlertDefinitionCommandValidator().Validate(
            new CreateAlertDefinitionCommand(
                Guid.NewGuid(), "Daily summary", AlertMedium.Email, AlertType.DailyTransactionSummary,
                recipients, AlertScheduleFrequency.Daily, new TimeOnly(19, 57)));
}
