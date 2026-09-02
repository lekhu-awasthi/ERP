namespace ErpApp.Application.Catalog.Variants;

/// <summary>One variant child as every variant command/query returns it -- the live product's
/// Variant Details table (SKU/Barcode, Name, Selling Price, Purchase Price) plus the combination
/// itself, which that table shows only through the composed Name.</summary>
public sealed record ProductVariantResult(
    Guid Id,
    Guid ParentProductId,
    string Code,
    string Name,
    string? Sku,
    string? Barcode,
    decimal SellingPrice,
    decimal PurchasePrice,
    bool IsActive,
    IReadOnlyList<ProductVariantValueResult> AttributeValues);

public sealed record ProductVariantValueResult(
    Guid AttributeId, string AttributeName, Guid OptionId, string OptionValue);

/// <summary>A parent's "Attributes Used" pool, grouped for display exactly as the live panel shows
/// it: one row per attribute, carrying the options that product offers.</summary>
public sealed record ProductVariantAttributeUsageResult(
    Guid AttributeId, string AttributeName, IReadOnlyList<ProductVariantOptionRef> Options);

public sealed record ProductVariantOptionRef(Guid OptionId, string Value);

/// <summary>One (attribute, option) pair on the wire.</summary>
public sealed record VariantCombinationInput(Guid AttributeId, Guid OptionId);
