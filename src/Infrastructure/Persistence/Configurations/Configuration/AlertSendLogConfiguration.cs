using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class AlertSendLogConfiguration : IEntityTypeConfiguration<AlertSendLog>
{
    public void Configure(EntityTypeBuilder<AlertSendLog> builder)
    {
        builder.ToTable("AlertSendLogs", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.AlertDefinitionId).IsRequired();
        builder.Property(x => x.AlertType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.OccurrenceDate).IsRequired();
        builder.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();

        // THE load-bearing constraint of this phase. It is what makes a send exactly-once-per-
        // occurrence rather than once-per-tick: the dispatcher inserts here before calling SMTP, so
        // a restart cannot resend and two app instances cannot both send (the loser's insert
        // violates this index and it skips). Removing it does not break any test that mocks the
        // sender -- it breaks production, silently, by mailing customers repeatedly. See
        // AlertSendLog's remarks and docs/phase-20e-status.md Decision C.
        builder.HasIndex(x => new { x.AlertDefinitionId, x.OccurrenceDate, x.Recipient }).IsUnique();

        // Backs the Email Logs screen's own ordering/filter (newest first, per organization).
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });

        // No FK to AlertDefinitions on purpose: deleting an alert must not cascade away the proof
        // that mail was sent. ListAlertSendLogsQueryHandler left-joins and renders "(deleted alert)".
    }
}
