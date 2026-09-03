using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Sales.Queries.SalesReturnRegister;

/// <summary>
/// The Tax Report group's <b>Sales Return Register</b> (phase 26c, slug
/// <c>sales-return-register</c>) -- the Nepal IRD statutory sales-return book. Generated live on
/// 2026-09-03; its Devanagari header set, translated: बीजक (मिति, बीजक नम्बर, खरिदकर्ताको नाम,
/// खरिदकर्ताको स्थायी लेखा नम्बर), जम्मा फिर्ता (Total Return),
/// स्थानीय कर छुटको फिर्ता मूल्य (Tax-exempt Return Value), and करयोग्य फिर्ता (Taxable Return)
/// split into मूल्य and कर. One row per approved Credit Note, values <b>positive</b>, with a footer
/// Total.
///
/// <para><b>The main Sales Register keeps its credit-note rows -- this phase's key correctness
/// finding, and it went the opposite way from the plan.</b> The roadmap asked whether the main
/// registers must now exclude notes once the return registers exist, and the 2026-09-02 catalogue
/// pass had inferred that they must. Generating both reports over the same period on 2026-09-03
/// showed otherwise: the same twelve credit notes appear in <i>both</i>, parenthesised (negative) in
/// the Sales Register and positive here, and the Sales Register's footer Total is arithmetically net
/// of them (its positive invoice rows summed to 45,068.60 and its Total was that less this
/// register's own 93,831,004,682,895.66 Total, to the cent). So phase 19's folding was correct
/// parity rather than the simplification it was recorded as, and <c>SalesRegisterQueryHandler</c>
/// is unchanged. Both reports read <c>SalesReturnReader</c>, so the magnitudes cannot drift.</para>
///
/// <para>The live Sales Register additionally carries a <b>"Include Credit Note In Calculation"</b>
/// view option. Toggled from off to on with APPLY FILTERS pressed, the rendered rows and every
/// total were identical on that tenant, so what it governs could not be established; it is recorded
/// as observed and not modelled.</para>
/// </summary>
public sealed record SalesReturnRegisterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<SalesReturnRegisterDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesReturnRegisterView;
}

public sealed record SalesReturnRegisterRowDto(
    DateOnly Date,
    string DocumentCode,
    Guid ContactId,
    string ContactName,
    string? ContactPan,
    decimal TotalReturnValue,
    decimal TaxExemptReturnValue,
    decimal TaxableReturnValue,
    decimal VatAmount);

public sealed record SalesReturnRegisterDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<SalesReturnRegisterRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalReturnValue,
    decimal TotalTaxExemptReturnValue,
    decimal TotalTaxableReturnValue,
    decimal TotalVatAmount);
