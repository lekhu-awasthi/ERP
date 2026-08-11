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
/// </summary>
public sealed class Product
{
    private readonly List<ProductSecondaryUnit> _secondaryUnits = [];

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
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? SalesAccountId { get; private set; }
    public Guid? SalesReturnAccountId { get; private set; }
    public Guid? PurchaseAccountId { get; private set; }
    public Guid? PurchaseReturnAccountId { get; private set; }

    public IReadOnlyList<ProductSecondaryUnit> SecondaryUnits => _secondaryUnits;

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
        bool trackInventory)
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
        bool isActive)
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
    }

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
