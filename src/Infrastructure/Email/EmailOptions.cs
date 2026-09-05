namespace ErpApp.Infrastructure.Email;

/// <summary>How outbound mail leaves this process.</summary>
public enum EmailDeliveryMode
{
    /// <summary>Real SMTP. The default, so a missing or misspelled setting can only ever fail
    /// closed toward sending real mail rather than silently swallowing it.</summary>
    Smtp,

    /// <summary>Write each message to disk as an <c>.eml</c> and send nothing. See
    /// <see cref="FileDropEmailSender"/> for why this is selected by configuration and never by
    /// environment name.</summary>
    FileDrop,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public required string From { get; init; }
    public required string SmtpServer { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }

    /// <summary>Phase 30. Optional with an <see cref="EmailDeliveryMode.Smtp"/> default, so adding
    /// it does not break the four host-booting integration-test suites' in-memory configuration —
    /// a new <c>required</c> member here turns CI red on its own (see CLAUDE.md's gotcha).</summary>
    public EmailDeliveryMode DeliveryMode { get; init; } = EmailDeliveryMode.Smtp;

    /// <summary>Where <see cref="EmailDeliveryMode.FileDrop"/> writes. Blank falls back to
    /// <c>%TEMP%/erpapp-maildrop</c>.</summary>
    public string? FileDropPath { get; init; }
}
