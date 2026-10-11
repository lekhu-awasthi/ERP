using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// Phase 81 -- a current user who may price a Quotation, Sales Order or Invoice: an Accepted member of
/// <paramref name="organizationId"/> holding <c>Sales.Invoice.Discount</c> and
/// <c>Sales.Invoice.ChangeRate</c> organization-wide (<see cref="PermissionGrantSeed"/>).
///
/// <para>The six sales Create/Update handlers now ask <c>SalesPriceGate</c> before saving a discount or
/// a rate other than the catalogue's, and the tests that use them as a seeding step type whatever rate
/// suits their arithmetic. Those tests are about reports and postings, not about who may price, so
/// they seed through this. <c>SalesPriceGateTests</c> is where the refusals are pinned.</para>
/// </summary>
public static class SalesPricing
{
    public static async Task<FakeCurrentUserService> PricerAsync(IAppDbContext db, Guid organizationId)
    {
        var userId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(
            db, organizationId, userId, PermissionKeys.InvoiceDiscount, PermissionKeys.InvoiceChangeRate);
        return new FakeCurrentUserService(userId);
    }
}
