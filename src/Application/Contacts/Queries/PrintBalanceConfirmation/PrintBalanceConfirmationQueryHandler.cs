using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.PrintBalanceConfirmation;

/// <summary>Builds the balance-confirmation letter. See the query's doc comment for why it carries
/// no permission key of its own and why the figure comes from <c>ContactLedgerReader</c>.</summary>
public sealed class PrintBalanceConfirmationQueryHandler(IAppDbContext db)
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
        var organization = await db.Organizations.SingleAsync(x => x.Id == request.OrganizationId, cancellationToken);

        var contact = await db.Contacts.SingleOrDefaultAsync(
                x => x.Id == request.ContactId && x.OrganizationId == request.OrganizationId && x.Type == request.ContactType,
                cancellationToken)
            ?? throw new NotFoundException($"{request.ContactType} not found.");

        var events = await ContactLedgerReader.LoadEventsAsync(
            db, request.OrganizationId, request.ContactType, request.ContactId, request.AsOfDate, cancellationToken);

        // Identical to ContactStatementQueryHandler's closing balance for the same as-of date: the
        // opening balance plus every event up to it. Not "the statement's ClosingBalance field",
        // because that would mean running a paginated report to read one number.
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
