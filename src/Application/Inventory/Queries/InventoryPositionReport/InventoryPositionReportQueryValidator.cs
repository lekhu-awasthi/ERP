using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.InventoryPositionReport;

public sealed class InventoryPositionReportQueryValidator : AbstractValidator<InventoryPositionReportQuery>
{
    public InventoryPositionReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // Phase 44 -- Display Warehouse in Column is a modifier of Group by Warehouse, not an
        // alternative to it. The live drawer enforces this by disabling the control until Group by
        // Warehouse is ticked (Moonbeam 2026-09-15, `input.disabled === true`); a screen's disabled
        // attribute is not a server rule, so the server states it too.
        //
        // A 400 naming the field rather than silently ignoring the flag: phase-43's rule that a
        // field which will not be honoured should be refused rather than accepted and dropped,
        // because present-and-ignored reads to a client as accepted.
        this.RuleFor(x => x.DisplayWarehouseInColumn)
            .Must((query, displayInColumn) => !displayInColumn || query.GroupByWarehouse)
            .WithMessage("Display Warehouse in Column requires Group by Warehouse.");

        // Phase 58 -- an unknown mode is a 400 naming the field, never a silent fall-back.
        this.RuleFor(x => x.Mode).IsInEnum();
    }
}
