namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Aggregate root for the Tenancy bounded context (architecture-spec.md §2/§4.1). Created via
/// the 3-step New Organization wizard as a single command (roadmap Phase 1b task 7) -- every
/// field here is set once at creation, not built up across separate screens.
/// </summary>
public sealed class Organization
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Industry { get; private set; } = null!;
    public string? Address { get; private set; }
    public DateOnly AccountingStartDate { get; private set; }
    public bool IsVatRegistered { get; private set; }
    public string WorkspaceName { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? PanNumber { get; private set; }
    public string? Website { get; private set; }
    /// <summary>
    /// Phase 39 -- the opaque <c>IFileStorage</c> key of the organization's logo, or null. Never a
    /// URL: every read goes through an authenticated endpoint, which is the rule IFileStorage's own
    /// remarks set out and the reason it exposes no "resolve to a public URL".
    /// </summary>
    public string? LogoStorageKey { get; private set; }

    /// <summary>The media type the logo's own bytes said it was, so the serving endpoint does not
    /// have to re-read the header on every request -- and so it never echoes a client-supplied
    /// Content-Type back to a browser.</summary>
    public string? LogoContentType { get; private set; }

    public DateOnly? LockDate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private Organization()
    {
    }

    public static Organization Create(
        string name,
        string industry,
        string? address,
        DateOnly accountingStartDate,
        bool isVatRegistered,
        string workspaceName,
        string? email,
        string? phone,
        string? panNumber,
        string? website,
        Guid createdByUserId)
    {
        return new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            Industry = industry,
            Address = address,
            AccountingStartDate = accountingStartDate,
            IsVatRegistered = isVatRegistered,
            // Normalized the same way User.Email is (Register()) -- the workspace name is
            // effectively a unique login-adjacent identifier (a subdomain slug), so it gets the
            // same case-insensitive-uniqueness treatment.
            WorkspaceName = workspaceName.Trim().ToLowerInvariant(),
            Email = email,
            Phone = phone,
            PanNumber = panNumber,
            Website = website,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Phase 43 -- the one mutator for the details this organization prints on every document it
    /// issues. The aggregate was create-only from phase 1b until now, which made eight of its own
    /// fields unreachable after the wizard closed: phase-31's rule is that a field is reachable only
    /// if you can name the command that writes it <i>and</i> the screen that calls it, and there was
    /// neither. The reference product's <c>EDIT DETAILS</c> dialog has always edited them.
    ///
    /// <para><b>What this deliberately does not take.</b> <see cref="WorkspaceName"/> is a
    /// login-adjacent unique slug, normalised and uniquely indexed, that addresses this tenant --
    /// changing it would silently break every bookmarked or shared workspace URL, and the live
    /// dialog has no counterpart field at all (its Display Name is a separate, additional label this
    /// codebase does not model). The entitlement flags stay immutable for phase-20f/41 Decision F's
    /// reason -- a tenant with stock in a FIFO ledger must not be able to turn inventory off
    /// underneath it. <see cref="LockDate"/> keeps <see cref="SetLockDate"/>: it is a ledger control
    /// with its own Admin-only key, not a detail that prints on a letterhead.</para>
    ///
    /// <para><see cref="AccountingStartDate"/> <i>is</i> here, because the live dialog edits it and a
    /// cutover date typed once in a wizard is exactly the kind of thing that gets typed wrong. What
    /// stops it becoming a silent restatement is a caller-side guard, not an invariant here: an
    /// opening-stock FIFO layer is stamped with the date that was current when it was written, and
    /// nothing restates it -- see UpdateOrganizationCommandHandler.</para>
    /// </summary>
    public void UpdateDetails(
        string name,
        string industry,
        string? address,
        DateOnly accountingStartDate,
        bool isVatRegistered,
        string? email,
        string? phone,
        string? panNumber,
        string? website)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(industry);

        if (accountingStartDate == default)
        {
            throw new InvalidOperationException("An organization needs an accounting start date.");
        }

        Name = name.Trim();
        Industry = industry.Trim();
        Address = address;
        AccountingStartDate = accountingStartDate;
        IsVatRegistered = isVatRegistered;
        Email = email;
        Phone = phone;
        PanNumber = panNumber;
        Website = website;
    }

    public void SetLockDate(DateOnly? lockDate) => LockDate = lockDate;

    /// <summary>
    /// Points the organization at a newly stored logo and <b>returns the key it replaced</b>, so the
    /// caller can delete that blob. Returning it rather than deleting here keeps the Domain free of
    /// storage, and returning it rather than leaving it to the caller to remember is what stops a
    /// replaced logo becoming an orphaned file -- phase-21b's rule that a feature which writes a
    /// blob owes its deletion story in the same phase.
    /// </summary>
    public string? SetLogo(string storageKey, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var replaced = LogoStorageKey;
        LogoStorageKey = storageKey;
        LogoContentType = contentType;

        return replaced;
    }

    /// <summary>Clears the logo and returns the key to delete, or null when there was none.</summary>
    public string? RemoveLogo()
    {
        var removed = LogoStorageKey;
        LogoStorageKey = null;
        LogoContentType = null;

        return removed;
    }
}
