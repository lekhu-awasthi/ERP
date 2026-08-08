using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Infrastructure.Email;
using ErpApp.Infrastructure.Identity;
using ErpApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
