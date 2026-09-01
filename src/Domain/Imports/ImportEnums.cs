namespace ErpApp.Domain.Imports;

/// <summary>
/// What a bulk-import job is loading (product-requirements.md FR-2.9). The member set is
/// deliberately narrower than the reference product's own Upload Type dropdown, which was read
/// live during Phase 21a's confirm-live pass and offers exactly seven options: Product, Customer,
/// Supplier, Contact, Account, Product Category, Account Group.
///
/// <para><b>Three of the seven ship here; four are deferred as mechanical follow-up</b> (see
/// docs/phase-21a-status.md). The three chosen are not arbitrary: Product has by far the richest
/// column set and the most foreign keys to resolve by name, and Customer/Supplier exercise the
/// <c>ContactType</c> discriminator through a single importer -- between them they cover every
/// mechanism a later type would reuse.</para>
///
/// <para><b>A confirm-live correction worth carrying:</b> the reference product's "Contact" upload
/// type is <i>not</i> our Contacts.Contact aggregate. Its template columns are Code / Contact Name /
/// Contact Group / Phone No / Email / Address / <b>Organisation</b> / <b>Title</b>, with the
/// instruction "&quot;Organisation&quot; should exactly match with customer or supplier name in the
/// existing contact list" -- i.e. a person attached to a Customer or Supplier, which in this
/// codebase is <c>ContactPersonnel</c> (Phase 18), a different aggregate entirely. Naming it here
/// would have quietly produced the wrong importer.</para>
/// </summary>
public enum ImportEntityType
{
    Product,
    Customer,
    Supplier,
}

/// <summary>
/// Confirmed live: the reference product's "Select action" dropdown has exactly two options,
/// "Create New Records" and "Update Existing Records" -- and offers both for five of its seven
/// upload types, restricting Product Category and Account Group to Create only. All three types
/// this phase ships support both modes.
/// </summary>
public enum ImportMode
{
    CreateNew,
    UpdateExisting,
}

/// <summary>
/// <para><b>The distinction that matters here is Completed vs Failed</b> (docs/phase-21a-status.md,
/// Decision C). FR-2.9 requires row-level error reporting, which means partial success is the
/// <i>normal</i> outcome, not an error: a file where 3 rows are rejected and 997 are created is a
/// <see cref="Completed"/> job with a non-zero failed-row count, and the 997 stay created.</para>
///
/// <para><see cref="Failed"/> is therefore deliberately narrow -- it means the job could not
/// process rows <i>at all</i>: the uploaded file could not be read, its column headers do not match
/// the template, it contains no data rows, or the initiating user's permission was revoked between
/// enqueue and execution. Anything a single row can do to itself is a row outcome, never a job
/// outcome.</para>
/// </summary>
public enum ImportJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// <para><see cref="Pending"/> is not a waiting state a reader should ever see on a finished job --
/// it is the <b>claim</b>. The processor inserts the row Pending and commits it <i>before</i>
/// sending the create/update command, exactly as Phase 20e's AlertSendLog claims a send before
/// calling SMTP. That ordering is what makes a crashed import safe to resume: the row already
/// exists, so the resumed run skips it and cannot create the same Product twice.</para>
///
/// <para>The cost of that ordering is the mirror image of 20e's: a process that dies between the
/// claim and the command leaves a Pending row whose real outcome is unknown. Finalisation converts
/// any such row to <see cref="Failed"/> with an explicit "interrupted" reason rather than guessing,
/// so the user sees precisely which rows to re-upload.</para>
/// </summary>
public enum ImportJobRowStatus
{
    Pending,
    Succeeded,
    Failed,
}
