using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named print layout per DocumentType (architecture-spec.md line ~29; erp-module-scan.md
/// Configurations §12; FR-11.2), one <see cref="IsDefault"/> per (OrganizationId, DocumentType).
///
/// <para><b>Phase 20d</b> shipped this metadata-only -- a Name and the default flag -- because the reference product's
/// screen is a visual template editor and building one was out of scope. Every document printed one shared layout.</para>
///
/// <para><b>Phase 73</b> made it real, from a live read of that editor (phase-73-status.md § Live read): the vendor's
/// layouts are fixed drawing code over <i>one</i> settings shape, so a template here is a <see cref="Layout"/> plus what it
/// hides (<see cref="HiddenElements"/>), which custom fields it prints, a footer line and a compact-margins switch. It
/// never relabels anything and never chooses a calendar (Decisions B and D): a tax document's statutory content is not
/// a template's to change (<see cref="PrintElements.Locked"/>), and its dates are BS with AD beside it (phase 71).</para>
///
/// <para>Every setting defaults to Standard, nothing hidden, no custom fields, no footer line and normal margins, so a
/// row written before the columns existed needs no backfill, and an organization with no row at all prints through
/// <see cref="PrintingTemplateSettings.Default"/>, the same values. "Nothing hidden" includes the three elements this
/// phase added to the print (a line's unit, the HS code where a line has one, the amount in words): Decision E.</para>
/// </summary>
public sealed class PrintingTemplate : ITenantLookupEntity
{
    /// <summary>The longest footer line a template may carry: one or two printed lines at the footer's size.</summary>
    public const int FooterTextMaxLength = 500;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public DocumentType DocumentType { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Phase 73 -- which fixed layout draws the document.</summary>
    public PrintLayout Layout { get; private set; }

    /// <summary>Phase 73 -- the elements this template hides; empty prints everything the type prints.</summary>
    public IReadOnlyList<PrintElement> HiddenElements { get; private set; } = new List<PrintElement>();

    /// <summary>Phase 73 -- the custom fields printed, in this order. Empty prints none, as before this phase; the
    /// vendor's editor is likewise one unticked checkbox per field.</summary>
    public IReadOnlyList<Guid> PrintedCustomFieldIds { get; private set; } = new List<Guid>();

    /// <summary>Phase 73 -- one free line printed at the foot of every page (bank details, a thank-you), or null.</summary>
    public string? FooterText { get; private set; }

    /// <summary>Phase 73 -- narrower margins and tighter spacing, so a short document fits half a page.</summary>
    public bool CompactMargins { get; private set; }

    private PrintingTemplate()
    {
    }

    /// <summary>isDefault is set by the caller (CreatePrintingTemplateCommandHandler), not decided
    /// here -- "is this the first template for this DocumentType" is a DB read, an Application-layer
    /// concern, same split CreateCostTerm/CreateCustomStatus use for their own uniqueness checks.</summary>
    public static PrintingTemplate Create(Guid organizationId, string name, DocumentType documentType, bool isDefault)
    {
        return new PrintingTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            DocumentType = documentType,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Moving a template to a different DocumentType while it's the current default would
    /// leave two groups' default invariant inconsistent (its old group loses a default, its new
    /// group might gain a second one) -- clearing IsDefault as a side effect keeps that invariant
    /// enforceable without a separate error path for what's a rare edit.
    ///
    /// <para>Phase 73 -- the same move keeps only the hidden elements the new type may still hide, and drops the
    /// custom fields: a field applies to a set of types, so the old type's choice says nothing about the new one, and an
    /// Invoice must never inherit a Quotation template's hidden PAN.</para></summary>
    public void Update(string name, DocumentType documentType, bool isActive)
    {
        if (DocumentType != documentType)
        {
            IsDefault = false;
            HiddenElements = HiddenElements.Where(x => PrintElements.CanHide(documentType, x)).ToList();
            PrintedCustomFieldIds = new List<Guid>();
        }

        Name = name;
        DocumentType = documentType;
        IsActive = isActive;
    }

    /// <summary>
    /// Phase 73 -- the layout editor's save, wholesale. Refuses an element this type does not print or may not hide
    /// (the validator says the same as a 400 first; this is the backstop for any other caller). Whether each custom field
    /// exists and applies to the type is a database read, so the handler checks it.
    /// </summary>
    public void UpdateLayout(
        PrintLayout layout,
        IEnumerable<PrintElement> hiddenElements,
        IEnumerable<Guid> printedCustomFieldIds,
        string? footerText,
        bool compactMargins)
    {
        if (!Enum.IsDefined(layout))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "Not a print layout.");
        }

        var hidden = hiddenElements.Distinct().OrderBy(x => x).ToList();
        foreach (var element in hidden)
        {
            if (!PrintElements.CanHide(DocumentType, element))
            {
                throw new InvalidOperationException(
                    PrintElements.For(DocumentType).Contains(element)
                        ? $"{element} is required on {DocumentType}, a tax document, and cannot be hidden."
                        : $"{DocumentType} does not print {element}.");
            }
        }

        var footer = string.IsNullOrWhiteSpace(footerText) ? null : footerText.Trim();
        if (footer is { Length: > FooterTextMaxLength })
        {
            throw new InvalidOperationException($"The footer line is longer than {FooterTextMaxLength} characters.");
        }

        Layout = layout;
        HiddenElements = hidden;
        PrintedCustomFieldIds = printedCustomFieldIds.Distinct().ToList();
        FooterText = footer;
        CompactMargins = compactMargins;
    }

    /// <summary>What the print handler reads.</summary>
    public PrintingTemplateSettings Settings =>
        new(Name, Layout, HiddenElements, PrintedCustomFieldIds, FooterText, CompactMargins);

    public void MarkAsDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;
}

/// <summary>Phase 73 -- a template's print settings, detached from the row, so an organization with no template and
/// the editor's unsaved preview print through the same values a saved template does.</summary>
public sealed record PrintingTemplateSettings(
    string Name,
    PrintLayout Layout,
    IReadOnlyList<PrintElement> HiddenElements,
    IReadOnlyList<Guid> PrintedCustomFieldIds,
    string? FooterText,
    bool CompactMargins)
{
    /// <summary>No template at all: what every document printed before phase 73.</summary>
    public static PrintingTemplateSettings Default { get; } = new("Default", PrintLayout.Standard, [], [], null, false);

    public bool Shows(PrintElement element) => !HiddenElements.Contains(element);
}
