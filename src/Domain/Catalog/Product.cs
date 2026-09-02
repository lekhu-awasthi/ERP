namespace ErpApp.Domain.Catalog;

/// <summary>
/// Aggregate root for Goods/Service master data (architecture-spec.md §4.3). Not an
/// ITenantLookupEntity, same reasoning as Contacts.Contact.
///
/// Code is assigned at Create via IDocumentNumberGenerator(DocumentType.Product), same pattern as
/// Contact.Code (see that type's doc comment).
///
/// Deliberately excludes PrintProfileId (PrintingTemplate isn't built at all) -- see
/// phase-3-status.md's scope decisions. Tax is modeled as the fixed VatRate enum, not a FK, per
/// VatRate's doc comment.
///
/// SalesAccountId/SalesReturnAccountId/PurchaseAccountId/PurchaseReturnAccountId (Phase 3
/// deferred these until Accounting.Account existed) are added in Phase 4 as a clean additive
/// migration, via SetAccounts -- deliberately not wired to UpdateProductCommand/Angular yet
/// (no command sets them this phase), since nothing reads them until Sales/Purchase's posting
/// rules (Phase 5+) need a Product's default GL accounts. "Build the seam, not the feature",
/// same judgment call as JournalVoucherStatus.Void.
///
/// **Phase 24 -- a variant is a Product.** Confirmed live against the reference tenant (see
/// docs/phase-24-status.md's Decision A): "Iphone 16 Pro Max" and its four variants are five rows
/// in the same Products list, each with its own Code, prices, tax and account mappings, and the
/// invoice line picker lists them flat alongside every other product. So a variant is not a new
/// kind of thing that stock, documents and reports must learn about -- it is a Product, and
/// ProductId already means "the sellable, stockable thing". The FIFO ledger, all twelve
/// ProductId-bearing entities and every report key on it unchanged.
///
/// Three roles, distinguished by two fields and nothing else:
/// <list type="bullet">
/// <item>Ordinary product: ParentProductId null, HasVariants false. Every row in every existing
/// tenant. Transactable.</item>
/// <item>Variant parent: ParentProductId null, HasVariants true. Carries the "Attributes Used"
/// pool (<see cref="VariantAttributeUsages"/>). **Not transactable** -- see
/// <see cref="HasVariants"/>.</item>
/// <item>Variant child: ParentProductId set, HasVariants false. Carries its own combination
/// (<see cref="VariantValues"/>) and <see cref="CombinationKey"/>. Transactable, and the only way
/// its parent's stock is ever moved.</item>
/// </list>
/// </summary>
public sealed class Product
{
    private readonly List<ProductSecondaryUnit> _secondaryUnits = [];
    private readonly List<ProductVariantAttributeUsage> _variantAttributeUsages = [];
    private readonly List<ProductVariantValue> _variantValues = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public ProductType Type { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public Guid PrimaryUnitId { get; private set; }
    public string? HsCode { get; private set; }
    public bool AvailableForSale { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public VatRate VatRate { get; private set; }
    public ValuationMethod ValuationMethod { get; private set; }
    public int ReOrderLevel { get; private set; }
    public bool TrackInventory { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>FR-8.3's own two nouns, alongside the pricing this type already carried. Present on
    /// every product, not only variants -- a variant IS a product (see the type doc comment), so
    /// "each variant carrying its own SKU, barcode and pricing" is satisfied by putting them here.
    /// The reference product's own JSON carries sku_id/barcodes at product level too.</summary>
    public string? Sku { get; private set; }

    public string? Barcode { get; private set; }

    /// <summary>Set on a variant child, pointing at its variant parent. Null for an ordinary
    /// product and for a parent. Immutable: a product cannot be re-parented, because its stock
    /// layers and document lines are already its own and re-parenting would silently reassign
    /// which matrix they belong to.</summary>
    public Guid? ParentProductId { get; private set; }

    /// <summary>
    /// True on a variant parent. **A parent is not transactable** -- ProductVariantRules.EnsureNot
    /// Parent rejects it on every document line, opening stock line and adjustment. This is a
    /// deliberate divergence from the reference product, which does offer the parent in its line
    /// picker: allowing it creates a stock bucket nobody ever receives into, so Stock Position
    /// would show a parent balance that reconciles against nothing. See docs/phase-24-status.md's
    /// Decision A.
    ///
    /// Maintained only by the variant commands (<see cref="MarkHasVariants"/> /
    /// <see cref="ClearHasVariants"/>), never by <see cref="Update"/>, so an ordinary product edit
    /// cannot flip it.
    /// </summary>
    public bool HasVariants { get; private set; }

    /// <summary>Order-independent fingerprint of a variant child's combination, non-null exactly
    /// when <see cref="ParentProductId"/> is. Backs the unique index that makes "generate the
    /// matrix twice" idempotent rather than duplicating -- the same let-the-index-be-the-mechanism
    /// idiom as AlertSendLog's occurrence key (phase-20e) and ImportJobRow's (phase-21a).</summary>
    public string? CombinationKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? SalesAccountId { get; private set; }
    public Guid? SalesReturnAccountId { get; private set; }
    public Guid? PurchaseAccountId { get; private set; }
    public Guid? PurchaseReturnAccountId { get; private set; }

    public IReadOnlyList<ProductSecondaryUnit> SecondaryUnits => _secondaryUnits;

    /// <summary>A parent's "Attributes Used" pool. Empty on an ordinary product and on a child.</summary>
    public IReadOnlyList<ProductVariantAttributeUsage> VariantAttributeUsages => _variantAttributeUsages;

    /// <summary>A child's own combination, one row per attribute. Empty otherwise.</summary>
    public IReadOnlyList<ProductVariantValue> VariantValues => _variantValues;

    private Product()
    {
    }

    public static Product Create(
        Guid organizationId,
        ProductType type,
        string name,
        string code,
        Guid categoryId,
        Guid primaryUnitId,
        string? hsCode,
        bool availableForSale,
        decimal sellingPrice,
        decimal purchasePrice,
        VatRate vatRate,
        int reOrderLevel,
        bool trackInventory,
        string? sku = null,
        string? barcode = null)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Type = type,
            Name = name,
            Code = code,
            CategoryId = categoryId,
            PrimaryUnitId = primaryUnitId,
            HsCode = hsCode,
            AvailableForSale = availableForSale,
            SellingPrice = sellingPrice,
            PurchasePrice = purchasePrice,
            VatRate = vatRate,
            ValuationMethod = ValuationMethod.Fifo,
            ReOrderLevel = reOrderLevel,
            TrackInventory = trackInventory,
            Sku = Normalize(sku),
            Barcode = Normalize(barcode),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string name,
        Guid categoryId,
        Guid primaryUnitId,
        string? hsCode,
        bool availableForSale,
        decimal sellingPrice,
        decimal purchasePrice,
        VatRate vatRate,
        int reOrderLevel,
        bool trackInventory,
        bool isActive,
        string? sku = null,
        string? barcode = null)
    {
        Name = name;
        CategoryId = categoryId;
        PrimaryUnitId = primaryUnitId;
        HsCode = hsCode;
        AvailableForSale = availableForSale;
        SellingPrice = sellingPrice;
        PurchasePrice = purchasePrice;
        VatRate = vatRate;
        ReOrderLevel = reOrderLevel;
        TrackInventory = trackInventory;
        IsActive = isActive;
        Sku = Normalize(sku);
        Barcode = Normalize(barcode);
    }

    /// <summary>Promotes an ordinary product to a variant parent. Idempotent.</summary>
    public void MarkHasVariants()
    {
        if (ParentProductId is not null)
        {
            throw new InvalidOperationException("A variant cannot itself have variants.");
        }

        HasVariants = true;
    }

    /// <summary>Demotes a parent back to an ordinary product. Only reachable once its last variant
    /// has been deleted, which DeleteProductVariantCommandHandler permits only when no stock layer
    /// or document line references it -- so this can never strand transacted history behind a
    /// cleared flag.</summary>
    public void ClearHasVariants()
    {
        HasVariants = false;
        _variantAttributeUsages.Clear();
    }

    /// <summary>
    /// Replaces the parent's "Attributes Used" pool wholesale. Callers pass the full desired set;
    /// the handler is responsible for refusing to drop an option an existing variant child is
    /// actually built from (ProductVariantRules.EnsureUsagesStillCoverVariants) -- dropping one
    /// would leave those children built from an option their own parent no longer offers.
    ///
    /// Explicitly diffed rather than Clear()+AddRange(): see CLAUDE.md's full-collection-replace
    /// gotcha (phase-4 bug #1), where a same-count clear-and-re-add mis-tracked under the InMemory
    /// provider and threw DbUpdateConcurrencyException on save.
    /// </summary>
    public VariantUsageChanges SetVariantAttributeUsages(IReadOnlyList<(Guid AttributeId, Guid OptionId)> usages)
    {
        if (ParentProductId is not null)
        {
            throw new InvalidOperationException("A variant cannot itself offer attribute options.");
        }

        var removed = _variantAttributeUsages
            .Where(x => !usages.Any(u => u.AttributeId == x.VariantAttributeId && u.OptionId == x.VariantAttributeOptionId))
            .ToList();

        foreach (var row in removed)
        {
            _variantAttributeUsages.Remove(row);
        }

        var added = new List<ProductVariantAttributeUsage>();

        foreach (var (attributeId, optionId) in usages)
        {
            if (!_variantAttributeUsages.Any(x => x.VariantAttributeId == attributeId && x.VariantAttributeOptionId == optionId))
            {
                var row = ProductVariantAttributeUsage.Create(Id, attributeId, optionId);
                _variantAttributeUsages.Add(row);
                added.Add(row);
            }
        }

        HasVariants = _variantAttributeUsages.Count > 0;

        return new VariantUsageChanges(added, removed);
    }

    /// <summary>
    /// What <see cref="SetVariantAttributeUsages"/> changed, so the handler can add and remove the
    /// rows through their own DbSet instead of leaving it to collection-navigation fixup.
    ///
    /// That is not defensive style, it is required: a child newly appended to a *tracked* parent's
    /// encapsulated collection is picked up by DetectChanges in the parent's own state -- Modified,
    /// not Added -- because its key is already set, and SaveChanges then dies with
    /// DbUpdateConcurrencyException ("does not exist in the store"). Same family as CLAUDE.md's
    /// phase-4 bug #1, but reached by an add-only change rather than a clear-and-re-add.
    /// </summary>
    public sealed record VariantUsageChanges(
        IReadOnlyList<ProductVariantAttributeUsage> Added,
        IReadOnlyList<ProductVariantAttributeUsage> Removed);

    /// <summary>
    /// Creates a variant child of this parent: a real Product, inheriting the parent's Type,
    /// Category, Primary Unit, VAT rate, valuation method, HS code and all four GL account
    /// mappings -- everything that must agree for the two to belong to one matrix -- while
    /// carrying its own Code, Name, SKU, Barcode and pricing (FR-8.3's three nouns).
    /// </summary>
    /// <param name="combination">One (attributeId, optionId) pair per attribute. Order is
    /// irrelevant; <see cref="BuildCombinationKey"/> sorts before fingerprinting.</param>
    public Product CreateVariant(
        string code,
        string name,
        IReadOnlyList<(Guid AttributeId, Guid OptionId)> combination,
        decimal sellingPrice,
        decimal purchasePrice,
        string? sku,
        string? barcode)
    {
        if (ParentProductId is not null)
        {
            throw new InvalidOperationException("A variant cannot itself have variants.");
        }

        if (combination.Count == 0)
        {
            throw new InvalidOperationException("A variant needs at least one attribute value.");
        }

        if (combination.Select(x => x.AttributeId).Distinct().Count() != combination.Count)
        {
            throw new InvalidOperationException("A variant cannot take two values of the same attribute.");
        }

        foreach (var pair in combination)
        {
            if (!_variantAttributeUsages.Any(
                    x => x.VariantAttributeId == pair.AttributeId && x.VariantAttributeOptionId == pair.OptionId))
            {
                throw new InvalidOperationException(
                    "A variant can only use attribute options this product actually offers.");
            }
        }

        if (sellingPrice < 0 || purchasePrice < 0)
        {
            throw new InvalidOperationException("A variant's prices cannot be negative.");
        }

        var variant = new Product
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            Type = Type,
            Name = name.Trim(),
            Code = code,
            CategoryId = CategoryId,
            PrimaryUnitId = PrimaryUnitId,
            HsCode = HsCode,
            AvailableForSale = AvailableForSale,
            SellingPrice = sellingPrice,
            PurchasePrice = purchasePrice,
            VatRate = VatRate,
            ValuationMethod = ValuationMethod,
            ReOrderLevel = ReOrderLevel,
            TrackInventory = TrackInventory,
            Sku = Normalize(sku),
            Barcode = Normalize(barcode),
            ParentProductId = Id,
            HasVariants = false,
            CombinationKey = BuildCombinationKey(combination),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SalesAccountId = SalesAccountId,
            SalesReturnAccountId = SalesReturnAccountId,
            PurchaseAccountId = PurchaseAccountId,
            PurchaseReturnAccountId = PurchaseReturnAccountId,
        };

        foreach (var pair in combination)
        {
            variant._variantValues.Add(ProductVariantValue.Create(variant.Id, pair.AttributeId, pair.OptionId));
        }

        HasVariants = true;
        return variant;
    }

    /// <summary>Order-independent fingerprint: option ids sorted, then joined. Two generations of
    /// the same combination produce the same key regardless of the order the attributes were
    /// listed in, which is what makes the unique index a real duplicate guard rather than a
    /// formality.</summary>
    public static string BuildCombinationKey(IReadOnlyList<(Guid AttributeId, Guid OptionId)> combination)
    {
        var ids = combination.Select(x => x.OptionId.ToString("N")).OrderBy(x => x, StringComparer.Ordinal);
        return string.Join("|", ids);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void SetAccounts(
        Guid? salesAccountId, Guid? salesReturnAccountId, Guid? purchaseAccountId, Guid? purchaseReturnAccountId)
    {
        SalesAccountId = salesAccountId;
        SalesReturnAccountId = salesReturnAccountId;
        PurchaseAccountId = purchaseAccountId;
        PurchaseReturnAccountId = purchaseReturnAccountId;
    }

    public ProductSecondaryUnit AddSecondaryUnit(
        Guid unitId, decimal conversionRate, decimal sellingPrice, decimal purchasePrice)
    {
        var secondaryUnit = ProductSecondaryUnit.Create(Id, unitId, conversionRate, sellingPrice, purchasePrice);
        _secondaryUnits.Add(secondaryUnit);
        return secondaryUnit;
    }
}
