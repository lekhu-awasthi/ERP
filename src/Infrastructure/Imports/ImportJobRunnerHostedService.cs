using ErpApp.Application.Imports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.Imports;

/// <summary>
/// This codebase's second background job (roadmap Phase 21a, FR-2.9 / NFR-4.3), and a deliberate
/// copy of <c>AlertSchedulerHostedService</c>'s shape rather than an extension of it.
///
/// <para><b>Why a second service instead of one shared runner (Decision A).</b> The two jobs share a
/// <i>shape</i> and nothing else. Alerts are schedule-driven, idempotent, and answer "what is due
/// right now"; imports are queue-driven, not idempotent, long-running, cancellable, and answer
/// "what has someone asked for". Merging them would mean a generic job framework with a handler
/// registry -- built for exactly two consumers whose only common code would be the six lines of
/// timer and scope management duplicated below. Phase 20e's scope-control lesson applies twice
/// here: build the narrow thing behind one clean seam (<see cref="IImportJobProcessor"/>), and let
/// 21b/21c decide whether they join this table or get their own once they exist and can be
/// looked at.</para>
///
/// <para>The three ways a hosted service goes wrong are handled exactly as they were in 20e, and for
/// exactly the same reasons: a <b>scope per tick</b> because IAppDbContext, IFileStorage and
/// IEmailSender are all AddScoped and a singleton cannot hold them; <b>IOptionsMonitor</b> so a
/// changed poll interval takes effect without a restart; and a tick whose exception is <b>logged and
/// swallowed</b>, because an unhandled one in ExecuteAsync silently stops the loop for the rest of
/// the process's life while the app keeps serving HTTP perfectly happily.</para>
///
/// <para>One thing here that 20e did not need: the tick drains. A user who queues three imports
/// should not wait three poll intervals for the second to start, so the tick keeps calling the
/// processor until it reports nothing left.</para>
/// </summary>
public sealed class ImportJobRunnerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ImportJobRunnerOptions> options,
    TimeProvider timeProvider,
    ILogger<ImportJobRunnerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            logger.LogInformation("Import job runner is disabled by configuration; not starting.");
            return;
        }

        var interval = Normalize(options.CurrentValue.PollInterval);
        logger.LogInformation("Import job runner started; polling every {PollInterval}.", interval);

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

            var current = Normalize(options.CurrentValue.PollInterval);
            if (current != interval)
            {
                logger.LogInformation(
                    "Import job runner poll interval changed from {Previous} to {PollInterval}.", interval, current);
                interval = current;
                timer.Period = interval;
            }

            await TickAsync(stoppingToken);
        }

        logger.LogInformation("Import job runner stopped.");
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // A scope per job, not per tick: a single import can run for minutes, and its
                // DbContext must not be shared with the next one's.
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IImportJobProcessor>();

                if (!await processor.ProcessNextAsync(stoppingToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. A job caught mid-run stays Running with a heartbeat that will go stale, and
            // the next process to start resumes it from the first unclaimed row.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import job runner tick failed; the loop continues.");
        }
    }

    /// <summary>A zero or negative interval from misconfiguration would make PeriodicTimer throw at
    /// construction and take the runner down for the process lifetime; clamping is the safer
    /// failure.</summary>
    private static TimeSpan Normalize(TimeSpan interval) =>
        interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
}
