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
    /// Resolves one row into the command it would send, <b>without sending it</b>. Throws
    /// <see cref="ImportRowException"/> for anything the user can fix in the spreadsheet; any other
    /// exception is also caught by the processor and recorded against the row, so a single
    /// malformed row can never take the job down.
    ///
    /// <para><b>Phase 38 split this out of the old <c>ApplyAsync</c></b> so the dry run and the real
    /// run share it exactly (see <see cref="ImportRowPlan"/>). Everything an importer can check
    /// cheaply -- required cells, type coercion, enum spellings, name-to-id foreign key resolution,
    /// the update-mode code lookup -- belongs here, because everything here is a rejection the user
    /// can be shown <i>before</i> anything is written. What deliberately stays behind is whatever
    /// only the handler knows (uniqueness races, lifecycle conflicts); those surface as row failures
    /// during the apply pass, exactly as they did before.</para>
    /// </summary>
    Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken);
}

/// <summary>
/// Implemented by the importers whose rows can point at <b>each other</b>.
///
/// <para><b>Exactly two types qualify, and the third the roadmap named does not.</b>
/// <c>ProductCategory</c> and <c>AccountGroup</c> are self-referencing trees, so a row's parent may
/// legitimately appear further down the same file -- the reference product's own templates say so
/// ("Parent category should already exist or should be in the upcoming rows", "Make sure there are
/// no cyclic dependencies"). <c>Account</c> looks like the third because its template has an
/// "Account Group" column, but that column points at a <i>different aggregate</i> which must already
/// exist; an Account file has no edges inside it at all. Solving the ordering problem three times
/// would have been solving it twice too often.</para>
///
/// <para>The sequencer this drives (<see cref="ImportRowSequencer"/>) runs over the whole file
/// before a single row is written, in both the dry-run and the apply pass, so a cycle is a
/// whole-file rejection naming its rows rather than a half-finished tree.</para>
/// </summary>
public interface IHierarchicalImporter
{
    /// <summary>Template column holding the row's own name -- the value another row's
    /// <see cref="ParentColumn"/> would match.</summary>
    string KeyColumn { get; }

    /// <summary>Template column naming this row's parent. Blank means a root.</summary>
    string ParentColumn { get; }
}

/// <param name="TargetId">The Product/Contact created or updated.</param>
/// <param name="TargetCode">Its business code, shown in the results grid.</param>
public sealed record ImportRowResult(Guid TargetId, string? TargetCode);
