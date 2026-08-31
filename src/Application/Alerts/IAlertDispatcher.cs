namespace ErpApp.Application.Alerts;

/// <summary>
/// The scheduler's entire decision-and-send logic, deliberately separated from the hosted service
/// that ticks it (Infrastructure). The hosted service owns the timer, the DI scope and the process
/// lifetime; this owns "which alerts are due right now, and what happens to each one".
///
/// <para>That split is what makes the phase testable without a single Task.Delay: every test drives
/// this interface with a FakeTimeProvider and an in-memory DbContext, and the hosted service --
/// which contains no business decision at all -- is never instantiated. See
/// docs/phase-20e-status.md's testing section.</para>
/// </summary>
public interface IAlertDispatcher
{
    /// <summary>Sends every alert occurrence that is due and not already claimed. Safe to call
    /// repeatedly and concurrently; returns the number of emails actually handed to the sender.</summary>
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}
