using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class AlertDefinitionConfiguration : IEntityTypeConfiguration<AlertDefinition>
{
    public void Configure(EntityTypeBuilder<AlertDefinition> builder)
    {
        builder.ToTable("AlertDefinitions", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Medium).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.AlertType).HasConversion<string>().HasMaxLength(40).IsRequired();
        // Length must stay in step with AlertDefinitionValidation.MaxRecipientsLength -- a form the
        // validator accepts must not then fail at the database.
        builder.Property(x => x.Recipients).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20).IsRequired();
        // time(0) -- the picker is HH:mm and AlertDefinition truncates seconds, so storing
        // sub-minute precision would only create values the UI can never round-trip.
        builder.Property(x => x.ScheduleTime).HasColumnType("time(0)").IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Expression-bodied with no backing field, so EF would not map it anyway -- declared
        // explicitly because a future refactor to a stored property would otherwise silently add a
        // column.
        builder.Ignore(x => x.RecipientAddresses);

        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();

        // The dispatcher's due-query is deliberately cross-tenant (see AlertDispatcher), so its
        // index deliberately does not lead with OrganizationId.
        builder.HasIndex(x => new { x.IsActive, x.ScheduleTime });
    }
}
