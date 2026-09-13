using ErpApp.Application.Common.Validation;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 39. Covered directly rather than only through the ten validators that call it, for the
/// reason phase 25 records: a FluentValidation rule that cannot resolve its property name throws at
/// <i>rule-construction</i> time inside the API pipeline, which means it 500s every endpoint it
/// guards while every handler test stays green. The only test that can see that is one that builds
/// the rule and validates through it.
/// </summary>
public class RichTextRulesTests
{
    private sealed class Subject
    {
        public string? Terms { get; init; }
    }

    private sealed class SubjectValidator : AbstractValidator<Subject>
    {
        public SubjectValidator() => RuleFor(x => x.Terms).RichText("Terms and conditions");
    }

    [Fact]
    public void The_rule_resolves_its_property_name_rather_than_throwing()
    {
        var result = new SubjectValidator().Validate(
            new Subject { Terms = new string('x', RichText.MaxLength + 1) });

        Assert.False(result.IsValid);
        Assert.Equal(nameof(Subject.Terms), Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void The_message_names_the_field_a_user_would_recognise()
    {
        var result = new SubjectValidator().Validate(
            new Subject { Terms = new string('x', RichText.MaxLength + 1) });

        Assert.Contains("Terms and conditions", Assert.Single(result.Errors).ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_exactly_at_the_limit_is_accepted()
    {
        var result = new SubjectValidator().Validate(
            new Subject { Terms = new string('x', RichText.MaxLength) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Null_terms_are_fine_because_the_field_is_optional()
    {
        Assert.True(new SubjectValidator().Validate(new Subject { Terms = null }).IsValid);
    }

    /// <summary>
    /// And the rule is really reached from a real command, which is the half a synthetic subject
    /// cannot prove. Phase 27b's lesson in miniature: a field wired everywhere except the one place
    /// that matters looks identical from the inside.
    /// </summary>
    [Fact]
    public void A_real_command_rejects_oversized_terms()
    {
        var command = new CreateInvoiceCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow),
            Reference: null, Lines: [], Terms: new string('x', RichText.MaxLength + 1));

        var result = new CreateInvoiceCommandValidator().Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CreateInvoiceCommand.Terms));
    }
}
