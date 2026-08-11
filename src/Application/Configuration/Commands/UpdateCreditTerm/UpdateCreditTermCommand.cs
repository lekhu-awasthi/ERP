using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateCreditTerm;

public sealed record UpdateCreditTermCommand(Guid OrganizationId, Guid Id, string Name, int DueDays, bool IsActive)
    : IRequest<UpdateCreditTermResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CreditTermManage;
}

public sealed record UpdateCreditTermResult(Guid Id, string Name, int DueDays, bool IsActive);
