namespace ErpApp.Domain.Configuration;

/// <summary>
/// Delivery channel for a scheduled <see cref="AlertDefinition"/> (erp-module-scan.md
/// Configurations §15, FR-11.1). Exactly one member, and that is a live-confirmed fact rather
/// than a placeholder: the reference product's "Medium" dropdown was opened during Phase 20e's
/// confirm-live pass and contains "Email" and nothing else. Kept as an enum rather than dropped
/// because the screen genuinely renders a dropdown -- and because this codebase already has a
/// working ISmsSender (Phase 18), so an Sms member is a one-line addition the day the reference
/// product grows one. See docs/phase-20e-status.md.
/// </summary>
public enum AlertMedium
{
    Email,
}
