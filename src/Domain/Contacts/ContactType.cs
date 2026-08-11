namespace ErpApp.Domain.Contacts;

/// <summary>Discriminator for Contact (architecture-spec.md §4.2). Immutable after creation --
/// switching a Customer into a Supplier is a modeling smell the reference product doesn't
/// expose either (see phase-3-status.md's scope decisions).</summary>
public enum ContactType
{
    Customer,
    Supplier,
    Lead,
}
