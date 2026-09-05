using ErpApp.Domain.Communications;
using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Communications;

public sealed class EmailSendLogConfiguration : IEntityTypeConfiguration<EmailSendLog>
{
    public void Configure(EntityTypeBuilder<EmailSendLog> builder)
    {
        builder.ToTable("EmailSendLogs", schema: "communications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.RequestId).IsRequired();
        builder.Property(x => x.ParentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ParentId).IsRequired();
        builder.Property(x => x.Context).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(x => x.ToAddresses).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CcAddresses).HasMaxLength(2000);
        builder.Property(x => x.BccAddresses).HasMaxLength(2000);
        builder.Property(x => x.ReplyTo).HasMaxLength(320);

        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.AttachDocumentPdf).IsRequired();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Property(x => x.SentByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // The runner's claim mechanism -- see EmailSendLog.RowVersion for why a concurrency token is
        // safe on this row when phase-21a's rule forbade one on ImportJob/ExportJob.
        builder.Property(x => x.RowVersion).IsRowVersion();

        // The idempotency constraint. A double-clicked Send, or a client that retried a request it
        // never saw succeed, resolves to one row and one email because the second insert loses this
        // index. A deliberate resend carries a fresh RequestId and is unaffected -- which is the
        // whole design, and the reason the key is a request id rather than a content hash.
        builder.HasIndex(x => new { x.OrganizationId, x.RequestId }).IsUnique();

        // The Email Logs tab's own query: newest first, for one parent.
        builder.HasIndex(x => new { x.OrganizationId, x.ParentType, x.ParentId, x.CreatedAt });

        // The runner's poll: the queue head, cheaply.
        builder.HasIndex(x => new { x.Status, x.CreatedAt });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SentByUserId).OnDelete(DeleteBehavior.Restrict);

        // No FK to EmailTemplates, deliberately, and for AlertSendLog's reason: deleting a template
        // must not cascade away the proof that mail went out. TemplateId is attribution and may
        // dangle -- the log stores the resolved text, so nothing about it needs the template to
        // still exist.

        // Phase 4's gotcha: the encapsulated child collection is reached through its backing field,
        // never a public setter. TestAppDbContext must restate this or it mis-tracks on InMemory.
        builder.Metadata
            .FindNavigation(nameof(EmailSendLog.Attachments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class EmailSendAttachmentConfiguration : IEntityTypeConfiguration<EmailSendAttachment>
{
    public void Configure(EntityTypeBuilder<EmailSendAttachment> builder)
    {
        builder.ToTable("EmailSendAttachments", schema: "communications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EmailSendLogId).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(400);

        builder.HasIndex(x => x.EmailSendLogId);

        // Cascade here, unlike the log's own FKs: an attachment row is meaningless without its
        // message, and nothing else references it.
        builder.HasOne<EmailSendLog>()
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.EmailSendLogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
