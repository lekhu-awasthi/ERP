using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.AdditionalCostGrid;

/// <summary>
/// Phase 38 -- the product-wise Additional Cost grid's Import, the phase-29 carried item, read live
/// on 2026-09-12 rather than inferred.
///
/// <para><b>Phase 29 recorded it as "a bulk paste of the grid" and that was wrong.</b> Ticking
/// "Add product-wise" on the reference product's Purchase Bill reveals an <b>Import</b> button next
/// to the checkbox; it opens a drawer with a drop zone, the instruction <i>"The uploaded product's
/// additional cost must be in accordance with the template. Download the template from the link
/// below, enter the product-specific additional costs, and upload it here"</i>, and a
/// <b>Download Template (.xlsx)</b> button. It is a file import, not a clipboard paste.</para>
///
/// <para><b>And the template is unlike the other eight in this codebase</b>, which is the finding
/// worth carrying: it is generated <i>from the bill in front of you</i>. The downloaded file has one
/// sheet, a header row of <c>Products</c> plus <b>one column per tenant cost term</b> in the
/// tenant's own order, and the bill's own product lines already filled into column A with the amount
/// cells blank. There is no instruction block, no sample row and no <c>**</c> required marker --
/// none of which would mean anything, because a landed-cost matrix has no meaning away from its
/// bill.</para>
///
/// <para><b>This is therefore not an <c>ImportJob</c>, and that is a decision rather than an
/// omission.</b> Every other importer in this phase writes master data through a permission-gated
/// command and must survive a crash; this one fills in a grid on a form the user has not saved yet.
/// Nothing is written, there is nothing to resume, and a background job would put the answer
/// somewhere the form cannot see it. The file is parsed and handed straight back.</para>
/// </summary>
/// <param name="ProductIds">The bill's own product lines, in the order the form shows them. The
/// template's rows are these, which is what makes the downloaded file worth anything.</param>
/// <remarks>
/// <b><c>ILocationAgnosticRequest</c>, and it is the <c>Preview*GlPosting</c> case exactly</b>
/// (phase 32b). Both requests here compute over a payload the caller is holding in an <i>unsaved
/// form</i>: there is no Purchase Bill row yet, no stored <c>LocationId</c>, and nothing
/// per-location to leak -- the template lists cost terms and the products the caller just named,
/// and the parser hands those same products back. Refusing them for a branch-scoped Member would be
/// the failure that marker exists to prevent: a user allowed to write the bill but not to fill in
/// its grid.
/// </remarks>
public sealed record GetAdditionalCostGridTemplateQuery(Guid OrganizationId, IReadOnlyList<Guid> ProductIds)
    : IRequest<AdditionalCostGridTemplate>, IRequirePermission, IOrganizationScoped, ILocationAgnosticRequest
{
    // The grid belongs to a Purchase Bill being edited, so the key is the one that lets somebody
    // edit one. A separate key would be a permission nobody could reason about, and the template
    // exposes this tenant's cost-term list and product names -- exactly what Edit already sees.
    public string PermissionKey => PermissionKeys.PurchaseBillEdit;
}

/// <param name="CostTerms">One column each, landed-cost terms only -- a production cost term is not
/// selectable on a Purchase Bill (phase 29), so offering one here would produce a column whose
/// values the form must then reject.</param>
/// <param name="Products">One row each, pre-filled in column A.</param>
public sealed record AdditionalCostGridTemplate(
    IReadOnlyList<AdditionalCostGridColumn> CostTerms,
    IReadOnlyList<AdditionalCostGridProduct> Products);

public sealed record AdditionalCostGridColumn(Guid CostTermId, string Name);

public sealed record AdditionalCostGridProduct(Guid ProductId, string Name);

public sealed class GetAdditionalCostGridTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAdditionalCostGridTemplateQuery, AdditionalCostGridTemplate>
{
    public async Task<AdditionalCostGridTemplate> Handle(
        GetAdditionalCostGridTemplateQuery request, CancellationToken cancellationToken)
    {
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.IsActive
                        && x.Category == CostTermCategory.AdditionalCost)
            .OrderBy(x => x.Name)
            .Select(x => new AdditionalCostGridColumn(x.Id, x.Name))
            .ToListAsync(cancellationToken);

        var productIds = request.ProductIds.Distinct().ToList();

        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        // Ordered by the caller's own list rather than by name: the file's rows must line up with
        // the lines the user is looking at on the form.
        var byId = products.ToDictionary(p => p.Id, p => p.Name);
        var ordered = productIds
            .Where(byId.ContainsKey)
            .Select(id => new AdditionalCostGridProduct(id, byId[id]))
            .ToList();

        return new AdditionalCostGridTemplate(costTerms, ordered);
    }
}

/// <summary>
/// Reads a filled-in grid back and hands the form its cells. <b>Writes nothing</b> -- see the query
/// above for why this is not an import job.
/// </summary>
public sealed record ParseAdditionalCostGridCommand(Guid OrganizationId, Stream Content)
    : IRequest<AdditionalCostGridResult>, IRequirePermission, IOrganizationScoped, ILocationAgnosticRequest
{
    public string PermissionKey => PermissionKeys.PurchaseBillEdit;
}

/// <param name="Cells">Every non-zero amount the file carried, resolved to ids.</param>
/// <param name="Errors">Per-cell problems, in the same <c>Row / Column / Message</c> shape the
/// import screen uses, so the drawer can render them the way the review step does.</param>
public sealed record AdditionalCostGridResult(
    IReadOnlyList<AdditionalCostGridCell> Cells,
    IReadOnlyList<AdditionalCostGridError> Errors);

public sealed record AdditionalCostGridCell(Guid ProductId, Guid CostTermId, decimal Amount);

public sealed record AdditionalCostGridError(int RowNumber, string? ColumnName, string Message);

public sealed class ParseAdditionalCostGridCommandHandler(IAppDbContext db, IImportFileReader fileReader)
    : IRequestHandler<ParseAdditionalCostGridCommand, AdditionalCostGridResult>
{
    private const string ProductColumn = "Products";

    public async Task<AdditionalCostGridResult> Handle(
        ParseAdditionalCostGridCommand request, CancellationToken cancellationToken)
    {
        var sheet = await fileReader.ReadAsync(request.Content, cancellationToken);

        var headers = sheet.Headers.Select(ImportRowReader.Normalize).ToList();
        var columnIndexes = ImportRowReader.BuildColumnIndexes(headers);

        if (!columnIndexes.ContainsKey(ProductColumn))
        {
            throw new ImportFileException(
                $"The file has no '{ProductColumn}' column. Download the template from this dialog and fill that in.");
        }

        // Resolved by name, like every other importer here, and from this tenant only.
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.IsActive
                        && x.Category == CostTermCategory.AdditionalCost)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var costTermByName = costTerms.ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var productsByName = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        // Two products of one tenant may share a name; a grid row naming an ambiguous one is an
        // error rather than a coin flip, so the lookup keeps the count.
        var productGroups = productsByName
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var cells = new List<AdditionalCostGridCell>();
        var errors = new List<AdditionalCostGridError>();

        foreach (var row in sheet.Rows.Where(r => !r.IsBlank))
        {
            var reader = new ImportRowReader(columnIndexes, row);

            var productName = reader.GetOptionalString(ProductColumn);
            if (productName is null)
            {
                errors.Add(new AdditionalCostGridError(row.RowNumber, ProductColumn, "This row names no product."));
                continue;
            }

            if (!productGroups.TryGetValue(productName, out var matches))
            {
                errors.Add(new AdditionalCostGridError(
                    row.RowNumber, ProductColumn, $"'{productName}' is not a product in this organization."));
                continue;
            }

            if (matches.Count > 1)
            {
                errors.Add(new AdditionalCostGridError(
                    row.RowNumber,
                    ProductColumn,
                    $"'{productName}' matches more than one product; rename one of them, or type the amounts in by hand."));
                continue;
            }

            foreach (var header in headers.Where(h => h.Length > 0 && h != ProductColumn))
            {
                if (!costTermByName.TryGetValue(header, out var costTermId))
                {
                    // An unknown column is ignored rather than rejected, exactly as ImportRowReader
                    // ignores a user's own notes column -- and the header is only ever an extra one,
                    // because the template writes the tenant's real cost terms.
                    continue;
                }

                decimal amount;
                try
                {
                    amount = reader.GetOptionalDecimal(header);
                }
                catch (ImportRowException ex)
                {
                    errors.Add(new AdditionalCostGridError(row.RowNumber, header, ex.Message));
                    continue;
                }

                if (amount < 0)
                {
                    errors.Add(new AdditionalCostGridError(
                        row.RowNumber, header, "An additional cost cannot be negative."));
                    continue;
                }

                // Zero is "no cost for this pair", not a cell to send: the grid's empty state and a
                // typed 0 mean the same thing to the bill, and sending every empty cell of a 40x9
                // grid would bury the ones that matter.
                if (amount > 0)
                {
                    cells.Add(new AdditionalCostGridCell(matches[0].Id, costTermId, amount));
                }
            }
        }

        return new AdditionalCostGridResult(cells, errors);
    }
}
