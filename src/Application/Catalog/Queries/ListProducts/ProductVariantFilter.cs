namespace ErpApp.Application.Catalog.Queries.ListProducts;

/// <summary>
/// How a product list treats the three roles a Product can now play (see Product's doc comment).
/// Defaults to <see cref="All"/> so every existing caller keeps its exact previous behaviour --
/// and so the main Products screen matches the live reference product, which does list a parent
/// and its variants together.
/// </summary>
public enum ProductVariantFilter
{
    /// <summary>Everything: ordinary products, variant parents and variant children.</summary>
    All = 0,

    /// <summary>Everything that may actually be put on a document line -- ordinary products and
    /// variant children, excluding parents. This is what every line picker asks for, and it is the
    /// query-side half of the rule ProductVariantRules enforces server-side.</summary>
    Transactable = 1,

    /// <summary>Variant parents only -- the live product's "Variant Products" sub-module, which is
    /// exactly this filter over the same Products list.</summary>
    VariantParents = 2,
}
