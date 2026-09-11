using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.PrintBalanceConfirmation;

/// <summary>Builds the balance-confirmation letter. See the query's doc comment for why it carries
/// no permission key of its own and why the figure comes from <c>ContactLedgerReader</c>.</summary>
public sealed class PrintBalanceConfirmationQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PrintBalanceConfirmationQuery, BalanceConfirmationDto>
{
    /// <summary>
    /// The letter a tenant gets before it has written a template of its own. Phase 18's SMS
    /// Templates established the <c>$[placeholder]$</c> convention this reuses; the placeholders are
    /// substituted below, so a tenant editing this text keeps working merge fields.
    /// </summary>
    private const string DefaultBody =
        "Dear $[ContactName]$,\n\n" +
        "As part of our periodic reconciliation, we request your confirmation of the balance shown " +
        "in our books as at $[AsOfDate]$.\n\n" +
        "Our records show a balance of $[Balance]$ $[BalanceType]$.\n\n" +
        "Please confirm whether this agrees with your records. If it does not, kindly send us the " +
        "details of the difference so that we can reconcile them.\n\n" +
        "Yours faithfully,\n$[OrganizationName]$";

    public async Task<BalanceConfirmationDto> Handle(
        PrintBalanceConfirmationQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var organization = await db.Organizations.SingleAsync(x => x.Id == request.OrganizationId, cancellationToken);

        var contact = await db.Contacts.SingleOrDefaultAsync(
                x => x.Id == request.ContactId && x.OrganizationId == request.OrganizationId && x.Type == request.ContactType,
                cancellationToken)
            ?? throw new NotFoundException($"{request.ContactType} not found.");

        var events = await ContactLedgerReader.LoadEventsAsync(
            db, request.OrganizationId, request.ContactType, request.ContactId, request.AsOfDate, cancellationToken,
            request.LocationId, reportLocations);

        // Identical to ContactStatementQueryHandler's closing balance for the same as-of date: the
        // opening balance plus every event up to it. Not "the statement's ClosingBalance field",
        // because that would mean running a paginated report to read one number.
        // Phase 35b -- Contact.OpeningBalance is a contact-level migration figure with no billing
        // location of its own (it predates every document), so a location-filtered balance is
        // "opening carry-forward plus this branch's movements", not "this branch's share of the
        // opening". Nothing in the schema could make it the latter, and splitting one number across
        // branches by guesswork would be worse than saying so. Named here rather than left for a
        // reader to notice a total that does not add up across branches.
        var signedBalance = contact.OpeningBalance + events.Sum(x => x.SignedAmount);
        var balanceType = ContactLedgerReader.BalanceType(request.ContactType, signedBalance);

        var templateType = request.ContactType == ContactType.Customer
            ? CustomTemplateType.CustomerBalanceConfirmation
            : CustomTemplateType.SupplierBalanceConfirmation;

        // Default first, then any active default template of the right type. A tenant that has not
        // configured one still gets a usable letter rather than a blank page -- the same "the
        // feature works before you configure it" call PrintDocumentQueryHandler makes for the
        // PrintingTemplate name.
        var template = await db.CustomTemplates
            .Where(x => x.OrganizationId == request.OrganizationId && x.Type == templateType && x.IsDefault && x.IsActive)
            .Select(x => new { x.Name, x.Body })
            .SingleOrDefaultAsync(cancellationToken);

        var asOfDateText = RequestCalendar.Format(request.AsOfDate);
        var balance = Math.Abs(signedBalance);

        var body = (template?.Body ?? DefaultBody)
            .Replace("$[ContactName]$", contact.Name, StringComparison.Ordinal)
            .Replace("$[ContactCode]$", contact.Code, StringComparison.Ordinal)
            .Replace("$[AsOfDate]$", asOfDateText, StringComparison.Ordinal)
            .Replace("$[Balance]$", balance.ToString("#,##0.00"), StringComparison.Ordinal)
            .Replace("$[BalanceType]$", balanceType, StringComparison.Ordinal)
            .Replace("$[OrganizationName]$", organization.Name, StringComparison.Ordinal);

        return new BalanceConfirmationDto(
            organization.Name,
            organization.Address,
            organization.Phone,
            organization.Email,
            organization.PanNumber,
            contact.Code,
            contact.Name,
            contact.Address,
            contact.Pan,
            request.ContactType,
            asOfDateText,
            balance,
            balanceType,
            template?.Name ?? "Default",
            body,
            RequestCalendar.DisclosureLine);
    }
}
