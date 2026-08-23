using ErpApp.Domain.Crm;
using FluentValidation;

namespace ErpApp.Application.Crm.Commands.SendSms;

public sealed class SendSmsCommandValidator : AbstractValidator<SendSmsCommand>
{
    public SendSmsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AudienceMode).IsInEnum();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(500);

        RuleFor(x => x.ContactGroupId).NotEmpty().When(x => x.AudienceMode == SmsAudienceMode.ContactGroup)
            .WithMessage("Contact group is required for ContactGroup audience mode.");

        RuleFor(x => x.ContactIds).Must(x => x is { Count: > 0 }).When(x => x.AudienceMode == SmsAudienceMode.Custom)
            .WithMessage("At least one contact is required for Custom audience mode.");
    }
}
