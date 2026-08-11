using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.GetCashTransfer;

public sealed record GetCashTransferQuery(Guid OrganizationId, Guid Id)
    : IRequest<CashTransferDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CashTransferView;
}

public sealed record CashTransferLineDto(Guid Id, Guid ToAccountId, decimal Amount);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record CashTransferDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid FromAccountId,
    CashTransferStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CashTransferLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines);
