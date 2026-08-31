using ErpApp.Application.Alerts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.Alerts;

/// <summary>
/// This codebase's first background job (roadmap Phase 20e, FR-11.1). It wakes on a timer and asks
/// <see cref="IAlertDispatcher"/> what is due; it contains no business decision of its own, which is
/// the point -- everything worth testing lives in the dispatcher and is driven by a FakeTimeProvider
/// rather than by wall-clock waiting.
///
/// <para><b>Decision A (docs/phase-20e-status.md): hand-rolled BackgroundService, not Hangfire /
/// Quartz / Coravel.</b> The usual reason to take a scheduler dependency is durable schedule state,
/// missed-window catch-up and multi-instance locking. All three are already solved here by the
/// AlertSendLog ledger and its unique index, which this phase needs regardless -- the schedule
/// itself is durable because it is tenant data in the AlertDefinitions table, not runner state. A
/// scheduler library would therefore have added a second schema, a dashboard endpoint to secure,
/// and a deployment story, in exchange for machinery this design does not use. Phase 21's
/// import/export jobs are on-demand and long-running with progress and cancellation, which is a
/// different problem this deliberately does not pre-build (see the status doc's Phase 21 handoff).</para>
///
/// <para><b>Three things here are the standard ways a first hosted service goes wrong:</b>
/// <list type="number">
/// <item><b>A scope per tick.</b> IAppDbContext and IEmailSender are registered AddScoped; a
/// singleton BackgroundService cannot inject either, and capturing one would pin a DbContext for
/// the process lifetime. Hence IServiceScopeFactory and a fresh scope inside the loop.</item>
/// <item><b>IOptionsMonitor, not IOptions.</b> IOptions caches its bound value at first resolution
/// and a long-lived singleton never sees a later user-secrets change -- exactly the trap
/// phase-20g hit. The poll interval is read from the monitor on each iteration, so shortening it
/// for a manual E2E run takes effect on the next tick.</item>
/// <item><b>A tick never kills the loop.</b> Any exception from the dispatcher is logged and
/// swallowed; an unhandled exception in ExecuteAsync would silently stop the scheduler for the
/// process's remaining lifetime with the app still serving HTTP perfectly happily.</item>
/// </list></para>
/// </summary>
public sealed class AlertSchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AlertSchedulerOptions> options,
    TimeProvider timeProvider,
    ILogger<AlertSchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            logger.LogInformation("Alert scheduler is disabled by configuration; not starting.");
            return;
        }

        var interval = Normalize(options.CurrentValue.PollInterval);
        logger.LogInformation("Alert scheduler started; polling every {PollInterval}.", interval);

        // PeriodicTimer is created from TimeProvider rather than new PeriodicTimer(...) so the loop
        // itself is fake-clock-drivable too, should a future test ever want to exercise it.
        using var timer = new PeriodicTimer(interval, timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Re-read every iteration, which is the entire reason this takes IOptionsMonitor rather
            // than IOptions: a user-secrets/appsettings change to the interval (how a manual E2E run
            // shortens it) is picked up on the next tick instead of needing a process restart.
            var current = Normalize(options.CurrentValue.PollInterval);
            if (current != interval)
            {
                logger.LogInformation(
                    "Alert scheduler poll interval changed from {Previous} to {PollInterval}.", interval, current);
                interval = current;
                timer.Period = interval;
            }

            await TickAsync(stoppingToken);
        }

        logger.LogInformation("Alert scheduler stopped.");
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IAlertDispatcher>();

            var sent = await dispatcher.DispatchDueAsync(stoppingToken);
            if (sent > 0)
            {
                logger.LogInformation("Alert scheduler dispatched {SentCount} alert email(s).", sent);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Alert scheduler tick failed; the loop continues.");
        }
    }

    /// <summary>A zero or negative interval from misconfiguration would make PeriodicTimer throw at
    /// construction and take the scheduler down for the process lifetime; clamping to one second is
    /// the safer failure.</summary>
    private static TimeSpan Normalize(TimeSpan interval) =>
        interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
}
