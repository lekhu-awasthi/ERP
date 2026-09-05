using ErpApp.Domain.Purchasing;

namespace ErpApp.Application.Purchasing;

/// <summary>
/// One row of the Purchase Bill's Additional Cost section (FR-6.15), as posted by the client.
/// <paramref name="ProductId"/> null is the live picker's "All Product".
///
/// <para><see cref="AdditionalCostMethod"/> is required even in the live "Add product-wise" mode,
/// where the form hides the Method column: a product-wise cell still needs a rule if that product
/// happens to sit on two lines of the same bill, and defaulting it there to
/// <see cref="AdditionalCostMethod.Value"/> in the client keeps one shape on the wire instead of
/// two.</para>
/// </summary>
public sealed record PurchaseBillAdditionalCostInput(
    Guid CostTermId, Guid? ProductId, AdditionalCostMethod Method, decimal Amount);
