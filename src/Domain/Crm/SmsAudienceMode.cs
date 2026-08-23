namespace ErpApp.Domain.Crm;

/// <summary>
/// docs/phase-18-status.md decision #6: implements product-requirements.md FR-4.8's literal three
/// modes ("all contacts, a Contact Group, or a custom selection") rather than replicating the live
/// Tigg screen's own Type-based mechanic (checkboxes for Customer/Supplier/Lead/Contact Persons,
/// narrowed via a per-contact override table) -- simpler, matches the written spec, and Contact
/// Persons as a distinct SMS-audience source is deferred (personnel don't carry their own SMS
/// history/credit tracking this phase).
/// </summary>
public enum SmsAudienceMode
{
    All,
    ContactGroup,
    Custom,
}
