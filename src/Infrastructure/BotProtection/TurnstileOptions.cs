namespace ErpApp.Infrastructure.BotProtection;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public required string SecretKey { get; init; }
}
