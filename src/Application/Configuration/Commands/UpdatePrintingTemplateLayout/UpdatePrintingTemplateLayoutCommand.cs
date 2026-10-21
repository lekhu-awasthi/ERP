using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdatePrintingTemplateLayout;

/// <summary>
/// Phase 73 -- the layout editor's save: which fixed layout draws the type, what it hides, which custom fields it
/// prints, a footer line and compact margins, replaced wholesale. Its own command rather than more parameters on
/// <c>UpdatePrintingTemplateCommand</c>, whose callers send a name and a type and must not reset a layout they never
/// heard of (phase 60's trailing-optional-parameter lesson).
///
/// <para>The lists and the layout are nullable so a misspelt body is a 400 naming the field, never "hide nothing"
/// with a 200 (phase 72's required-field rule). The same <c>Manage</c> key as every other template write.</para>
/// </summary>
public sealed record UpdatePrintingTemplateLayoutCommand(
    Guid OrganizationId,
    Guid Id,
    PrintLayout? Layout,
    IReadOnlyList<PrintElement>? HiddenElements,
    IReadOnlyList<Guid>? PrintedCustomFieldIds,
    string? FooterText,
    bool? CompactMargins,
    bool? PrintSerialNumbers = null)
    : IRequest<PrintingTemplateLayoutResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateManage;
}

public sealed record PrintingTemplateLayoutResult(
    Guid Id,
    PrintLayout Layout,
    IReadOnlyList<PrintElement> HiddenElements,
    IReadOnlyList<Guid> PrintedCustomFieldIds,
    string? FooterText,
    bool CompactMargins,
    bool PrintSerialNumbers = false);
