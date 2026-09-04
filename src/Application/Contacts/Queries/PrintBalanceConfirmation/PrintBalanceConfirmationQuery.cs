using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.PrintBalanceConfirmation;

/// <summary>
/// Phase 27b -- <c>CustomTemplate</c>'s second consumer (FR-11.3): the
/// <c>CustomerBalanceConfirmation</c> / <c>SupplierBalanceConfirmation</c> letter, rendered as a PDF
/// from the Contact statement. Phase 20d created those two template types and nothing has ever read
/// one; this is the screen that does.
///
/// <para><b>No new permission key.</b> The letter states one figure -- the contact's closing balance
/// as at a date -- next to that contact's own name and PAN, which is precisely what
/// <c>ContactStatementQuery</c> already shows, so it rides that query's key
/// (<c>CustomerStatementView</c> / <c>SupplierStatementView</c>) rather than inventing a parallel
/// one a role could be granted independently. The standing rule reads "anything exposing PAN or
/// contact identity is Admin-only", and both statement keys already are; a new key here could only
/// have widened access or duplicated an existing decision.</para>
///
/// <para><b>The balance comes from <c>ContactLedgerReader</c></b>, the same reader Contact
/// Statement, Contact Overview and Contact Balance Summary all read. So a confirmation letter and
/// the statement it is confirming agree <i>by construction</i>, which is the whole point of the
/// document -- phase-26b's shared-reader lesson applied where it matters most.</para>
/// </summary>
public sealed record PrintBalanceConfirmationQuery(
    Guid OrganizationId, ContactType ContactType, Guid ContactId, DateOnly AsOfDate)
    : IRequest<BalanceConfirmationDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        ContactType == ContactType.Customer ? PermissionKeys.CustomerStatementView : PermissionKeys.SupplierStatementView;
}

/// <summary>Pre-formatted for print, the same choice <c>PrintableDocumentDto</c> makes and for the
/// same reason: the calendar the caller asked for is resolved here, once.</summary>
public sealed record BalanceConfirmationDto(
    string OrganizationName,
    string? OrganizationAddress,
    string? OrganizationPhone,
    string? OrganizationEmail,
    string? OrganizationPan,
    string ContactCode,
    string ContactName,
    string? ContactAddress,
    string? ContactPan,
    ContactType ContactType,
    string AsOfDateText,
    decimal Balance,
    string BalanceType,
    string TemplateName,
    string Body,
    string? CalendarNote);
