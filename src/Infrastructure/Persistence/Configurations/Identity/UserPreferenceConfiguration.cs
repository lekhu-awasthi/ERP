using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Identity;

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("UserPreferences", schema: "identity");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrganizationId).IsRequired();
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.Key).HasMaxLength(UserPreference.KeyMaxLength).IsRequired();
        builder.Property(e => e.Value).HasMaxLength(UserPreference.ValueMaxLength).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        // At most one row per setting per user per organization -- this index *is* the "row per key"
        // decision in UserPreference's remarks, and it is what makes the upsert in
        // SetUserPreferenceCommandHandler safe against two tabs saving at once. Not filtered: none of
        // the three columns is nullable, so the standing nullable-unique-index gotcha does not apply.
        builder.HasIndex(e => new { e.OrganizationId, e.UserId, e.Key }).IsUnique();
    }
}
