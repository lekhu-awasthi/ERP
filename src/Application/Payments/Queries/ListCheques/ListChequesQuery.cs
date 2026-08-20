using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListCheques;

/// <summary>Backs the Cheque Register's Cheque Received/Cheque Issued tabs (Direction filter) and
/// its Dashboard tab's combined Cheque Lists table (Direction = null shows both).</summary>
public sealed record ListChequesQuery(
    Guid OrganizationId,
    PaymentDirection? Direction,
    ChequeStatus? Status = null,
    Guid? ContactId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ChequeDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ChequeView;
}

public sealed record ChequeDto(
    Guid Id, Guid LinkedPaymentId, PaymentDirection Direction, Guid ContactId, string ContactName,
    Guid AccountId, string AccountName, string ChequeNo, DateOnly ChequeDate, DateOnly? ReceivedDate,
    decimal Amount, ChequeStatus Status);
