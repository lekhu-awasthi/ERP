using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.UpdateOrganization;

/// <summary>
/// The same rules <c>CreateOrganizationCommandValidator</c> applies to the same nine fields, minus
/// the two the wizard owns and this command does not touch (WorkspaceName, TurnstileToken).
///
/// <para>Stated as its own class rather than shared with Create's: phase-25's lesson is about a
/// shared FluentValidation <i>helper</i> built from a captured selector, and this is the opposite
/// problem -- two validators over two different command types whose field lists happen to overlap.
/// <c>OrganizationDetailValidationTests</c> asserts they agree on every shared field, which is the
/// part worth pinning.</para>
/// </summary>
public sealed class UpdateOrganizationCommandValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Industry).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.AccountingStartDate).NotEqual(default(DateOnly));

        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.PanNumber).MaximumLength(50);
        RuleFor(x => x.Website).MaximumLength(256);
    }
}
