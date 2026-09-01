using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Domain.Imports;

namespace ErpApp.Application.Imports;

/// <summary>One place that turns an <see cref="ImportJob"/> into its wire shape, shared by the
/// create, list and get paths so the three cannot drift.</summary>
internal static class ImportJobMapper
{
    public static ImportJobSummary ToSummary(ImportJob job, string initiatedByName) =>
        new(
            job.Id,
            job.EntityType,
            job.Mode,
            job.FileName,
            job.Status,
            job.FailureReason,
            job.TotalRowCount,
            job.ProcessedRowCount,
            job.SucceededRowCount,
            job.FailedRowCount,
            job.CancellationRequested,
            job.InitiatedByUserId,
            initiatedByName,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt);
}
