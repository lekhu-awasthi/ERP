using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.ApproveProductionJournal;

/// <summary><see cref="OverrideWarning"/> mirrors ApproveInvoiceCommand's own flag: when the
/// tenant's NegativeStockBalanceAction is Warn and raw stock is short, the first attempt returns a
/// confirmable warning and a second attempt with this set proceeds. A Reject tenant is never
/// overridable.</summary>
public sealed record ApproveProductionJournalCommand(Guid OrganizationId, Guid Id, bool OverrideWarning = false)
    : IRequest<ApproveProductionJournalResult>, IRequirePermission, IOrganizationScoped, IRequireFeature,
      ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.ProductionJournalApprove;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];

    public DocumentType LockDateDocumentType => DocumentType.ProductionJournal;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveProductionJournalResult(
    Guid Id,
    string Code,
    ProductionJournalStatus Status,
    DateTimeOffset? ApprovedAt,
    decimal RawMaterialCost,
    decimal ProductionExpenseCost,
    decimal TotalCostOfProduction,
    decimal CostAllocatedToByProduct,
    decimal FinishedGoodsCost,
    decimal FinishedGoodsUnitCost,
    decimal CostRoundingAdjustment);
