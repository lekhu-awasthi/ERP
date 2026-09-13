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
/// <para><b>Phase 21b took no parameters beyond the tenant; Phase 38 added two, and the follow-up
/// it added them for is the one that phase named.</b> 21b shipped FR-2.8's five categories always,
/// with no checkboxes and no date range, and recorded a date filter as "the obvious follow-up if the
/// row cap ever bites a real tenant". Phase 34c then measured a real tenant hitting it by 8x. So
/// both parameters are here, and both default to the 21b behaviour: omit them and "export my data"
/// is still one button that gets all of it.</para>
///
/// <para><b>They are also the honest answer to the row cap</b>, which is why they arrived in the
/// same phase that raised it. A cap on a buffered workbook cannot be raised indefinitely -- see
/// <c>ExportLimits</c> for the memory law it expresses -- so the way to get 200,000 ledger rows out
/// of this product is to ask for them a quarter at a time, and that is only possible if the request
/// can say which categories and which dates.</para>
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
/// <param name="Categories">Which categories to include. <b>Null or empty means all of them</b>,
/// which is what a caller that predates this parameter meant and what the button still means.</param>
/// <param name="FromDate">Inclusive start of the window, or null. Ignored by the categories that
/// have no date -- the workbook says which, per sheet.</param>
/// <param name="ToDate">Inclusive end of the window, or null.</param>
public sealed record CreateExportJobCommand(
    Guid OrganizationId,
    IReadOnlyList<ExportCategory>? Categories = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<ExportJobSummary>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.ExportJobManage;

    public DocumentType AuditDocumentType => DocumentType.DataExport;

    /// <summary>The selection as the job will store it: distinct, and in the workbook's own sheet
    /// order rather than the caller's, so two identical requests are identical jobs.</summary>
    public IReadOnlyList<ExportCategory> ResolvedCategories =>
        Categories is null || Categories.Count == 0
            ? ExportJobProcessor.AllCategories
            : [.. ExportJobProcessor.AllCategories.Where(Categories.Contains)];
}

public sealed class CreateExportJobCommandValidator : AbstractValidator<CreateExportJobCommand>
{
    public CreateExportJobCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();

        RuleForEach(x => x.Categories)
            .IsInEnum()
            .When(x => x.Categories is not null);

        // A range whose end precedes its start is a typo that would otherwise produce an empty
        // workbook and look like an empty tenant -- phase 34c's "a benchmark against an empty tenant
        // looks like a spectacularly fast one", in the shape a user meets it.
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate!.Value)
            .When(x => x.FromDate is not null && x.ToDate is not null)
            .WithMessage("The end of the date range cannot be before its start.");
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
            request.ResolvedCategories,
            request.FromDate,
            request.ToDate,
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
    IReadOnlyList<ExportCategory> Categories,
    DateOnly? FromDate,
    DateOnly? ToDate,
    bool CancellationRequested,
    bool HasArtifact,
    Guid InitiatedByUserId,
    string InitiatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ArtifactPurgedAt);
