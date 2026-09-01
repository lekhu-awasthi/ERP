namespace ErpApp.Application.Common.Security;

/// <summary>
/// <b>Phase 21a, Decision B -- the identity Phase 20e was able to avoid.</b>
///
/// <para>A scheduled alert only reads, so <c>AlertDispatcher</c> could send no MediatR request at
/// all and needed no acting user. A bulk import <i>writes</i>: it creates Products and Contacts, and
/// every rule about doing that correctly (code generation, foreign-key existence, FluentValidation,
/// the audit row, the permission check) lives in the existing Create/Update handlers and their
/// pipeline. Reimplementing those in a parallel import-only write path would duplicate the rules and
/// then drift from them, so the job reuses the commands -- and reusing them requires an acting
/// user.</para>
///
/// <para><b>What makes that defensible:</b> the identity is not fabricated. The initiating user was
/// authenticated and permission-checked by a real HTTP request at enqueue time, their id is
/// persisted on <c>ImportJob.InitiatedByUserId</c>, and the job is that user's own action deferred.
/// Because the commands travel the normal pipeline, <c>AuthorizationBehavior</c> <b>re-checks the
/// permission on every row at execution time</b>: a user removed from the organisation, or stripped
/// of <c>Catalog.Product.Manage</c>, between enqueue and run has their job stopped, not honoured.
/// <c>AuditBehavior</c> likewise attributes every created row to them by name.</para>
///
/// <para><b>What contains the danger:</b>
/// <list type="bullet">
/// <item>It is a plain scoped service, not an <c>AsyncLocal</c>. Only code holding this exact
/// instance -- the import runner, which resolves it from the scope it just created -- can call
/// <see cref="Assume"/>. There is no ambient channel for unrelated code to set it through.</item>
/// <item><b>An HTTP request can never be served by it.</b> <c>CurrentUserService</c> consults this
/// only when there is no <c>HttpContext</c> at all; inside a request the JWT wins unconditionally,
/// so even a hypothetical rogue <see cref="Assume"/> call in a request scope changes nothing.</item>
/// <item>Assignment is single-shot per scope: a job cannot switch users mid-run.</item>
/// <item>It grants no permission of its own. It only names <i>who</i> is acting; <i>whether</i> they
/// may still act is re-derived from the database on every single command.</item>
/// </list></para>
///
/// <para>The residual risk, stated plainly: a bug in the runner that assumed the wrong user id would
/// perform writes as that user. The mitigation is that the id is read from the job row the runner
/// just claimed and is never derived from anything client-supplied.</para>
/// </summary>
public interface IJobActingUser
{
    /// <summary>Null in every scope that is not a background job's -- which is every HTTP scope.</summary>
    Guid? UserId { get; }

    /// <summary>Names the user this background scope acts as. Callable once per scope.</summary>
    void Assume(Guid userId);
}

/// <inheritdoc cref="IJobActingUser"/>
public sealed class JobActingUser : IJobActingUser
{
    public Guid? UserId { get; private set; }

    public void Assume(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A background job cannot act as an empty user id.", nameof(userId));
        }

        if (UserId is { } existing && existing != userId)
        {
            throw new InvalidOperationException(
                "This scope has already assumed a different acting user; create a new scope per job instead.");
        }

        UserId = userId;
    }
}
