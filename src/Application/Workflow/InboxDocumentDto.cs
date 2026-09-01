using System.Text.Json;
using ErpApp.Application.Common.DocumentExtraction;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;

namespace ErpApp.Application.Workflow;

/// <summary>
/// One inbox row as the grid and the detail pane both read it. <see cref="IsLinked"/> is projected
/// from the aggregate rather than left to the client to derive from
/// <see cref="LinkedTransactionId"/>, and the UI gates its "+ Add as", Delete and Reopen controls
/// on it -- never on <see cref="Status"/>, which a user can also set by hand (the same
/// gate-on-the-fact-not-the-status discipline as phase-21b's <c>ExportJob.HasArtifact</c>).
/// </summary>
public sealed record InboxDocumentDto(
    Guid Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    string? Description,
    string? Label,
    UploadedDocumentStatus Status,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    bool IsLinked,
    DocumentType? LinkedTransactionType,
    Guid? LinkedTransactionId,
    DateTimeOffset? LinkedAt,
    DocumentExtractionStatus ExtractionStatus,
    string? ExtractionModelId,
    string? ExtractionFailureReason,
    DateTimeOffset? ExtractionAttemptedAt,
    bool IsExtractable,
    ExtractedDocumentData? ExtractedData);

public static class InboxDocumentMapper
{
    /// <summary>Matches the serializer <c>ExtractInboxDocumentCommandHandler</c> writes with, so a
    /// round trip through <c>UploadedDocument.ExtractedDataJson</c> is symmetric.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static InboxDocumentDto ToDto(UploadedDocument document, string uploadedByName)
    {
        return new InboxDocumentDto(
            document.Id,
            document.FileName,
            document.SizeBytes,
            document.ContentType,
            document.Description,
            document.Label,
            document.Status,
            document.UploadedByUserId,
            uploadedByName,
            document.UploadedAt,
            document.IsLinked,
            document.LinkedTransactionType,
            document.LinkedTransactionId,
            document.LinkedAt,
            document.ExtractionStatus,
            document.ExtractionModelId,
            document.ExtractionFailureReason,
            document.ExtractionAttemptedAt,
            InboxDocumentValidation.IsExtractable(document.FileName),
            Deserialize(document.ExtractedDataJson));
    }

    /// <summary>
    /// A stored suggestion that no longer parses is treated as no suggestion at all, not as an
    /// error. The JSON was written by a previous version of this code (or, in principle, by a model
    /// whose output changed shape), and a document is always convertible by hand -- failing the
    /// whole list query because one row's machine guess is stale would be exactly backwards.
    /// </summary>
    public static ExtractedDocumentData? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ExtractedDocumentData>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
