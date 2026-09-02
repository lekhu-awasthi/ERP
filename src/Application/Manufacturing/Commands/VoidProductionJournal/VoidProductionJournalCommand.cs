using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.VoidProductionJournal;

public sealed record VoidProductionJournalCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidProductionJournalResult>, IRequirePermission, IOrganizationScoped, IRequireFeature,
      ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.ProductionJournalVoid;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];

    public DocumentType LockDateDocumentType => DocumentType.ProductionJournal;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidProductionJournalResult(
    Guid Id, string Code, ProductionJournalStatus Status, DateTimeOffset? VoidedAt);
