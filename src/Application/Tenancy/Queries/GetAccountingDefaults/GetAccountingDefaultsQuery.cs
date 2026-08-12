using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetAccountingDefaults;

public sealed record GetAccountingDefaultsQuery(Guid OrganizationId) : IRequest<GetAccountingDefaultsDto>;

public sealed record GetAccountingDefaultsDto(
    Guid? DefaultSalesAccountId, Guid? DefaultAccountsReceivableId, Guid? DefaultVatPayableAccountId);
