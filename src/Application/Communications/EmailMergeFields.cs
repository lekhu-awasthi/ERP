using ErpApp.Domain.Configuration;

namespace ErpApp.Application.Communications;

/// <summary>One offerable merge field: the token a body writes, and how it is presented.</summary>
/// <param name="Token">Bare token name — the body writes <c>$[TOKEN]$</c>.</param>
public sealed record EmailMergeField(string Group, string Label, string Token);

/// <summary>
/// The merge-field catalogue behind the Send Email template editor's "Custom Tags" menu, read live
/// off the reference product on 2026-09-05 (docs/phase-30-status.md, "The merge-field catalogue").
///
/// <para><b>Syntax is <c>$[TOKEN]$</c></b> — confirmed verbatim live, and the same convention
/// <c>SmsTemplate</c> established in Phase 18. Substitution is plain string replacement, applied
/// once when the dialog opens.</para>
///
/// <para><b>Four groups: three fixed, one per context.</b> Organization, Contact and User are
/// offered everywhere. The fourth is the document's own group and is empty for
/// <see cref="EmailTemplateContext.General"/> and
/// <see cref="EmailTemplateContext.BalanceConfirmation"/>, which are about a Contact rather than a
/// document.</para>
///
/// <para><b>Document tokens are generic here and aliased per context.</b> Live, an Invoice
/// template's group offers <c>INVOICE_NO</c>/<c>INVOICE_DATE</c> — but only some of the group is
/// prefixed (<c>DUE_DATE</c>, <c>CURRENCY</c>, <c>GRAND_TOTAL</c> and <c>CUSTOMER_NAME</c> are
/// not), and the pass could only read the Invoice group's labels, never a Quotation's or a Purchase
/// Order's. Guessing a prefix scheme from one sample is exactly the inference Phase 27b's Send
/// Email list got wrong. So the catalogue this product <i>offers</i> is unprefixed and uniform
/// (<c>DOCUMENT_NO</c>, <c>DOCUMENT_DATE</c>, …), and <see cref="EmailMergeResolver"/>
/// <i>additionally</i> resolves the live per-context aliases, so a body pasted from the reference
/// product still renders. Offering one spelling and accepting two is the safe asymmetry.</para>
/// </summary>
public static class EmailMergeFields
{
    public const string OrganizationGroup = "Organization";
    public const string ContactGroup = "Contact";
    public const string UserGroup = "User";

    private static readonly IReadOnlyList<EmailMergeField> Organization =
    [
        new(OrganizationGroup, "Name", "ORGANIZATION_NAME"),
        new(OrganizationGroup, "Display Name", "ORGANIZATION_DISPLAY_NAME"),
        new(OrganizationGroup, "Address", "ORGANIZATION_ADDRESS"),
        new(OrganizationGroup, "Phone", "ORGANIZATION_PHONE"),
        new(OrganizationGroup, "Email", "ORGANIZATION_EMAIL"),
        new(OrganizationGroup, "Website", "ORGANIZATION_WEBSITE"),
        new(OrganizationGroup, "Pan", "ORGANIZATION_PAN"),
    ];

    private static readonly IReadOnlyList<EmailMergeField> Contact =
    [
        new(ContactGroup, "Name", "CONTACT_NAME"),
        new(ContactGroup, "Address", "CONTACT_ADDRESS"),
        new(ContactGroup, "Phone", "CONTACT_PHONE"),
        new(ContactGroup, "Email", "CONTACT_EMAIL"),
        new(ContactGroup, "Pan", "CONTACT_PAN"),
    ];

    private static readonly IReadOnlyList<EmailMergeField> User =
    [
        new(UserGroup, "Name", "USER_NAME"),
        new(UserGroup, "Phone No", "USER_PHONE_NO"),
        new(UserGroup, "Email", "USER_EMAIL"),
        new(UserGroup, "Address", "USER_ADDRESS"),
    ];

    /// <summary>The document group's fields, offered under the context's own display name. Mirrors
    /// the live Invoice group one-for-one, minus its three payment-allocation fields (Payment Mode /
    /// Payment Reference / Payment Amount), which belong to a Payment context and are added
    /// there.</summary>
    private static IReadOnlyList<EmailMergeField> DocumentFields(string group) =>
    [
        new(group, "Customer Name", "CUSTOMER_NAME"),
        new(group, "Reference", "DOCUMENT_REFERENCE"),
        new(group, "Number", "DOCUMENT_NO"),
        new(group, "Date", "DOCUMENT_DATE"),
        new(group, "Transaction Date", "TRANSACTION_DATE"),
        new(group, "Due Date", "DUE_DATE"),
        new(group, "Currency", "CURRENCY"),
        new(group, "Exchange Rate", "EXCHANGE_RATE"),
        new(group, "Sub Total", "SUB_TOTAL"),
        new(group, "Transaction Discount", "TRANSACTION_DISCOUNT"),
        new(group, "Non-Taxable Total", "NON_TAXABLE_TOTAL"),
        new(group, "Taxable Total", "TAXABLE_TOTAL"),
        new(group, "VAT", "VAT"),
        new(group, "Grand Total", "GRAND_TOTAL"),
        new(group, "Note", "DOCUMENT_NOTE"),
    ];

    private static IReadOnlyList<EmailMergeField> PaymentFields(string group) =>
    [
        new(group, "Payment Mode", "PAYMENT_MODE"),
        new(group, "Payment Reference", "PAYMENT_REFERENCE"),
        new(group, "Payment Amount", "PAYMENT_AMOUNT"),
    ];

    /// <summary>Human-readable name of a context's document group, and the prefix its live token
    /// aliases use.</summary>
    public static string GroupNameFor(EmailTemplateContext context) => context switch
    {
        EmailTemplateContext.Quotation => "Quotation",
        EmailTemplateContext.SalesOrder => "Sales Order",
        EmailTemplateContext.Invoice => "Invoice",
        EmailTemplateContext.CreditNote => "Credit Note",
        EmailTemplateContext.CustomerPayment => "Customer Payment",
        EmailTemplateContext.SupplierPayment => "Supplier Payment",
        EmailTemplateContext.PurchaseOrder => "Purchase Order",
        EmailTemplateContext.BalanceConfirmation => "Balance Confirmation",
        _ => "General",
    };

    /// <summary>
    /// Every field offerable in this context, in menu order: Organization, Contact, User, then the
    /// document group where there is one.
    /// </summary>
    public static IReadOnlyList<EmailMergeField> For(EmailTemplateContext context)
    {
        var fields = new List<EmailMergeField>(Organization.Count + Contact.Count + User.Count + 18);
        fields.AddRange(Organization);
        fields.AddRange(Contact);
        fields.AddRange(User);

        if (EmailTemplateContexts.DocumentTypeFor(context) is null)
        {
            return fields;
        }

        var group = GroupNameFor(context);
        fields.AddRange(DocumentFields(group));

        if (context is EmailTemplateContext.CustomerPayment or EmailTemplateContext.SupplierPayment)
        {
            fields.AddRange(PaymentFields(group));
        }

        return fields;
    }

    /// <summary>
    /// The live per-context aliases this product <i>accepts</i> but does not offer — see the
    /// type-level remarks. <c>INVOICE_NO</c> resolves exactly as <c>DOCUMENT_NO</c> on an Invoice
    /// template, and not at all on any other context, which is also true of the reference product.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AliasesFor(EmailTemplateContext context)
    {
        var prefix = GroupNameFor(context).Replace(" ", "_").ToUpperInvariant();

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{prefix}_NO"] = "DOCUMENT_NO",
            [$"{prefix}_DATE"] = "DOCUMENT_DATE",
            [$"{prefix}_REFERENCE"] = "DOCUMENT_REFERENCE",
            [$"{prefix}_NOTE"] = "DOCUMENT_NOTE",
        };
    }
}
