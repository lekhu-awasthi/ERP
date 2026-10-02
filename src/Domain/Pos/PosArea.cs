namespace ErpApp.Domain.Pos;

public enum PosTableShape
{
    Rectangle = 1,
    Circle = 2,
}

/// <summary>
/// Phase 64 -- one part of a restaurant's floor (Ground Floor, Rooftop) with the tables laid out on
/// it: the vendor's Floorplan tab, read live on 2026-10-02 (<c>erp-module-scan.md</c>, "Restaurant,
/// read live").
///
/// <para><b>The canvas is fixed</b>, as the vendor's is: every area is <see cref="CanvasWidth"/> by
/// <see cref="CanvasHeight"/> logical units, and a table's position and size are in those units. The
/// screen scales the canvas to fit; nothing stored depends on a screen's size.</para>
///
/// <para><b>Tables are deactivated, never deleted.</b> The vendor's table dialog offers <i>Make
/// Inactive</i> and no delete, and here an order names its table, so a deleted table would take an
/// order's history with it. An area is the same.</para>
/// </summary>
public sealed class PosArea
{
    public const int CanvasWidth = 1100;
    public const int CanvasHeight = 800;
    public const int MaxNameLength = 60;

    /// <summary>More tables than any one room holds; a bound, not a rule.</summary>
    public const int MaxTables = 200;

    private readonly List<PosTable> _tables = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BillingLocationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<PosTable> Tables => _tables;

    private PosArea()
    {
    }

    public static PosArea Create(Guid organizationId, Guid billingLocationId, string name)
    {
        if (billingLocationId == Guid.Empty)
        {
            throw new InvalidOperationException("An area belongs to a billing location.");
        }

        return new PosArea
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BillingLocationId = billingLocationId,
            Name = RequireName(name, "An area"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isActive)
    {
        Name = RequireName(name, "An area");
        IsActive = isActive;
    }

    /// <summary>
    /// Applies a whole layout, as the vendor's single <i>Save Changes</i> does, and returns the tables
    /// it created for the caller to add through its own set (phase 24: a child appended to a tracked
    /// parent's collection is otherwise seen as Modified, not Added).
    ///
    /// <para><b>Every existing table must be in the layout.</b> The vendor's save replaces the area's
    /// whole table set; here leaving a table out would either delete it (refused: orders name tables)
    /// or keep it silently (a save that ignores what it was sent). So omitting one is refused, and a
    /// table leaves the floor by being made inactive.</para>
    /// </summary>
    public IReadOnlyList<PosTable> ApplyLayout(IReadOnlyList<PosTableLayout> layout)
    {
        if (layout.Count > MaxTables)
        {
            throw new InvalidOperationException($"An area holds at most {MaxTables} tables.");
        }

        var named = layout.Where(x => x.Id is not null).Select(x => x.Id!.Value).ToList();
        if (named.Distinct().Count() != named.Count)
        {
            throw new InvalidOperationException("A table appears twice in the layout.");
        }

        var unknown = named.Where(id => _tables.All(t => t.Id != id)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException("The layout names a table that is not on this area.");
        }

        var missing = _tables.Where(t => !named.Contains(t.Id)).Select(t => t.Name).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The layout leaves out {string.Join(", ", missing)}. A table is never deleted; make it inactive instead.");
        }

        var names = layout.Select(x => (x.Name ?? string.Empty).Trim()).ToList();
        var duplicate = names
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two tables are named '{duplicate.Key}'. A table's name is unique at its location.");
        }

        var added = new List<PosTable>();

        foreach (var row in layout)
        {
            if (row.Id is { } id)
            {
                _tables.Single(t => t.Id == id).Apply(row);
            }
            else
            {
                var table = PosTable.Create(OrganizationId, BillingLocationId, Id, row);
                _tables.Add(table);
                added.Add(table);
            }
        }

        return added;
    }

    internal static string RequireName(string? name, string what)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException($"{what} needs a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidOperationException($"{what}'s name is at most {MaxNameLength} characters.");
        }

        return trimmed;
    }
}

/// <summary>One table as a layout save describes it. <see cref="Id"/> is null for a new table.</summary>
public sealed record PosTableLayout(
    Guid? Id,
    string Name,
    int Capacity,
    PosTableShape Shape,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsActive);

/// <summary>
/// Phase 64 -- a table on an area's canvas. Its name is unique at its <b>location</b>, not just its
/// area, because a kitchen ticket and a waiter both say "T1": two T1s in one restaurant would be a
/// ticket the kitchen cannot place. The vendor refuses a duplicate with "table name must be unique"
/// (a 400, read live).
///
/// <para><b>Whether it is occupied is not stored.</b> The vendor writes the order's id onto the table
/// row; here a table is occupied exactly when an open order names it, which a filtered unique index
/// on <c>PosOrder</c> makes true by construction, so there is no second copy to drift.</para>
/// </summary>
public sealed class PosTable
{
    /// <summary>Bigger than any real table; the vendor's dialog accepts 1000 and anything above.</summary>
    public const int MaxCapacity = 100;

    public const int MinSize = 40;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BillingLocationId { get; private set; }
    public Guid PosAreaId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Capacity { get; private set; }
    public PosTableShape Shape { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PosTable()
    {
    }

    internal static PosTable Create(Guid organizationId, Guid billingLocationId, Guid areaId, PosTableLayout row)
    {
        var table = new PosTable
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BillingLocationId = billingLocationId,
            PosAreaId = areaId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        table.Apply(row);
        return table;
    }

    internal void Apply(PosTableLayout row)
    {
        if (row.Capacity < 1 || row.Capacity > MaxCapacity)
        {
            throw new InvalidOperationException($"A table seats between 1 and {MaxCapacity}.");
        }

        if (!Enum.IsDefined(row.Shape))
        {
            throw new InvalidOperationException($"'{row.Shape}' is not a table shape.");
        }

        if (row.Width < MinSize || row.Height < MinSize)
        {
            throw new InvalidOperationException($"A table is at least {MinSize} units wide and tall.");
        }

        if (row.X < 0 || row.Y < 0 || row.X + row.Width > PosArea.CanvasWidth || row.Y + row.Height > PosArea.CanvasHeight)
        {
            throw new InvalidOperationException(
                $"Every table must sit inside the {PosArea.CanvasWidth} by {PosArea.CanvasHeight} floor.");
        }

        Name = PosArea.RequireName(row.Name, "A table");
        Capacity = row.Capacity;
        Shape = row.Shape;
        X = row.X;
        Y = row.Y;
        Width = row.Width;
        Height = row.Height;
        IsActive = row.IsActive;
    }
}
