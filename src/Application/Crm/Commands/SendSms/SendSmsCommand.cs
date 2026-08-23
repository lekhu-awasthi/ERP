using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.SendSms;

/// <summary>
/// Serves both the live Tigg product's "Bulk SMS" and "Quick SMS" flows (docs/phase-18-status.md
/// decision #6) -- a Quick SMS to one Contact is just AudienceMode=Custom with a single-element
/// ContactIds. AudienceMode implements product-requirements.md FR-4.8's literal three modes
/// (All/ContactGroup/Custom), not Tigg's own Type-checkbox mechanic -- see SmsAudienceMode's own
/// doc comment.
/// </summary>
public sealed record SendSmsCommand(
    Guid OrganizationId,
    SmsAudienceMode AudienceMode,
    Guid? ContactGroupId,
    IReadOnlyList<Guid>? ContactIds,
    Guid? TemplateId,
    string Title,
    string Content)
    : IRequest<SendSmsResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsSend;
}

public sealed record SendSmsResult(Guid BatchId, int RecipientCount, int CreditsUsed, int RemainingBalance);
