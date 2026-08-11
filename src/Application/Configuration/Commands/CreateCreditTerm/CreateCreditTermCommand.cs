using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateCreditTerm;

public sealed record CreateCreditTermCommand(Guid OrganizationId, string Name, int DueDays)
    : IRequest<CreateCreditTermResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CreditTermManage;
}

public sealed record CreateCreditTermResult(Guid Id, string Name, int DueDays);
