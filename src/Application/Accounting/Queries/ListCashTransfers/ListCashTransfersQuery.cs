using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListCashTransfers;

public sealed record ListCashTransfersQuery(Guid OrganizationId, CashTransferStatus? Status)
    : IRequest<IReadOnlyList<CashTransfer>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CashTransferView;
}
