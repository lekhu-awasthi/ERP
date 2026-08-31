using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class AlertDefinitionTests
{
    [Fact]
    public void Create_starts_active_and_records_the_scheduling_user()
    {
        var userId = Guid.NewGuid();

        var alert = AlertDefinition.Create(
            Guid.NewGuid(), "Daily summary", AlertMedium.Email, AlertType.DailyTransactionSummary,
            "ops@example.test", AlertScheduleFrequency.Daily, new TimeOnly(19, 57), userId);

        Assert.True(alert.IsActive);
        Assert.Equal(userId, alert.CreatedByUserId);
        Assert.Equal(new TimeOnly(19, 57), alert.ScheduleTime);
    }

    /// <summary>Seconds are dropped so that the stored value can always be round-tripped by the
    /// HH:mm picker the reference product uses -- see AlertDefinition.Create.</summary>
    [Fact]
    public void Create_truncates_the_schedule_time_to_whole_minutes()
    {
        var alert = Build(scheduleTime: new TimeOnly(19, 57, 42));

        Assert.Equal(new TimeOnly(19, 57), alert.ScheduleTime);
        Assert.Equal(0, alert.ScheduleTime.Second);
    }

    [Fact]
    public void Recipients_are_parsed_trimmed_and_split_on_commas()
    {
        var alert = Build(recipients: " a@example.test ,b@example.test");

        Assert.Equal(["a@example.test", "b@example.test"], alert.RecipientAddresses);
    }

    /// <summary>Mail clients disagree about the separator; an admin pasting a semicolon list would
    /// otherwise get one giant invalid address.</summary>
    [Fact]
    public void Recipients_also_accept_semicolons_and_are_normalized_to_commas()
    {
        var alert = Build(recipients: "a@example.test;b@example.test");

        Assert.Equal(["a@example.test", "b@example.test"], alert.RecipientAddresses);
        Assert.Equal("a@example.test, b@example.test", alert.Recipients);
    }

    /// <summary>A duplicate address would otherwise mail the same person twice for one occurrence --
    /// the send ledger's per-recipient uniqueness would not catch it, because both sends are for
    /// genuinely different rows only if the strings differ.</summary>
    [Fact]
    public void Duplicate_recipients_are_removed_case_insensitively()
    {
        var alert = Build(recipients: "ops@example.test, OPS@example.test , ops@example.test");

        Assert.Equal(["ops@example.test"], alert.RecipientAddresses);
    }

    [Fact]
    public void Blank_recipients_parse_to_an_empty_list_rather_than_a_single_empty_entry()
    {
        Assert.Empty(AlertDefinition.ParseRecipients(null));
        Assert.Empty(AlertDefinition.ParseRecipients("   "));
        Assert.Empty(AlertDefinition.ParseRecipients(" , ; ,"));
    }

    [Fact]
    public void Update_replaces_every_editable_field()
    {
        var alert = Build();

        alert.Update(
            "Renamed", AlertMedium.Email, AlertType.CrmReport, "new@example.test",
            AlertScheduleFrequency.Daily, new TimeOnly(6, 30), isActive: false);

        Assert.Equal("Renamed", alert.Name);
        Assert.Equal(AlertType.CrmReport, alert.AlertType);
        Assert.Equal(["new@example.test"], alert.RecipientAddresses);
        Assert.Equal(new TimeOnly(6, 30), alert.ScheduleTime);
        Assert.False(alert.IsActive);
    }

    [Fact]
    public void SetActive_toggles_only_the_active_flag()
    {
        var alert = Build();

        alert.SetActive(false);

        Assert.False(alert.IsActive);
        Assert.Equal("Daily summary", alert.Name);
        Assert.Equal(new TimeOnly(19, 57), alert.ScheduleTime);
    }

    private static AlertDefinition Build(
        string recipients = "ops@example.test", TimeOnly? scheduleTime = null) =>
        AlertDefinition.Create(
            Guid.NewGuid(), "Daily summary", AlertMedium.Email, AlertType.DailyTransactionSummary,
            recipients, AlertScheduleFrequency.Daily, scheduleTime ?? new TimeOnly(19, 57), Guid.NewGuid());
}
