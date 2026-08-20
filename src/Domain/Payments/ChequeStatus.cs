namespace ErpApp.Domain.Payments;

/// <summary>
/// Phase 17 -- live-confirmed against the Tigg reference product's "Edit Received Cheque" Status
/// dropdown: a flat 5-state field, not the roadmap's guessed linear
/// Issued/Received -&gt; Presented -&gt; Cleared/Bounced pipeline. See Cheque.TransitionStatus for the
/// allowed-transition table this codebase enforces on top of it (docs/phase-17-status.md decision
/// #4/#5).
/// </summary>
public enum ChequeStatus
{
    Pending,
    Deposited,
    Cleared,
    Bounced,
    Cancelled,
}
