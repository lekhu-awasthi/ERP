using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class AlertSendLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The row is a claim ticket first and a history row second, so it must start Pending
    /// with no completion timestamp -- a row that started life as Sent would make a crash between
    /// the insert and SMTP indistinguishable from a successful send.</summary>
    [Fact]
    public void Claim_starts_pending_with_no_completion_time()
    {
        var log = Build();

        Assert.Equal(AlertSendStatus.Pending, log.Status);
        Assert.Null(log.CompletedAt);
        Assert.Null(log.FailureReason);
        Assert.Equal(Now, log.CreatedAt);
    }

    [Fact]
    public void MarkSent_completes_the_row_and_clears_any_failure_reason()
    {
        var log = Build();
        log.MarkFailed(Now, "transient");

        log.MarkSent(Now.AddSeconds(2));

        Assert.Equal(AlertSendStatus.Sent, log.Status);
        Assert.Null(log.FailureReason);
        Assert.Equal(Now.AddSeconds(2), log.CompletedAt);
    }

    [Fact]
    public void MarkFailed_records_the_reason()
    {
        var log = Build();

        log.MarkFailed(Now.AddSeconds(2), "SMTP said no.");

        Assert.Equal(AlertSendStatus.Failed, log.Status);
        Assert.Equal("SMTP said no.", log.FailureReason);
        Assert.Equal(Now.AddSeconds(2), log.CompletedAt);
    }

    /// <summary>An SMTP exception message is arbitrary third-party text; losing its tail must never
    /// fail the SaveChanges that records the failure (the column is nvarchar(1000)).</summary>
    [Fact]
    public void MarkFailed_truncates_an_over_long_reason_rather_than_throwing()
    {
        var log = Build();

        log.MarkFailed(Now, new string('x', 5000));

        Assert.Equal(1000, log.FailureReason!.Length);
    }

    private static AlertSendLog Build() =>
        AlertSendLog.Claim(
            Guid.NewGuid(), Guid.NewGuid(), AlertType.DailyTransactionSummary, AlertMedium.Email,
            new DateOnly(2026, 6, 15), "ops@example.test", "Daily Transaction Summary", Now);
}
