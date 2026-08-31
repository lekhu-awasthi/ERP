using ErpApp.Domain.Configuration;

namespace ErpApp.Application.Alerts;

/// <summary>
/// Builds the email body for one <see cref="AlertType"/>. One implementation per enum member,
/// resolved by <see cref="AlertType"/> at dispatch time -- the same one-strategy-per-document-type
/// registration shape IGlPostingRule&lt;T&gt; uses, and readable for the same reason.
///
/// <para><b>This interface is the answer to "how does a jobless command authenticate?".</b> It
/// deliberately takes an explicit <paramref name="organizationId"/> and reads IAppDbContext
/// directly instead of sending a MediatR request, so the background dispatcher never needs a
/// current user, never traverses AuthorizationBehavior/FeatureGateBehavior/AuditBehavior, and
/// therefore introduces no authentication-bypass surface at all -- CurrentUserService keeps
/// throwing outside an HTTP context, exactly as it did before this phase. The access control for
/// alerts lives entirely at definition time (Configuration.AlertDefinition.Manage), and the
/// content each implementation may produce is fixed and bounded by the implementation itself
/// rather than by a report the scheduling admin got to choose. See docs/phase-20e-status.md,
/// Decision B.</para>
/// </summary>
public interface IAlertContentBuilder
{
    AlertType AlertType { get; }

    /// <param name="occurrenceDate">The tenant-local business day being reported on.</param>
    Task<AlertContent> BuildAsync(Guid organizationId, DateOnly occurrenceDate, CancellationToken cancellationToken);
}
