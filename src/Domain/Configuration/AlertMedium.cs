namespace ErpApp.Domain.Configuration;

/// <summary>
/// Delivery channel for a scheduled <see cref="AlertDefinition"/> (erp-module-scan.md
/// Configurations §15, FR-11.1).
///
/// <para>Phase 20e found exactly one member live — the reference product's "Medium" dropdown was
/// opened and contained "Email" and nothing else — and predicted that an <see cref="Sms"/> member
/// would be "a one-line addition the day the reference product grows one", since
/// <c>ISmsSender</c> had already existed since Phase 18.</para>
///
/// <para><b>Phase 30 added it, and the one-line estimate was wrong.</b> It is recorded here because
/// the shape of the miss generalises. An alert's recipients are <c>AlertDefinition.Recipients</c>,
/// validated as email addresses; an SMS needs phone numbers, so validation had to switch on the
/// medium rather than being fixed. An email has a subject and an SMS does not, so
/// <c>AlertContent</c>'s two halves stopped being uniformly meaningful. And — the part nothing in
/// the estimate anticipated — Phase 18 charges 1 SMS credit per recipient through
/// <c>SmsCreditLedgerEntry</c>, so a scheduled SMS that did not debit would make the credit balance
/// a lie, and a scheduled SMS that cannot afford itself has to fail visibly in the send ledger
/// rather than throwing inside a timer tick. Four changes, not one. See docs/phase-30-status.md,
/// Decision G.</para>
/// </summary>
public enum AlertMedium
{
    Email,

    /// <summary>Phase 30. Recipients are phone numbers, not addresses; the send debits SMS credit
    /// exactly as an interactive send does, and an insufficient balance is recorded as a failed
    /// occurrence rather than an exception.</summary>
    Sms,
}
