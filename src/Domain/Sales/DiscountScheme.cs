namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 97 -- a dated discount a sale picks up by itself: a percent off certain items (<see cref="DiscountSchemeKind.ItemWise"/>),
/// off every item in certain categories (<see cref="DiscountSchemeKind.CategoryWise"/>), or off a whole bill once it reaches
/// an amount (<see cref="DiscountSchemeKind.Slab"/>).
///
/// <para><b>Ours, not the vendor's.</b> The reference product's POS settings show a Discount Schemes screen with these three
/// kinds, but it is a mock-up (read live 2026-10-21 in its bundle): the list is a static empty array, Save only navigates
/// back, and the product picker offers one hard-coded item. So every rule here is the user's (2026-10-21): a scheme applies
/// at the till and on the ERP's Quotation, Sales Order and Invoice; it gives a percent only (the vendor's "Issue Qty", free
/// goods, is left out); a slab is judged on the bill's amount before VAT; and the scheme's percent is where a line starts,
/// which anyone may keep and which changing needs the discount key it always needed. The vendor's Vat Inclusive/Exclusive
/// field is left out too: a percent off a VAT-inclusive price is the same percent off the price before VAT.</para>
///
/// <para>The lines and locations are rows of their own (<see cref="DiscountSchemeLine"/>, <see cref="DiscountSchemeLocation"/>)
/// that an edit replaces wholesale, so they are built here (<see cref="BuildLines"/>), where the rules for each kind live, and
/// added through their own sets (phase-4 bug #1's remedy, without the navigation that needed it).</para>
/// </summary>
public sealed class DiscountScheme
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public DiscountSchemeKind Kind { get; private set; }

    /// <summary>The first and last day the scheme applies, both inclusive: a sale dated either day gets it.</summary>
    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>True: every billing location, including a document that carries none. False: only the locations listed in
    /// <see cref="DiscountSchemeLocation"/>.</summary>
    public bool AllLocations { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private DiscountScheme()
    {
    }

    public static DiscountScheme Create(
        Guid organizationId, string code, string name, DiscountSchemeKind kind, DateOnly startDate, DateOnly endDate,
        bool allLocations)
    {
        var scheme = new DiscountScheme
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        scheme.Set(code, name, kind, startDate, endDate, allLocations);
        return scheme;
    }

    public void Update(
        string code, string name, DiscountSchemeKind kind, DateOnly startDate, DateOnly endDate, bool allLocations,
        bool isActive)
    {
        Set(code, name, kind, startDate, endDate, allLocations);
        IsActive = isActive;
    }

    /// <summary>Whether a sale dated <paramref name="date"/> falls inside the scheme's dates.</summary>
    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>
    /// The scheme's rows for this kind, checked: an item-wise row names a product and a category-wise row a category, each
    /// at most once; a slab row names neither, and its minimum bill amounts are distinct. Every rate is above 0 and at most
    /// 100. At least one row.
    /// </summary>
    public IReadOnlyList<DiscountSchemeLine> BuildLines(IReadOnlyList<DiscountSchemeLineInput> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A discount scheme needs at least one row.");
        }

        foreach (var line in lines)
        {
            if (line.DiscountPct <= 0m || line.DiscountPct > 100m || decimal.Round(line.DiscountPct, 2) != line.DiscountPct)
            {
                throw new InvalidOperationException(
                    "A scheme's discount is more than 0% and at most 100%, with at most two decimal places.");
            }

            var shaped = Kind switch
            {
                DiscountSchemeKind.ItemWise => line.ProductId is not null && line.CategoryId is null && line.MinimumAmount is null,
                DiscountSchemeKind.CategoryWise => line.CategoryId is not null && line.ProductId is null && line.MinimumAmount is null,
                _ => line.MinimumAmount is >= 0m && line.ProductId is null && line.CategoryId is null,
            };
            if (!shaped)
            {
                throw new InvalidOperationException(Kind switch
                {
                    DiscountSchemeKind.ItemWise => "Each row of an item-wise scheme names one product.",
                    DiscountSchemeKind.CategoryWise => "Each row of a category-wise scheme names one category.",
                    _ => "Each row of a slab scheme names a bill amount of 0 or more.",
                });
            }
        }

        var keys = lines.Select(x => (object?)x.ProductId ?? (object?)x.CategoryId ?? x.MinimumAmount).ToList();
        if (keys.Distinct().Count() != keys.Count)
        {
            throw new InvalidOperationException(Kind switch
            {
                DiscountSchemeKind.ItemWise => "A product is listed twice in this scheme.",
                DiscountSchemeKind.CategoryWise => "A category is listed twice in this scheme.",
                _ => "Two slabs start at the same bill amount.",
            });
        }

        return
        [
            .. lines.Select((x, i) => new DiscountSchemeLine(
                OrganizationId, Id, i, x.ProductId, x.CategoryId, x.MinimumAmount, x.DiscountPct)),
        ];
    }

    /// <summary>The rows naming where the scheme applies: none when it applies everywhere, else at least one.</summary>
    public IReadOnlyList<DiscountSchemeLocation> BuildLocations(IReadOnlyList<Guid> locationIds)
    {
        if (AllLocations)
        {
            return [];
        }

        if (locationIds.Count == 0)
        {
            throw new InvalidOperationException("A scheme that is not for every location names at least one.");
        }

        return [.. locationIds.Distinct().Select(x => new DiscountSchemeLocation(OrganizationId, Id, x))];
    }

    private void Set(string code, string name, DiscountSchemeKind kind, DateOnly startDate, DateOnly endDate, bool allLocations)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > CodeMaxLength)
        {
            throw new InvalidOperationException($"A scheme's code is 1 to {CodeMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new InvalidOperationException($"A scheme's name is 1 to {NameMaxLength} characters.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException("Unknown kind of discount scheme.");
        }

        if (endDate < startDate)
        {
            throw new InvalidOperationException("A scheme cannot end before it starts.");
        }

        Code = code.Trim();
        Name = name.Trim();
        Kind = kind;
        StartDate = startDate;
        EndDate = endDate;
        AllLocations = allLocations;
    }
}

public enum DiscountSchemeKind
{
    ItemWise,
    CategoryWise,
    Slab,
}

/// <param name="MinimumAmount">A slab's threshold: a bill whose amount before VAT is at least this gets the row's percent
/// (the highest threshold reached wins within one scheme).</param>
public sealed record DiscountSchemeLineInput(Guid? ProductId, Guid? CategoryId, decimal? MinimumAmount, decimal DiscountPct);

/// <summary>Phase 97 -- one row of a <see cref="DiscountScheme"/>. Exactly one of the three keys is set, the one its kind
/// names.</summary>
public sealed class DiscountSchemeLine
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DiscountSchemeId { get; private set; }
    public int SortOrder { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public decimal? MinimumAmount { get; private set; }
    public decimal DiscountPct { get; private set; }

    private DiscountSchemeLine()
    {
    }

    internal DiscountSchemeLine(
        Guid organizationId, Guid discountSchemeId, int sortOrder, Guid? productId, Guid? categoryId, decimal? minimumAmount,
        decimal discountPct)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        DiscountSchemeId = discountSchemeId;
        SortOrder = sortOrder;
        ProductId = productId;
        CategoryId = categoryId;
        MinimumAmount = minimumAmount;
        DiscountPct = discountPct;
    }
}

/// <summary>Phase 97 -- a billing location a <see cref="DiscountScheme"/> that is not for every location applies at.</summary>
public sealed class DiscountSchemeLocation
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid DiscountSchemeId { get; private set; }
    public Guid BillingLocationId { get; private set; }

    private DiscountSchemeLocation()
    {
    }

    internal DiscountSchemeLocation(Guid organizationId, Guid discountSchemeId, Guid billingLocationId)
    {
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        DiscountSchemeId = discountSchemeId;
        BillingLocationId = billingLocationId;
    }
}
