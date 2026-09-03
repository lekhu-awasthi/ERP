namespace ErpApp.Domain.Identity;

/// <summary>
/// One authentication event -- a successful login, a failed login attempt, or a logout. Phase 26c's
/// only new stored entity, written because the User Log report is <b>not derivable from
/// <c>Audit</c></b>: <c>AuditBehavior</c> records MediatR requests that a signed-in user made, so it
/// can never see the attempt that never became a session.
///
/// <para><b>There is deliberately no OrganizationId.</b> Signing in is an application-level act,
/// not a tenant one -- a user authenticates first and only then picks an organization, and a failed
/// attempt has no organization to belong to even in principle. Making the row tenant-scoped would
/// mean inventing a tenant for the rows that matter most. <c>UserLogQueryHandler</c> instead scopes
/// the *report* by joining to <c>OrganizationMembership</c>, which is where the "who may see this"
/// question actually lives.</para>
///
/// <para><b><see cref="Email"/> is always populated; <see cref="UserId"/> need not be.</b> A failed
/// attempt against an address that matches no <c>User</c> row still records the address that was
/// tried -- that is the whole security value of the log, and it is why the email is stored on the
/// event rather than read through the (possibly absent) user. It is stored normalised, the same
/// trim-and-lowercase <c>LoginCommandHandler</c> applies before its own lookup, so a report keyed by
/// email matches regardless of how the attempt was typed.</para>
///
/// <para><b>Device and Browser are parsed at write time, not at read time.</b> The report shows the
/// operating system in its Device column and the browser plus version in its Device Info column;
/// parsing once on the way in means one parser to test and a plain read on the way out, and
/// <see cref="UserAgent"/> keeps the raw header so a future reading can be re-derived without the
/// original request.</para>
///
/// <para>Append-only: there is no mutator on this type at all. An authentication event is a fact
/// about a moment, and nothing that happens later can change what was attempted.</para>
/// </summary>
public sealed class UserLoginEvent
{
    /// <summary>Longest value any user agent yields in practice, with room to spare; the column is
    /// capped so a hostile header cannot write an unbounded row.</summary>
    public const int UserAgentMaxLength = 512;

    public Guid Id { get; private set; }

    /// <summary>Null when the attempted email matched no user -- see the type's own remarks.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>The attempted email, normalised (trimmed, lower-cased). Never null.</summary>
    public string Email { get; private set; } = null!;

    public UserLoginOutcome Outcome { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Null when the request carried no usable remote address (a test host, for one).</summary>
    public string? IpAddress { get; private set; }

    /// <summary>The raw User-Agent header, truncated to <see cref="UserAgentMaxLength"/>.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>The operating system, as the report's Device column shows it ("Windows 10").</summary>
    public string? DeviceOs { get; private set; }

    /// <summary>Browser and version, as the report's Device Info column shows it ("Chrome 152.0.0.0").</summary>
    public string? Browser { get; private set; }

    private UserLoginEvent()
    {
    }

    public static UserLoginEvent Create(
        Guid? userId,
        string email,
        UserLoginOutcome outcome,
        DateTimeOffset occurredAt,
        string? ipAddress,
        string? userAgent,
        string? deviceOs,
        string? browser)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("A user login event needs the email that was used.");
        }

        return new UserLoginEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email.Trim().ToLowerInvariant(),
            Outcome = outcome,
            OccurredAt = occurredAt,
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, UserAgentMaxLength),
            DeviceOs = deviceOs,
            Browser = browser,
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
