using ErpApp.Application.Common.Jobs;

namespace ErpApp.Application.Communications;

/// <summary>
/// The decider half of Phase 30's background sender, driven by
/// <c>QueuedJobRunnerHostedService&lt;IEmailSendJobProcessor, EmailSendRunnerOptions&gt;</c>.
///
/// <para>Its own hosted service rather than a share of the import or export one, per phase-21b
/// Decision C's reasoning applied a fourth time: one loop draining every processor in sequence
/// would let a 5,000-row import hold an invoice email for minutes, and head-of-line blocking
/// between unrelated features is a real regression for no gain. A registration line buys this queue
/// its own timer, its own poll interval and its own kill switch.</para>
/// </summary>
public interface IEmailSendJobProcessor : IQueuedJobProcessor;
