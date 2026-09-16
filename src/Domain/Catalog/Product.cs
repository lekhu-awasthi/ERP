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
    private readonly List<ProductLocation> _locations = [];
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

    /// <summary>
    /// Phase 51 -- the product's stock is tracked by <b>batch</b>: every receipt names the batch it
    /// belongs to, and a batch's quantity is a GROUP BY over the FIFO layers carrying its id (see
    /// <see cref="ProductBatch"/>). Off by default, so a tenant that never turns it on sees no new
    /// control -- the same additive shape as <see cref="TrackInventory"/>.
    ///
    /// <para><b>The parent/variant rule, stated once.</b> The flag is copied down by
    /// <see cref="CreateVariant"/> exactly as <see cref="TrackInventory"/> already is, so on a
    /// variant <i>parent</i> it is a template for the variants it generates and nothing else. A
    /// parent can never hold a batch, and it needs no new rule to stop it: a batch is minted only by
    /// a document line naming one, and phase 24's <c>ProductVariantRules</c> already refuses a
    /// parent on every document line in the app. That is why phase 51 added no refusal of its own --
    /// the existing rule covers it transitively, which <c>ProductTrackingFlagTests</c> asserts so
    /// the reasoning is not merely believed.</para>
    /// </summary>
    public bool BatchTracking { get; private set; }

    /// <summary>
    /// Phase 51 -- the product's stock is tracked by <b>serial number</b>: one physical unit per
    /// row, which in this model is a FIFO layer of quantity one carrying
    /// <c>StockLedgerEntry.SerialNo</c>. Off by default; copied down by
    /// <see cref="CreateVariant"/>; same parent rule as <see cref="BatchTracking"/>.
    ///
    /// <para>Serial tracking is <b>specific identification</b> -- issuing serial J9 relieves J9's
    /// layer even when an older layer exists -- which under a layer of quantity one is the ordinary
    /// FIFO walk with one more predicate, not a second engine. It also means a serialised product
    /// can never go negative whatever the tenant's Negative Item Balance setting says: "issue a
    /// serial that was never received" is a typo, not an oversell.</para>
    /// </summary>
    public bool SerialTracking { get; private set; }

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

    /// <summary>Phase 36 -- the billing locations this product is available at. <b>Empty means
    /// every location</b>, which is what the live control shows as <c>All</c> and what every
    /// product created before this phase has. See <see cref="ProductLocation"/>.</summary>
    public IReadOnlyList<ProductLocation> Locations => _locations;

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
        string? barcode = null,
        bool batchTracking = false,
        bool serialTracking = false)
    {
        EnsureTrackingIsCoherent(type, trackInventory, batchTracking, serialTracking);

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
            BatchTracking = batchTracking,
            SerialTracking = serialTracking,
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
        string? barcode = null,
        bool batchTracking = false,
        bool serialTracking = false)
    {
        EnsureTrackingIsCoherent(Type, trackInventory, batchTracking, serialTracking);

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
        BatchTracking = batchTracking;
        SerialTracking = serialTracking;
        IsActive = isActive;
        Sku = Normalize(sku);
        Barcode = Normalize(barcode);
    }

    /// <summary>
    /// Phase 51 -- batch and serial tracking are dimensions <b>of the stock ledger</b>, so a product
    /// that has no stock ledger cannot carry either. A Service product never reaches
    /// <c>IStockLedgerService</c> at all, and a Goods product with Track Inventory off is the tenant
    /// saying it does not want layers for this item.
    ///
    /// <para>The validator raises the same refusal as a 400 naming the field; this is the Domain
    /// backstop, because a Domain invariant reached through an endpoint is a 500 that tells the
    /// caller nothing (phase 39).</para>
    /// </summary>
    private static void EnsureTrackingIsCoherent(
        ProductType type, bool trackInventory, bool batchTracking, bool serialTracking)
    {
        if (!batchTracking && !serialTracking)
        {
            return;
        }

        var dimension = batchTracking ? "Batch Tracking" : "Serial Number Tracking";

        if (type != ProductType.Goods)
        {
            throw new InvalidOperationException(
                $"{dimension} applies to the stock ledger, so it can only be set on a Goods product.");
        }

        if (!trackInventory)
        {
            throw new InvalidOperationException(
                $"{dimension} needs Track Inventory switched on -- it is a dimension of the stock "
                + "ledger, and without inventory tracking there are no layers to carry it.");
        }
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
            BatchTracking = BatchTracking,
            SerialTracking = SerialTracking,
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

    /// <summary>
    /// Replaces the set of billing locations this product is available at, and <b>reports what
    /// changed</b> so the handler can push the additions through the child DbSet.
    ///
    /// <para>Returning the new rows rather than letting the caller read them back off
    /// <see cref="Locations"/> is phase-24 bug #1's remedy: a child appended to an already-tracked
    /// parent's encapsulated collection is detected as <c>Modified</c>, not <c>Added</c>, on the
    /// InMemory provider, and the save then fails with a DbUpdateConcurrencyException that names
    /// nothing useful.</para>
    ///
    /// <para>An empty or null set clears the restriction, which means "available everywhere" --
    /// never "available nowhere".</para>
    /// </summary>
    public (IReadOnlyList<ProductLocation> Removed, IReadOnlyList<ProductLocation> Added) SetLocations(
        IEnumerable<Guid>? locationIds)
    {
        var wanted = locationIds?.Distinct().ToList() ?? [];

        var removed = _locations.Where(x => !wanted.Contains(x.LocationId)).ToList();
        foreach (var row in removed)
        {
            _locations.Remove(row);
        }

        var added = wanted
            .Where(id => _locations.All(x => x.LocationId != id))
            .Select(id => ProductLocation.Create(Id, id))
            .ToList();
        _locations.AddRange(added);

        return (removed, added);
    }

    /// <summary>
    /// Phase 45 -- multi-UOM x variants, settled by reading the live product rather than by
    /// deciding in advance (docs/phase-45-status.md's Decision A).
    ///
    /// <para><b>A variant owns its unit matrix; it does not inherit one.</b> On the reference
    /// tenant, variant <c>18001 Iphone 16 Pro Max XXL Blue</c> carries primary unit
    /// <i>Number (NOS)</i> while its parent and all three of its siblings carry <i>Piecess (ppp)</i>
    /// -- so a per-variant unit change did not propagate. Each variant's own detail page carries its
    /// own Secondary Unit table with its own ADD NEW (Measurement Unit, Conversion Rate, Selling
    /// Price, Purchase Price -- all blank, none pre-filled from the parent) and a per-row Action
    /// column. That is already what this codebase does for free, because a variant <i>is</i> a
    /// Product and this collection hangs off the row: nothing was stored, read through, or swept.
    /// <see cref="CreateVariant"/> copies <see cref="PrimaryUnitId"/> as the creation-time default,
    /// which is what the live "New Variant Product" form does by having no unit field at all, and it
    /// deliberately copies <b>no</b> secondary units -- a new variant starts with an empty
    /// matrix.</para>
    ///
    /// <para><b>What did have to change is the parent.</b> A variant parent's detail page in the
    /// reference product has no Inventory Details panel at all -- no stock, no Secondary Unit tab,
    /// no Warehouse tab -- and that is the same fact as phase 24's rule that a parent may not reach
    /// a document line. Phase 24's sweep-guard allow-list excused this method with the words "a
    /// secondary unit is catalog metadata ... attaching one to a parent moves nothing and reconciles
    /// against nothing", which is the argument for <i>refusing</i> it, not for permitting it. So a
    /// parent is refused here.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The product is a variant parent, the unit is the
    /// primary unit, or the unit already has a row.</exception>
    public ProductSecondaryUnit AddSecondaryUnit(
        Guid unitId, decimal conversionRate, decimal sellingPrice, decimal purchasePrice)
    {
        EnsureCarriesAUnitMatrix();

        if (unitId == PrimaryUnitId)
        {
            throw new InvalidOperationException(
                "That is this product's primary unit, which it already sells in; a secondary unit is a different one.");
        }

        if (_secondaryUnits.Any(x => x.UnitId == unitId))
        {
            throw new InvalidOperationException("This product already has a secondary unit for that unit of measurement.");
        }

        if (conversionRate <= 0)
        {
            throw new InvalidOperationException("A conversion rate must be greater than zero.");
        }

        if (sellingPrice < 0 || purchasePrice < 0)
        {
            throw new InvalidOperationException("A secondary unit's prices cannot be negative.");
        }

        var secondaryUnit = ProductSecondaryUnit.Create(Id, unitId, conversionRate, sellingPrice, purchasePrice);
        _secondaryUnits.Add(secondaryUnit);
        return secondaryUnit;
    }

    /// <summary>
    /// Phase 45 -- the edit half of the live product's per-row <i>Action</i> column. Without it a
    /// mistyped conversion rate is permanent, which is the reason this was worth building at the
    /// same time as the refusal above rather than left as "add-only, like phase 3 shipped it".
    /// </summary>
    public ProductSecondaryUnit UpdateSecondaryUnit(
        Guid secondaryUnitId, decimal conversionRate, decimal sellingPrice, decimal purchasePrice)
    {
        EnsureCarriesAUnitMatrix();

        var row = _secondaryUnits.Find(x => x.Id == secondaryUnitId)
            ?? throw new InvalidOperationException("This product has no such secondary unit.");

        if (conversionRate <= 0)
        {
            throw new InvalidOperationException("A conversion rate must be greater than zero.");
        }

        if (sellingPrice < 0 || purchasePrice < 0)
        {
            throw new InvalidOperationException("A secondary unit's prices cannot be negative.");
        }

        row.Update(conversionRate, sellingPrice, purchasePrice);
        return row;
    }

    /// <summary>
    /// The delete half. Returns the removed row so the handler can delete it through the child
    /// <c>DbSet</c> rather than leaving it to collection-navigation fixup -- the same reason
    /// <see cref="SetLocations"/> and <see cref="SetVariantAttributeUsages"/> report their changes
    /// (phase-4 bug #1 and phase-24 bug #1).
    /// </summary>
    public ProductSecondaryUnit RemoveSecondaryUnit(Guid secondaryUnitId)
    {
        var row = _secondaryUnits.Find(x => x.Id == secondaryUnitId)
            ?? throw new InvalidOperationException("This product has no such secondary unit.");

        _secondaryUnits.Remove(row);
        return row;
    }

    private void EnsureCarriesAUnitMatrix()
    {
        if (HasVariants)
        {
            throw new InvalidOperationException(
                "This product has variants, so it holds no stock of its own and has no units to convert -- "
                + "set the secondary units on each variant instead.");
        }
    }
}
