using System.Globalization;
using System.Text;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Alerts;

/// <summary>
/// "CRM Report" (erp-module-scan.md Configurations §15) -- the day's pipeline and follow-up
/// activity. Same bounded-aggregates rule as DailyTransactionSummaryContentBuilder: counts and one
/// revenue total, never a deal title, contact name, or assignee.
///
/// <para>Private Deals and private WorkTasks (<c>IsPrivate</c>) are counted, not listed, for the
/// same reason -- a count cannot disclose which record was private. That is why this builder does
/// not need to know who, if anyone, the alert "runs as".</para>
///
/// <para>Deals are matched on <c>CreatedAt</c>'s Nepal-local date rather than a business Date field,
/// because a Deal has no business date -- it has ExpectedClosingDate and ClosingDate, which answer
/// different questions. WorkTasks are matched on DueDate, which is the field a daily follow-up
/// report is actually about.</para>
/// </summary>
public sealed class CrmReportContentBuilder(IAppDbContext db) : IAlertContentBuilder
{
    public AlertType AlertType => AlertType.CrmReport;

    public async Task<AlertContent> BuildAsync(
        Guid organizationId, DateOnly occurrenceDate, CancellationToken cancellationToken)
    {
        var organizationName = await db.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Your organization";

        // Deal.CreatedAt is a DateTimeOffset; the local-day window is computed here as two UTC
        // instants rather than translated per row, so the comparison stays translatable to SQL.
        var (dayStartUtc, dayEndUtc) = LocalDayBoundsUtc(occurrenceDate);

        var dealsCreated = await db.Deals.CountAsync(
            d => d.OrganizationId == organizationId && d.CreatedAt >= dayStartUtc && d.CreatedAt < dayEndUtc,
            cancellationToken);

        var dealsWon = await db.Deals.CountAsync(
            d => d.OrganizationId == organizationId && d.Status == DealStatus.Won && d.ClosingDate == occurrenceDate,
            cancellationToken);

        var dealsLost = await db.Deals.CountAsync(
            d => d.OrganizationId == organizationId && d.Status == DealStatus.Lost && d.ClosingDate == occurrenceDate,
            cancellationToken);

        var wonRevenue = await db.Deals
            .Where(d => d.OrganizationId == organizationId && d.Status == DealStatus.Won && d.ClosingDate == occurrenceDate)
            .SumAsync(d => (decimal?)d.ExpectedRevenue, cancellationToken) ?? 0m;

        var openPipeline = await db.Deals.CountAsync(
            d => d.OrganizationId == organizationId && d.Status == DealStatus.Pending, cancellationToken);

        var tasksDue = await db.Tasks.CountAsync(
            t => t.OrganizationId == organizationId && t.DueDate == occurrenceDate, cancellationToken);

        var tasksOverdue = await db.Tasks.CountAsync(
            t => t.OrganizationId == organizationId
                 && t.DueDate != null && t.DueDate < occurrenceDate
                 && t.Status != WorkTaskStatus.Done,
            cancellationToken);

        var smsSent = await db.SmsLogs.CountAsync(
            s => s.OrganizationId == organizationId && s.SentAt >= dayStartUtc && s.SentAt < dayEndUtc,
            cancellationToken);

        var rows = new (string Label, string Value)[]
        {
            ("Deals created", dealsCreated.ToString(CultureInfo.InvariantCulture)),
            ("Deals won", dealsWon.ToString(CultureInfo.InvariantCulture)),
            ("Deals lost", dealsLost.ToString(CultureInfo.InvariantCulture)),
            ("Won deal value", $"NPR {wonRevenue:N2}"),
            ("Open pipeline (all dates)", openPipeline.ToString(CultureInfo.InvariantCulture)),
            ("Tasks due today", tasksDue.ToString(CultureInfo.InvariantCulture)),
            ("Tasks overdue and not done", tasksOverdue.ToString(CultureInfo.InvariantCulture)),
            ("SMS messages sent", smsSent.ToString(CultureInfo.InvariantCulture)),
        };

        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"CRM Report for {organizationName}");
        body.AppendLine(CultureInfo.InvariantCulture, $"Business day: {occurrenceDate:yyyy-MM-dd} (Nepal time)");
        body.AppendLine();

        foreach (var (label, value) in rows)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"{label,-28} {value,20}");
        }

        body.AppendLine();
        body.AppendLine("You are receiving this because an administrator scheduled it in Configurations > Alert Scheduler.");

        return new AlertContent(
            $"CRM Report - {organizationName} - {occurrenceDate:yyyy-MM-dd}",
            body.ToString());
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) LocalDayBoundsUtc(DateOnly localDate)
    {
        var start = new DateTimeOffset(localDate.ToDateTime(TimeOnly.MinValue), Domain.Common.NepalTime.Offset);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
