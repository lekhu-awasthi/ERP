using MediatR;

namespace ErpApp.Application.Imports;

/// <summary>
/// What one spreadsheet row <i>would</i> do, built but not yet done.
///
/// <para><b>This type is Phase 38's Decision B in one object.</b> Phase 21a's importers went from a
/// row straight to <c>sender.Send(...)</c> in a single method, which meant there was no moment at
/// which the intended command existed and had not yet run -- and therefore no way to show a user
/// what an import was about to do. Splitting <c>ApplyAsync</c> into
/// <see cref="IEntityImporter.PlanAsync"/> (resolve every foreign key by name, coerce every cell,
/// build the command) and this object's <see cref="ExecuteAsync"/> (send it) creates that moment
/// without duplicating a line of the resolution logic: the dry run and the real run plan rows the
/// same way, and the dry run simply stops.</para>
///
/// <para><b>Why the closed generic and not a bare <c>object</c>.</b> The dry run has to run the
/// command's own FluentValidation validators, which are registered as <c>IValidator&lt;TCommand&gt;</c>
/// -- resolvable from the runtime type either way -- but the apply pass also has to <i>send</i> the
/// command and map its response, and <c>ISender.Send(object)</c> loses the response type. Keeping
/// the pair on a generic subtype means each importer states its own command and response once, and
/// the processor never names either.</para>
/// </summary>
/// <param name="Action">Human-readable summary of the intent, shown in the review step -- for
/// example "Create product 'Extra Energy Biscuit'". Present because "127 records validated" tells a
/// user how many rows parsed, not what the import will do to their data.</param>
/// <param name="TargetCode">The business code this row will write to, when the row already names one
/// (update mode). Null in create mode, where the code does not exist until the command runs.</param>
public abstract record ImportRowPlan(string Action, string? TargetCode)
{
    /// <summary>The command itself, for validator resolution -- null on a
    /// <see cref="Provisional"/> plan, which has no command yet. Never sent through this property;
    /// see the remark above.</summary>
    public abstract object? Request { get; }

    public abstract Task<ImportRowResult> ExecuteAsync(ISender sender, CancellationToken cancellationToken);

    /// <summary>Builds a plan without every call site restating both type arguments.</summary>
    public static ImportRowPlan<TRequest, TResponse> For<TRequest, TResponse>(
        TRequest command,
        string action,
        string? targetCode,
        Func<TResponse, ImportRowResult> toResult)
        where TRequest : IRequest<TResponse> =>
        new(command, action, targetCode, toResult);

    /// <summary>
    /// "This row is fine, and its command cannot be built yet." Returned only by a hierarchical
    /// importer during the dry run, for a row whose parent a <i>later</i> row of the same file
    /// creates -- see <see cref="ImportRowContext.PendingKeys"/>.
    ///
    /// <para>It throws rather than silently doing nothing if anybody tries to execute it, because
    /// the alternative -- a plan whose parent reference quietly resolved to "no parent" -- would
    /// flatten an imported tree and look like a success.</para>
    /// </summary>
    public static ImportRowPlan Provisional(string action) => new ProvisionalImportRowPlan(action);
}

/// <inheritdoc cref="ImportRowPlan"/>
public sealed record ImportRowPlan<TRequest, TResponse>(
    TRequest Command,
    string Action,
    string? TargetCode,
    Func<TResponse, ImportRowResult> ToResult)
    : ImportRowPlan(Action, TargetCode)
    where TRequest : IRequest<TResponse>
{
    public override object? Request => Command;

    public override async Task<ImportRowResult> ExecuteAsync(ISender sender, CancellationToken cancellationToken) =>
        ToResult(await sender.Send(Command, cancellationToken));
}

/// <inheritdoc cref="ImportRowPlan.Provisional"/>
public sealed record ProvisionalImportRowPlan(string Action) : ImportRowPlan(Action, TargetCode: null)
{
    public override object? Request => null;

    public override Task<ImportRowResult> ExecuteAsync(ISender sender, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "A provisional import row plan is a dry-run artifact and must never be executed: its parent "
                + "had not been created yet when it was built. The apply pass plans every row afresh, in "
                + "ImportRowSequencer's order, and so never produces one.");
}
