using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.PreviewGlPosting;

/// <summary>
/// Takes the same Lines[] shape as Create/UpdateJournalVoucher (no persisted voucher needed) and
/// runs it through the exact same JournalVoucherPostingRule the real Approve command handler
/// uses (architecture-spec.md §3.4's "no duplicated debit/credit math" rule) -- proves the
/// shared-rule contract and is the real preview path once a later document type's posting rule
/// stops being a trivial identity mapping.
/// </summary>
public sealed record PreviewGlPostingQuery(Guid OrganizationId, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<IReadOnlyList<GlLinePreviewDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherView;
}

public sealed record GlLinePreviewDto(Guid AccountId, decimal Debit, decimal Credit);
