using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Writes one append-only Audit row (UserId, Action, DocumentType, DocumentId, Timestamp) per
/// successful Create/Update/Approve/Void command (architecture-spec.md §3.9, roadmap Phase 16d),
/// in the one shared place every such command flows through -- not bespoke logging per handler.
/// Registered 5th in DependencyInjection.AddApplication, after LockDateBehavior: a request that
/// fails validation, permission, or lock-date never reaches here, so it never produces an audit
/// row (it never happened). Runs after the handler (post-`next()`), since a Create command's new
/// DocumentId isn't known until the handler's response comes back.
///
/// A request that implements neither <see cref="ILockDateSensitiveDocument"/> (Approve/Void) nor
/// <see cref="IAuditableRequest"/>/<see cref="IAuditableRequestWithId"/> (Create/Update) is simply
/// not audited -- the same "no marker interface, no gate" pattern AuthorizationBehavior/
/// LockDateBehavior already use, deliberately not a blocking requirement for unrelated commands.
///
/// Issues its own explicit SaveChangesAsync here, uniformly for every audited command (including
/// Approve/Void, whose DocumentId is already known pre-handler) -- one code path, not perfectly
/// atomic with the handler's own commit (a vanishingly rare crash-window gap between the two), an
/// accepted trade-off for an internal admin audit trail rather than a reason to build
/// cross-transaction coordination. See docs/phase-16d-status.md.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse>(IAppDbContext db, ICurrentUserService currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Action is derived from the request's own type name, not a separate field on every
    /// command -- LoggingBehavior already reflects over the same typeof(TRequest).Name for its own
    /// purposes, so this isn't a new pattern. "Convert" isn't a distinct action: every conversion in
    /// this codebase (e.g. CreateInvoiceCommand's optional ReferrerType/ReferrerId) is a plain
    /// Create under the hood, so it's already covered by the "Create" prefix.</summary>
    private static readonly string[] AuditedActionPrefixes = ["Create", "Update", "Approve", "Void"];

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is not IOrganizationScoped scoped)
        {
            return response;
        }

        var (documentType, documentId) = ResolveDocument(request, response);
        if (documentType is null || documentId is null)
        {
            return response;
        }

        var action = ResolveAction();
        if (action is null)
        {
            return response;
        }

        db.Audits.Add(Audit.Create(
            scoped.OrganizationId, currentUser.UserId, action, documentType.Value, documentId.Value));
        await db.SaveChangesAsync(cancellationToken);

        return response;
    }

    private static (DocumentType? DocumentType, Guid? DocumentId) ResolveDocument(TRequest request, TResponse response)
    {
        return request switch
        {
            ILockDateSensitiveDocument approval => (approval.LockDateDocumentType, approval.LockDateDocumentId),
            IAuditableRequestWithId withId => (withId.AuditDocumentType, withId.AuditDocumentId),
            IAuditableRequest auditable => (auditable.AuditDocumentType, TryGetResponseId(response)),
            _ => (null, null),
        };
    }

    /// <summary>Reflection, not another marker interface, is the deliberate choice here -- every
    /// CreateXResult/UpdateXResult/ApproveXResult/VoidXResult in this codebase already has a
    /// leading `Guid Id` property by convention (confirmed across Invoice/PurchaseBill/Payment/...),
    /// and this is a plain post-execution read of a CLR object, not an EF LINQ query -- the
    /// "generic Func selector fails EF translation" gotcha (phase-9-status.md) doesn't apply here.
    /// Adding a shared marker interface to ~50 result records for one property every one of them
    /// already has would be pure ceremony.</summary>
    private static Guid? TryGetResponseId(TResponse response)
    {
        if (response is null)
        {
            return null;
        }

        var property = typeof(TResponse).GetProperty("Id");
        return property?.PropertyType == typeof(Guid) ? (Guid?)property.GetValue(response) : null;
    }

    private static string? ResolveAction()
    {
        var requestName = typeof(TRequest).Name;
        foreach (var prefix in AuditedActionPrefixes)
        {
            if (requestName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        return null;
    }
}
