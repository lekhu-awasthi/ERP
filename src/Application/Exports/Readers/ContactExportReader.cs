using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// FR-2.8's "contacts" category -- Customers and Suppliers in one sheet, discriminated by the Type
/// column, exactly as they are one aggregate in the domain.
///
/// <para>This sheet carries PAN, phone and email for every contact the tenant has, which is the
/// single largest reason <c>Configuration.ExportJob.View</c> is Admin-only. See PermissionKeys.</para>
/// </summary>
public sealed class ContactExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.Contacts;

    public string SheetName => "Contacts";

    public IReadOnlyList<string> Headers { get; } =
    [
        "Code",
        "Name",
        "Type",
        "Contact Group",
        "PAN",
        "Phone No",
        "Email",
        "Address",
        "Opening Balance",
        "Active",
        "Created At",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, CancellationToken cancellationToken)
    {
        var query =
            from contact in db.Contacts
            where contact.OrganizationId == organizationId
            join contactGroup in db.ContactGroups on contact.GroupId equals contactGroup.Id into groups
            from contactGroup in groups.DefaultIfEmpty()
            orderby contact.Code
            select new
            {
                contact.Code,
                contact.Name,
                contact.Type,
                GroupName = contactGroup == null ? null : contactGroup.Name,
                contact.Pan,
                contact.Phone,
                contact.Email,
                contact.Address,
                contact.OpeningBalance,
                contact.IsActive,
                contact.CreatedAt,
            };

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);

        var rows = page
            .Select(c => new object?[]
            {
                c.Code,
                c.Name,
                c.Type.ToString(),
                c.GroupName,
                c.Pan,
                c.Phone,
                c.Email,
                c.Address,
                c.OpeningBalance,
                c.IsActive,
                ExportCell.LocalTimestamp(c.CreatedAt),
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
