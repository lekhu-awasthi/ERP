using ErpApp.Domain.Imports;

namespace ErpApp.Application.Imports;

/// <summary>
/// One entity type's import strategy: it owns the template's columns and knows how to turn a single
/// row into the create or update command for that type. Resolved by
/// <see cref="ImportJobProcessor"/> from the injected <c>IEnumerable</c>, exactly as
/// <c>AlertDispatcher</c> resolves <c>IAlertContentBuilder</c> and as <c>IGlPostingRule&lt;T&gt;</c>
/// is resolved for posting -- adding an entity type is a new class plus one DI line, with no change
/// to the processor.
///
/// <para><b>Implementations must send the real create/update command through MediatR</b>
/// (Decision B), never write the entity directly. Every rule about creating a Product or a Contact
/// correctly -- code generation, foreign-key existence, FluentValidation, the audit row, and the
/// permission check itself -- already lives in those handlers and their pipeline. A parallel
/// import-only write path would duplicate all of it and then drift from it.</para>
///
/// <para><b>On the template's sample row:</b> a user who uploads the template unmodified gets that
/// row rejected by the ordinary rules (its "Category"/"Contact Group" names do not exist in a real
/// tenant), which is the desired outcome -- one clearly-reported row error, not a phantom
/// "Kathmandu Suppliers Private Limited" in their contact list. No special-casing detects and skips
/// it, because a heuristic that silently drops a row matching some remembered sample text would
/// eventually drop a real one.</para>
/// </summary>
public interface IEntityImporter
{
    ImportEntityType EntityType { get; }

    ImportTemplateDefinition Template { get; }

    /// <summary>
    /// Applies one row. Throws <see cref="ImportRowException"/> for anything the user can fix in the
    /// spreadsheet; any other exception is also caught by the processor and recorded against the
    /// row, so a single malformed row can never take the job down.
    /// </summary>
    Task<ImportRowResult> ApplyAsync(
        Guid organizationId, ImportMode mode, ImportRowReader row, CancellationToken cancellationToken);
}

/// <param name="TargetId">The Product/Contact created or updated.</param>
/// <param name="TargetCode">Its business code, shown in the results grid.</param>
public sealed record ImportRowResult(Guid TargetId, string? TargetCode);
