using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.SystemAuditReport;

/// <summary>
/// Filterable read side of the append-only Audit trail (roadmap Phase 16d, architecture-spec.md
/// §3.9) -- Admin-only (PermissionKeys.SystemAuditView), a flat per-user activity register naming
/// every Create/Update/Approve/Void action any org member took, the same PAN-exposure-adjacent
/// discriminator that made TdsReportView Admin-only (phase-8b-status.md).
///
/// DocumentType's filter dropdown only needs to offer the 13 ApprovableTransaction types this
/// phase actually audits (see AuditBehavior's scope decision, phase-16d-status.md) -- the other 5
/// DocumentType enum values (Account/Contact/Product numbering-pool-only entries,
/// ProductionOrder/ProductionJournal) can never appear in an Audit row, so the Angular page's
/// dropdown is scoped to 13, not all 18.
/// </summary>
public sealed record SystemAuditReportQuery(
    Guid OrganizationId,
    Guid? UserId,
    string? Action,
    DocumentType? DocumentType,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PagedResult<AuditRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SystemAuditView;
}

/// <summary>Direction is populated only for DocumentType.Payment rows -- Customer Payment and
/// Supplier Payment share one aggregate but two separate Angular detail pages (same split
/// TransactionApprovalRowDto's own Direction field exists for), so the report's row-linking needs
/// it to pick the right route. Null for every other document type.</summary>
public sealed record AuditRowDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid UserId,
    string UserName,
    string Action,
    DocumentType DocumentType,
    Guid DocumentId,
    PaymentDirection? Direction);
