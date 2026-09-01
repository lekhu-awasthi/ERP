using ErpApp.Application.Common.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.Jobs;

/// <summary>
/// Drives one <see cref="IQueuedJobProcessor"/> on a timer. Phase 21b factored this out of Phase
/// 21a's <c>ImportJobRunnerHostedService</c> when a second queue-driven job arrived; the file it
/// replaces is gone, and imports now run through <c>QueuedJobRunnerHostedService&lt;IImportJobProcessor,
/// ImportJobRunnerOptions&gt;</c> with no behaviour change.
///
/// <para><b>One hosted service per processor, not one shared loop over all of them.</b> A single
/// loop draining processors in sequence would let a 5,000-row import hold up an export (and the
/// reverse) for minutes, and head-of-line blocking between two unrelated features is a real user
/// -visible regression for no gain. A separate <c>BackgroundService</c> per processor costs one
/// registration line and gives each its own timer, its own poll interval and its own kill switch.
/// See docs/phase-21b-status.md, Decision C, for why this is a shared <i>timer host</i> and not the
/// generic job framework Phase 21a was right to decline.</para>
///
/// <para>The three ways a hosted service goes wrong are handled exactly as they were in Phase 20e
/// and 21a, and for the same reasons: a <b>scope per job</b>, because <c>IAppDbContext</c>,
/// <c>IFileStorage</c> and <c>IEmailSender</c> are all <c>AddScoped</c> and a singleton cannot hold
/// them; <b>IOptionsMonitor</b> so a changed poll interval takes effect without a restart; and a
/// tick whose exception is <b>logged and swallowed</b>, because an unhandled one in
/// <c>ExecuteAsync</c> silently stops the loop for the rest of the process's life while the app
/// keeps serving HTTP perfectly happily.</para>
///
/// <para>The tick drains rather than doing one job per interval: a user who queues three exports
/// should not wait three poll intervals for the second to start.</para>
/// </summary>
public sealed class QueuedJobRunnerHostedService<TProcessor, TOptions>(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TOptions> options,
    TimeProvider timeProvider,
    ILogger<QueuedJobRunnerHostedService<TProcessor, TOptions>> logger) : BackgroundService
    where TProcessor : class, IQueuedJobProcessor
    where TOptions : QueuedJobRunnerOptions
{
    private static readonly string RunnerName = typeof(TProcessor).Name;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            logger.LogInformation("{Runner} is disabled by configuration; not starting.", RunnerName);
            return;
        }

        var interval = Normalize(options.CurrentValue.PollInterval);
        logger.LogInformation("{Runner} started; polling every {PollInterval}.", RunnerName, interval);

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
                    "{Runner} poll interval changed from {Previous} to {PollInterval}.",
                    RunnerName, interval, current);
                interval = current;
                timer.Period = interval;
            }

            await TickAsync(stoppingToken);
        }

        logger.LogInformation("{Runner} stopped.", RunnerName);
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // A scope per job, not per tick: a single job can run for minutes, and its
                // DbContext must not be shared with the next one's.
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<TProcessor>();

                if (!await processor.ProcessNextAsync(stoppingToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. A job caught mid-run stays Running with a heartbeat that will go stale, and
            // the next process to start resumes or regenerates it.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Runner} tick failed; the loop continues.", RunnerName);
        }
    }

    /// <summary>Retention housekeeping, in its own scope and its own try/catch: a sweep that fails
    /// must never stop the tick from actually running jobs.</summary>
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TProcessor>().SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Runner} retention sweep failed; the tick continues.", RunnerName);
        }
    }

    /// <summary>A zero or negative interval from misconfiguration would make PeriodicTimer throw at
    /// construction and take the runner down for the process lifetime; clamping is the safer
    /// failure.</summary>
    private static TimeSpan Normalize(TimeSpan interval) =>
        interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
}
