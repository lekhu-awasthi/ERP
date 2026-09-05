namespace ErpApp.Domain.Purchasing;

/// <summary>
/// How one Additional Cost row spreads its Amount across the purchase bill lines it applies to
/// (FR-6.15). Confirmed live 2026-09-04 on the reference product's Purchase Bill add form: the
/// Method column's dropdown offers exactly these two, and defaults to <see cref="Value"/>.
///
/// <para>The basis is always taken over the <i>goods</i> lines in scope, never every line -- see
/// <see cref="PurchaseBill.AllocateAdditionalCosts"/> for why a service line is excluded here even
/// though the reference product offers one in its picker.</para>
/// </summary>
public enum AdditionalCostMethod
{
    /// <summary>Pro rata by each line's net Amount (post-discount, pre-VAT) -- the live default.</summary>
    Value = 1,

    /// <summary>Pro rata by each line's Quantity, regardless of what it cost.</summary>
    Quantity = 2,
}
