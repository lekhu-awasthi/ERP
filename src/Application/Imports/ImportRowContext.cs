using ErpApp.Domain.Imports;

namespace ErpApp.Application.Imports;

/// <summary>
/// Everything a row needs beyond its own cells.
///
/// <para><b>The interesting member is <see cref="PendingKeys"/>, and it exists because the dry run
/// writes nothing.</b> In a hierarchical import a row's parent may be created by an earlier row of
/// the same file; <see cref="ImportRowSequencer"/> guarantees the ordering, so during the
/// <i>apply</i> pass that parent is genuinely in the database by the time its child is planned, and
/// the importer resolves it by name without knowing or caring where it came from. During the
/// <i>validate</i> pass nothing has been created, so the identical lookup would come back empty and
/// the review screen would report a file that is perfectly correct as a file full of errors.</para>
///
/// <para>So the two passes differ in exactly one value: <see cref="PendingKeys"/> is empty during
/// apply and holds every key the file creates during validation. A hierarchical importer that
/// cannot find a parent asks <see cref="WillCreate"/> before rejecting the row, and answers a yes by
/// returning <see cref="ImportRowPlan.Provisional"/> -- a plan that deliberately cannot be
/// executed, so the distinction can never be lost by a later caller sending one.</para>
/// </summary>
/// <param name="ImportJobId">The run itself, so a row can record which upload produced it. Phase 55
/// is the first importer whose rows want that: a bank statement has no natural key, so a file
/// uploaded twice cannot be detected, and the answer is to make the whole upload one thing to
/// delete rather than to invent a key that is not there.</param>
/// <param name="BankAccountId">The cash-and-bank account a <c>BankStatement</c> run imports into,
/// and null for every other entity type -- see <c>ImportJob.BankAccountId</c> for why a statement
/// row's account is per-run context rather than a column repeated on every row.</param>
public sealed record ImportRowContext(
    Guid OrganizationId,
    ImportMode Mode,
    IReadOnlySet<string> PendingKeys,
    Guid ImportJobId,
    Guid? BankAccountId)
{
    /// <summary>The apply pass: no keys are pending, because the sequencer already created them.</summary>
    public static ImportRowContext ForApply(
        Guid organizationId, ImportMode mode, Guid importJobId, Guid? bankAccountId) =>
        new(organizationId, mode, new HashSet<string>(StringComparer.OrdinalIgnoreCase), importJobId, bankAccountId);

    /// <summary>The dry run: <paramref name="pendingKeys"/> is every key this file would create.</summary>
    public static ImportRowContext ForValidation(
        Guid organizationId,
        ImportMode mode,
        IEnumerable<string> pendingKeys,
        Guid importJobId,
        Guid? bankAccountId) =>
        new(
            organizationId,
            mode,
            new HashSet<string>(pendingKeys, StringComparer.OrdinalIgnoreCase),
            importJobId,
            bankAccountId);

    /// <summary>True when this file creates <paramref name="key"/> itself and has not done so yet.</summary>
    public bool WillCreate(string key) => PendingKeys.Contains(key);
}
