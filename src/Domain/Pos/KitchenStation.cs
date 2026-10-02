namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 64 -- a place a restaurant's kitchen tickets go to: the Kitchen, the Bar, the Tandoor. The
/// vendor calls it a <i>Print Profile</i> (Settings &gt; Print Profiles: a name and nothing else) and
/// splits each saved order's ticket by it (phase 59 Decision F). It is named here for what it does.
///
/// <para><b>A product with no station goes to the Default station</b>, which is not a row: the vendor's
/// ticket dialog shows <i>Kitchen</i> and <i>Default</i> side by side, and the Coke that had no profile
/// went to Default. So null on <see cref="Catalog.Product.KitchenStationId"/> and on a ticket means
/// Default, and a tenant that never makes a station still gets one ticket per send.</para>
///
/// <para><b>Deactivating a station that still has products is refused.</b> Their next ticket would
/// otherwise go to a station nobody looks at; moving them first is the change the tenant means.</para>
/// </summary>
public sealed class KitchenStation
{
    public const int MaxNameLength = 60;

    /// <summary>What a ticket with no station is called on screen and on paper.</summary>
    public const string DefaultName = "Default";

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private KitchenStation()
    {
    }

    public static KitchenStation Create(Guid organizationId, string name)
    {
        return new KitchenStation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = RequireName(name),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <param name="assignedProducts">How many products are routed here now; deactivating with any is
    /// refused (see the type's remarks).</param>
    public void Update(string name, bool isActive, int assignedProducts)
    {
        if (!isActive && IsActive && assignedProducts > 0)
        {
            throw new InvalidOperationException(
                $"{assignedProducts} product(s) still send their kitchen tickets to '{Name}'. Move them to another station first.");
        }

        Name = RequireName(name);
        IsActive = isActive;
    }

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A kitchen station needs a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidOperationException($"A kitchen station's name is at most {MaxNameLength} characters.");
        }

        if (string.Equals(trimmed, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{DefaultName}' is where products without a station already go; give this station another name.");
        }

        return trimmed;
    }
}
