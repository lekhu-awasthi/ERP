using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PurchaseReturnRegister;

/// <summary>
/// The Tax Report group's <b>Purchase Return Register</b> (phase 26c, slug
/// <c>purchase-return-register</c>) -- the Nepal IRD statutory purchase-return book. Generated live
/// on 2026-09-03; its Devanagari header set, translated:
/// बीजक / प्रज्ञापनपत्र नम्बर (मिति, बीजक नं., प्रज्ञापनपत्र नं., आपूर्तिकर्ताको नाम,
/// आपूर्तिकर्ताको स्थायी लेखा नम्बर), जम्मा फिर्ता मूल्य,
/// कर छुट हुने वस्तु वा सेवाको फिर्ता / पैठारी मूल्य, करयोग्य फिर्ता (पूंजीगत बाहेक) मूल्य+कर,
/// करयोग्य पैठारी फिर्ता (पूंजीगत बाहेक) मूल्य+कर, and पूंजीगत करयोग्य फिर्ता / पैठारी मूल्य+कर.
/// One row per approved Debit Note, values <b>positive</b>, with a footer Total.
///
/// <para><b>It is not the Sales Return Register's mirror, which is what the roadmap predicted.</b>
/// It is the <i>Purchase</i> Register's mirror: seven money columns carrying that register's own
/// Capital-versus-Others and Local-versus-Import split, against the sales side's four. So the pair
/// is two handlers rather than 26b's one-handler-two-sides pattern -- there is no shared shape to
/// discriminate.</para>
///
/// <para><b>The main Purchase Register keeps its debit-note rows.</b> Confirmed live, back to back
/// over one period: the same notes appear in both, negative there and positive here, and the main
/// register's Total is net of them. Phase 19's folding was correct parity, not the simplification
/// the 2026-09-02 catalogue pass inferred it to be -- so nothing about the main registers changed
/// this phase. Both reports read <c>PurchaseReturnReader</c>, which is what keeps the magnitudes
/// identical.</para>
/// </summary>
public sealed record PurchaseReturnRegisterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PurchaseReturnRegisterDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseReturnRegisterView;
}

/// <summary>
/// <paramref name="ImportDeclarationNo"/> is always null today: a Debit Note carries no import
/// declaration of its own and the live column was empty ("-") on every row of the generated report.
/// It ships rather than being dropped so the statutory shape is complete -- the same call phase 19
/// made for the Sales Register's export columns before phase 23 filled them.
/// </summary>
public sealed record PurchaseReturnRegisterRowDto(
    DateOnly Date,
    string DocumentCode,
    string? ImportDeclarationNo,
    Guid ContactId,
    string ContactName,
    string? ContactPan,
    decimal TotalReturnValue,
    decimal TaxExemptValue,
    decimal TaxableNonCapitalLocalValue,
    decimal TaxableNonCapitalLocalVat,
    decimal TaxableNonCapitalImportValue,
    decimal TaxableNonCapitalImportVat,
    decimal TaxableCapitalValue,
    decimal TaxableCapitalVat);

public sealed record PurchaseReturnRegisterDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<PurchaseReturnRegisterRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalReturnValue,
    decimal TotalTaxExemptValue,
    decimal TotalTaxableNonCapitalLocalValue,
    decimal TotalTaxableNonCapitalLocalVat,
    decimal TotalTaxableNonCapitalImportValue,
    decimal TotalTaxableNonCapitalImportVat,
    decimal TotalTaxableCapitalValue,
    decimal TotalTaxableCapitalVat);
