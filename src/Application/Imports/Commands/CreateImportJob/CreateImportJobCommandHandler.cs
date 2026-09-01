using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Imports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports.Commands.CreateImportJob;

public sealed class CreateImportJobCommandHandler(
    IAppDbContext db,
    IFileStorage fileStorage,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<CreateImportJobCommand, ImportJobSummary>
{
    public async Task<ImportJobSummary> Handle(CreateImportJobCommand request, CancellationToken cancellationToken)
    {
        // The file is persisted before the job row, not after: a storage key on a job row must
        // always resolve, whereas an orphaned blob whose job row was never written is inert.
        var storageKey = await fileStorage.SaveAsync(request.Content, request.FileName, cancellationToken);

        var job = ImportJob.Create(
            request.OrganizationId,
            request.EntityType,
            request.Mode,
            storageKey,
            request.FileName,
            currentUser.UserId,
            timeProvider.GetUtcNow());

        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        var initiatedByName = await db.Users
            .Where(u => u.Id == job.InitiatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return ImportJobMapper.ToSummary(job, initiatedByName);
    }
}
