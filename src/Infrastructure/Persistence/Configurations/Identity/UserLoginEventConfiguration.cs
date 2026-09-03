using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Identity;

public sealed class UserLoginEventConfiguration : IEntityTypeConfiguration<UserLoginEvent>
{
    public void Configure(EntityTypeBuilder<UserLoginEvent> builder)
    {
        builder.ToTable("UserLoginEvents", schema: "identity");

        builder.HasKey(e => e.Id);

        // No FK to Users: a failed attempt against an address that matches no user must still be
        // recorded, so UserId is genuinely optional and a constraint would reject exactly the rows
        // this table exists for. See UserLoginEvent's own remarks.
        builder.Property(e => e.UserId);
        builder.Property(e => e.Email).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();

        // 45 characters is the longest an IPv6 address can render, including an IPv4-mapped tail.
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(UserLoginEvent.UserAgentMaxLength);
        builder.Property(e => e.DeviceOs).HasMaxLength(100);
        builder.Property(e => e.Browser).HasMaxLength(100);

        // The report reads a date range for one organization's members, newest first. Both of the
        // ways it narrows -- by the member's user id, and by the attempted email for an attempt
        // that never resolved to one -- lead with the timestamp, because the period is the filter
        // every query applies and the sort it always uses.
        builder.HasIndex(e => new { e.OccurredAt, e.UserId });
        builder.HasIndex(e => new { e.OccurredAt, e.Email });
    }
}
