using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Imports;

public sealed class ImportJobRowConfiguration : IEntityTypeConfiguration<ImportJobRow>
{
    public void Configure(EntityTypeBuilder<ImportJobRow> builder)
    {
        builder.ToTable("ImportJobRows", schema: "imports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ImportJobId).IsRequired();
        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.RowNumber).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ColumnName).HasMaxLength(100);
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.Property(x => x.TargetCode).HasMaxLength(50);

        // THE load-bearing constraint of this phase, the direct analogue of AlertSendLog's
        // (definition, occurrence, recipient). The processor inserts here and commits BEFORE
        // sending the row's create/update command, so a crashed and resumed import cannot create
        // the same Product twice, and two runners cannot both process one row. Removing it breaks
        // no test that uses the InMemory provider -- it breaks production, silently, by duplicating
        // a tenant's master data on any restart. See ImportJobRowStatus and phase-21a-status.md.
        builder.HasIndex(x => new { x.ImportJobId, x.RowNumber }).IsUnique();

        // Deleting a job takes its rows with it: unlike AlertSendLog (which deliberately outlives
        // its definition, because the proof that mail was sent must survive), a row outcome is
        // meaningless without the job it belongs to and is never read on its own.
        builder.HasOne<ImportJob>()
            .WithMany()
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
