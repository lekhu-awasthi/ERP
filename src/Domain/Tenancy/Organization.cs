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
}
