using ErpApp.Application.Contacts.Queries.PrintBalanceConfirmation;
using ErpApp.Domain.Contacts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ErpApp.Api.Printing;

/// <summary>
/// Phase 27b -- the balance-confirmation letter (FR-11.3). A letter, not a document: no line table,
/// no totals block, so it does not reuse <see cref="DocumentPdfRenderer"/>'s section frame. What it
/// does share is the organization header and the calendar-disclosure footer, because a reader must
/// be able to tell these two PDFs came from the same system.
///
/// <para>The signature block at the foot is the point of the whole document -- a confirmation the
/// recipient signs and returns -- so it is structural here rather than decorative.</para>
/// </summary>
public static class BalanceConfirmationPdfRenderer
{
    public static byte[] Render(BalanceConfirmationDto dto) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text(dto.OrganizationName).Bold().FontSize(16);

                    if (!string.IsNullOrWhiteSpace(dto.OrganizationAddress))
                    {
                        column.Item().Text(dto.OrganizationAddress).FontSize(9);
                    }

                    var contactLine = string.Join(
                        "  |  ",
                        new[]
                        {
                            dto.OrganizationPhone is { } phone ? $"Phone: {phone}" : null,
                            dto.OrganizationEmail is { } email ? $"Email: {email}" : null,
                            dto.OrganizationPan is { } pan ? $"PAN: {pan}" : null,
                        }.Where(x => x is not null));

                    if (!string.IsNullOrWhiteSpace(contactLine))
                    {
                        column.Item().Text(contactLine).FontSize(9);
                    }
                });

                page.Content().PaddingVertical(15).Column(column =>
                {
                    column.Spacing(14);

                    column.Item().AlignCenter().Text(Title(dto.ContactType)).Bold().FontSize(14);

                    column.Item().Column(party =>
                    {
                        party.Item().Text("To").Bold().FontSize(9);
                        party.Item().Text($"{dto.ContactCode} — {dto.ContactName}");

                        if (!string.IsNullOrWhiteSpace(dto.ContactAddress))
                        {
                            party.Item().Text(dto.ContactAddress).FontSize(9);
                        }

                        if (!string.IsNullOrWhiteSpace(dto.ContactPan))
                        {
                            party.Item().Text($"PAN: {dto.ContactPan}").FontSize(9);
                        }
                    });

                    column.Item().Text($"As at: {dto.AsOfDateText}").FontSize(9);

                    // The template body, merge fields already substituted by the handler.
                    column.Item().Text(dto.Body).LineHeight(1.4f);

                    column.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Text("Balance as per our books").Bold();
                        row.ConstantItem(140).AlignRight().Text($"{dto.Balance:#,##0.00} {dto.BalanceType}").Bold();
                    });

                    column.Item().PaddingTop(40).Row(row =>
                    {
                        row.RelativeItem().Column(ours =>
                        {
                            ours.Item().Text("_______________________").FontSize(9);
                            ours.Item().Text($"For {dto.OrganizationName}").FontSize(8).Light();
                        });

                        row.RelativeItem().AlignRight().Column(theirs =>
                        {
                            theirs.Item().AlignRight().Text("_______________________").FontSize(9);
                            theirs.Item().AlignRight().Text($"Confirmed by {dto.ContactName}").FontSize(8).Light();
                        });
                    });
                });

                page.Footer().AlignCenter().Column(footer =>
                {
                    if (dto.CalendarNote is { } calendarNote)
                    {
                        footer.Item().AlignCenter().Text(calendarNote).FontSize(8);
                    }

                    footer.Item().AlignCenter().Text($"Template: {dto.TemplateName}").FontSize(8);
                });
            });
        }).GeneratePdf();

    private static string Title(ContactType contactType) =>
        contactType == ContactType.Customer ? "CUSTOMER BALANCE CONFIRMATION" : "SUPPLIER BALANCE CONFIRMATION";
}
