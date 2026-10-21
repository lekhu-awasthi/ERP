using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using FluentValidation;
using FluentValidation.Results;

namespace ErpApp.Application.Catalog;

/// <summary>
/// Phase 95 -- a product's own tracking switch under the company's. The vendor's product form shows its Batch and
/// Serial switches only once the company's are on (read live, phases 91 and 93); ours refuses switching one on while the
/// company's is off, a 400 naming the field. A product that already tracks keeps its flag through an edit while the
/// company's switch is off (the form sends it back unchanged), so switching the company on again restores it.
/// </summary>
public static class ProductTrackingGuard
{
    public static async Task EnsureAllowedAsync(
        IAppDbContext db,
        Guid organizationId,
        bool batchTracking,
        bool serialTracking,
        Product? existing,
        CancellationToken cancellationToken)
    {
        var turningOnBatch = batchTracking && existing?.BatchTracking != true;
        var turningOnSerial = serialTracking && existing?.SerialTracking != true;
        if (!turningOnBatch && !turningOnSerial)
        {
            return;
        }

        var switches = await StockTrackingSwitches.LoadAsync(db, organizationId, cancellationToken);
        var failures = new List<ValidationFailure>();

        if (turningOnBatch && !switches.Batch)
        {
            failures.Add(new ValidationFailure(
                nameof(Product.BatchTracking),
                "Batch tracking is switched off for this company. Switch it on under Configurations > General first."));
        }

        if (turningOnSerial && !switches.Serial)
        {
            failures.Add(new ValidationFailure(
                nameof(Product.SerialTracking),
                "Serial number tracking is switched off for this company. Switch it on under Configurations > General first."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
