using ErpApp.Application.Common.Security;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Printing.Queries.PreviewPrintingTemplate;

/// <summary>
/// Phase 73 -- the layout editor's preview: settings not yet saved, drawn over a fixed sample document under the
/// organization's real header, as the vendor's editor previews fixed sample data (phase-73-status.md § Live read 3).
///
/// <para><b>Sample data, never a real document.</b> Previewing the latest invoice would render an uncounted copy of
/// a bill, which no route may do (phase 67), and would need that type's View key besides this one. The sample's
/// number is <c>SAMPLE</c>, so a preview that escapes cannot be mistaken for a bill.</para>
///
/// <para>A POST that writes nothing, because the settings travel in the body; gated by the template View key, since
/// it shows nothing a template reader cannot already see but the organization's own header.</para>
/// </summary>
public sealed record PreviewPrintingTemplateQuery(
    Guid OrganizationId,
    DocumentType? DocumentType,
    PrintLayout? Layout,
    IReadOnlyList<PrintElement>? HiddenElements,
    IReadOnlyList<Guid>? PrintedCustomFieldIds,
    string? FooterText,
    bool? CompactMargins)
    : IRequest<PrintableDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateView;
}
