using ErpApp.Domain.Identity;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Workflow;

public sealed class UploadedDocumentConfiguration : IEntityTypeConfiguration<UploadedDocument>
{
    public void Configure(EntityTypeBuilder<UploadedDocument> builder)
    {
        builder.ToTable("UploadedDocuments", schema: "workflow");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(260).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Label).HasMaxLength(60);
        builder.Property(x => x.UploadedByUserId).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // LinkedTransactionType/LinkedTransactionId are a polymorphic pair, not a real FK -- an
        // inbox document can point at an Invoice, PurchaseBill, Expense or Payment row, and there is
        // no shared table to reference. Existence in this organization is verified explicitly by
        // LinkInboxDocumentCommandHandler, the same way WorkTask/Attachment verify their parents.
        builder.Property(x => x.LinkedTransactionType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.LinkedTransactionId);
        builder.Property(x => x.LinkedAt);

        builder.Property(x => x.ExtractionStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        // No length cap: the payload is a model's JSON suggestion for one document, and a truncated
        // one would deserialize to null and silently look like "extraction never ran".
        builder.Property(x => x.ExtractedDataJson);
        builder.Property(x => x.ExtractionModelId).HasMaxLength(100);
        builder.Property(x => x.ExtractionFailureReason).HasMaxLength(500);
        builder.Property(x => x.ExtractionAttemptedAt);

        // The inbox grid's own ordering (Pending tab, newest first).
        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.UploadedAt });

        // Exit criterion #2's read path: the source-document panel on a transaction detail page
        // looks a document up by the transaction it produced.
        builder.HasIndex(x => new { x.OrganizationId, x.LinkedTransactionType, x.LinkedTransactionId });

        // No concurrency token, deliberately. Phase 21a's Decision C is the record of what one costs
        // on a row more than one writer touches; here nothing but a user's own request ever writes
        // this row, and the aggregate refuses a second link on its own.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
