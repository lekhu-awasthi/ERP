using ErpApp.Application.Common.BotProtection;
using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Sms;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Exports;
using ErpApp.Application.Imports;
using ErpApp.Infrastructure.Alerts;
using ErpApp.Infrastructure.Exports;
using ErpApp.Infrastructure.Imports;
using ErpApp.Infrastructure.Jobs;
using ErpApp.Infrastructure.BotProtection;
using ErpApp.Infrastructure.Email;
using ErpApp.Infrastructure.Identity;
using ErpApp.Infrastructure.Persistence;
using ErpApp.Infrastructure.Sms;
using ErpApp.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ErpApp.Infrastructure;

/// <summary>
/// Composition-root extension for the Infrastructure layer. Called once from
/// Api/Program.cs (builder.Services.AddInfrastructure(builder.Configuration)).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection-string read is deferred inside this delegate (invoked lazily, the
        // first time AppDbContext is actually resolved) rather than done eagerly right here --
        // this method runs before the host is fully built, which is too early to see
        // configuration sources WebApplicationFactory adds for tests (Api.IntegrationTests
        // hit exactly this: the eager version threw before its own connection-string override
        // was ever chained in, even though nothing here touches the database until a request
        // actually needs it).
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Missing 'ConnectionStrings:Default'. Set it via appsettings.Development.json " +
                    "(local dev) or user-secrets (never commit a real connection string): " +
                    "dotnet user-secrets set \"ConnectionStrings:Default\" \"<your connection string>\" --project src/Api");

            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Missing 'Jwt:SigningKey'. Set it via user-secrets: " +
                "dotnet user-secrets set \"Jwt:SigningKey\" \"<a long random string>\" --project src/Api")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "Missing 'Email' configuration. Set it via user-secrets: " +
                "dotnet user-secrets set \"Email:From\" \"<address>\" --project src/Api (and SmtpServer/Port/Username/Password likewise)")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();

        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddScoped<IFileStorage, LocalDiskFileStorage>();
        services.AddScoped<ISmsSender, ConsoleSmsSender>();

        // Phase 20e (Alert Scheduler, FR-11.1) -- this codebase's first background job.
        // TryAddSingleton so a test host that already substituted a FakeTimeProvider keeps it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<AlertSchedulerOptions>()
            .Bind(configuration.GetSection(AlertSchedulerOptions.SectionName));
        services.AddHostedService<AlertSchedulerHostedService>();

        // Phase 21a (Bulk import, FR-2.9 / NFR-4.3) -- this codebase's second background job, and
        // Phase 21b (Full-tenant data export, FR-2.8) -- its third. Both are queue-driven and
        // user-initiated, so unlike the alert scheduler above they share one loop implementation
        // (QueuedJobRunnerHostedService) closed over their own processor and their own options.
        // Separate hosted services rather than one loop over both, so a long import cannot hold up
        // an export or the reverse; the alert scheduler is deliberately left alone, because a
        // schedule-driven idempotent job is a genuinely different shape. See
        // QueuedJobRunnerHostedService's doc comment and docs/phase-21b-status.md, Decision C.
        services.AddScoped<IImportFileReader, ClosedXmlImportFileReader>();
        services.AddOptions<ImportJobRunnerOptions>()
            .Bind(configuration.GetSection(ImportJobRunnerOptions.SectionName));
        services.AddHostedService<QueuedJobRunnerHostedService<IImportJobProcessor, ImportJobRunnerOptions>>();

        services.AddScoped<IExportWorkbookWriter, ClosedXmlExportWorkbookWriter>();
        services.AddOptions<ExportJobRunnerOptions>()
            .Bind(configuration.GetSection(ExportJobRunnerOptions.SectionName));
        services.AddHostedService<QueuedJobRunnerHostedService<IExportJobProcessor, ExportJobRunnerOptions>>();

        services.AddOptions<TurnstileOptions>()
            .Bind(configuration.GetSection(TurnstileOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey), "Missing 'Turnstile:SecretKey'. Set it via user-secrets: " +
                "dotnet user-secrets set \"Turnstile:SecretKey\" \"<your secret key>\" --project src/Api " +
                "(Cloudflare's always-passes dummy secret key for local dev is 1x0000000000000000000000000000000AA)")
            .ValidateOnStart();
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>();

        return services;
    }
}
