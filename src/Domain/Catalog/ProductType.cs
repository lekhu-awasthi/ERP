namespace ErpApp.Domain.Catalog;

/// <summary>Discriminator for Product (architecture-spec.md §4.3). Immutable after creation --
/// same reasoning as ContactType (see phase-3-status.md's scope decisions).</summary>
public enum ProductType
{
    Goods,
    Service,
}
