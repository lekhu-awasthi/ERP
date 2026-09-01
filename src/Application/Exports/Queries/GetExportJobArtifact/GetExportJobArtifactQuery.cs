using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Exports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Queries.GetExportJobArtifact;

/// <summary>
/// Resolves a completed export to the storage key its file lives under, for the download endpoint
/// (<b>Decision F</b>).
///
/// <para><b>Every download goes through here, and here is where the access control is.</b>
/// <c>IFileStorage</c> deliberately has no "resolve to a public URL" method (Phase 18, decision #1),
/// so there is no path a browser could hit directly and nothing to guess: the caller presents a job
/// id, <c>AuthorizationBehavior</c> checks <c>Configuration.ExportJob.View</c> and org membership
/// before this handler runs, and the handler then re-filters by <c>OrganizationId</c> by hand --
/// which is what makes a cross-tenant id a 404 rather than a leak. Same pattern as
/// <c>GetAttachmentForDownloadQuery</c>.</para>
///
/// <para>Two "gone" cases are distinguished on purpose, because they mean different things to the
/// person clicking: a job that never produced a file (queued, running, failed, cancelled) versus one
/// whose file retention has since deleted (Decision E). The second is not an error the user caused,
/// and telling them so is the difference between "regenerate it" and "something is broken".</para>
/// </summary>
public sealed record GetExportJobArtifactQuery(Guid OrganizationId, Guid Id)
    : IRequest<ExportArtifactDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExportJobView;
}

public sealed record ExportArtifactDto(string StorageKey, string FileName, string ContentType);

public sealed class GetExportJobArtifactQueryValidator : AbstractValidator<GetExportJobArtifactQuery>
{
    public GetExportJobArtifactQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class GetExportJobArtifactQueryHandler(IAppDbContext db)
    : IRequestHandler<GetExportJobArtifactQuery, ExportArtifactDto>
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<ExportArtifactDto> Handle(
        GetExportJobArtifactQuery request, CancellationToken cancellationToken)
    {
        var job = await db.ExportJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Export job not found.");

        if (job.ArtifactPurgedAt is not null)
        {
            throw new NotFoundException(
                "This export has expired and its file has been deleted. Generate a new one.");
        }

        if (job.Status != ExportJobStatus.Completed || job.StorageKey is null || job.FileName is null)
        {
            throw new NotFoundException($"This export has no file to download ({job.Status}).");
        }

        return new ExportArtifactDto(job.StorageKey, job.FileName, XlsxContentType);
    }
}
