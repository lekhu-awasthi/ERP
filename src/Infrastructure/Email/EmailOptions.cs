namespace ErpApp.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public required string From { get; init; }
    public required string SmtpServer { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
}
