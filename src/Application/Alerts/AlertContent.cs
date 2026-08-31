namespace ErpApp.Application.Alerts;

/// <summary>The rendered body of one alert occurrence, ready to hand to IEmailSender.</summary>
public sealed record AlertContent(string Subject, string Body);
