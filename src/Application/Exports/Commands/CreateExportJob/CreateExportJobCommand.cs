using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Commands.CreateExportJob;

/// <summary>
/// Enqueues a full-tenant data export (FR-2.8). Returns immediately with a Queued job -- NFR-4.3's
/// "shall run asynchronously and not block the initiating user's session".
///
/// <para><b>The command takes no parameters beyond the tenant, and that is Decision A showing
/// through.</b> FR-2.8 names its five categories and the product ships exactly those five, always,
/// with no per-category checkboxes and no date range: "export my data" is one button, not a form.
/// A date filter on the two transactional categories is the obvious follow-up if the row cap ever
/// bites a real tenant, and is recorded as deferred rather than dismissed.</para>
///
/// <para><b>This is the only point where an identity matters</b> (Decision D). The permission check
/// and the <c>Audit</c> row both happen here, on a real authenticated request; the background runner
/// that produces the file has no acting user at all, because it only reads and reads through
/// org-filtered queries rather than permission-gated MediatR requests. That is Phase 20e's default,
/// which Phase 21a had to abandon for a job that writes and this one gets back.</para>
///
/// <para><c>IAuditableRequest</c> plus the "Create" prefix is all it takes for <c>AuditBehavior</c>
/// to record who generated a full-tenant dump and when. Note what is <i>not</i> audited: the
/// download itself, since <c>AuditBehavior</c> only fires on Create/Update/Approve/Void. Recording
/// each retrieval as well is a small, additive follow-up and is listed as such.</para>
/// </summary>
public sealed record CreateExportJobCommand(Guid OrganizationId)
    : IRequest<ExportJobSummary>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.ExportJobManage;

    public DocumentType AuditDocumentType => DocumentType.DataExport;
}

public sealed class CreateExportJobCommandValidator : AbstractValidator<CreateExportJobCommand>
{
    public CreateExportJobCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}

public sealed class CreateExportJobCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<CreateExportJobCommand, ExportJobSummary>
{
    /// <summary>
    /// One live export per organization at a time. Not a technical limit -- the runner would happily
    /// process a queue of them -- but a full-tenant workbook is the most expensive artifact this app
    /// produces, and an impatient user clicking Export four times should get one file, not four
    /// identical ones each holding a buffered workbook in memory.
    /// </summary>
    public async Task<ExportJobSummary> Handle(CreateExportJobCommand request, CancellationToken cancellationToken)
    {
        var alreadyRunning = await db.ExportJobs.AnyAsync(
            j => j.OrganizationId == request.OrganizationId
                 && (j.Status == ExportJobStatus.Queued || j.Status == ExportJobStatus.Running),
            cancellationToken);

        if (alreadyRunning)
        {
            throw new ConflictException(
                "An export is already running for this organization. Wait for it to finish, or cancel it first.");
        }

        var job = ExportJob.Create(
            request.OrganizationId,
            currentUser.UserId,
            ExportJobProcessor.CategoryCount,
            timeProvider.GetUtcNow());

        db.ExportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        var initiatedByName = await db.Users
            .Where(u => u.Id == job.InitiatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return ExportJobMapper.ToSummary(job, initiatedByName);
    }
}

/// <summary>List/detail row shape shared by the create and list paths.</summary>
public sealed record ExportJobSummary(
    Guid Id,
    ExportJobStatus Status,
    string? FailureReason,
    string? FileName,
    long? FileSizeBytes,
    int TotalCategoryCount,
    int ProcessedCategoryCount,
    int TotalRowCount,
    string? TruncationNotice,
    bool CancellationRequested,
    bool HasArtifact,
    Guid InitiatedByUserId,
    string InitiatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ArtifactPurgedAt);
