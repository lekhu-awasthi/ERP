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
