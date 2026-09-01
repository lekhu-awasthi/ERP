using ErpApp.Application.Exports.Commands.CreateExportJob;
using ErpApp.Domain.Exports;

namespace ErpApp.Application.Exports;

/// <summary>One place that turns an <see cref="ExportJob"/> into its wire shape, shared by the
/// create and list paths so the two cannot drift.</summary>
internal static class ExportJobMapper
{
    public static ExportJobSummary ToSummary(ExportJob job, string initiatedByName) =>
        new(
            job.Id,
            job.Status,
            job.FailureReason,
            job.FileName,
            job.FileSizeBytes,
            job.TotalCategoryCount,
            job.ProcessedCategoryCount,
            job.TotalRowCount,
            job.TruncationNotice,
            job.CancellationRequested,
            job.HasArtifact,
            job.InitiatedByUserId,
            initiatedByName,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ExpiresAt,
            job.ArtifactPurgedAt);
}
