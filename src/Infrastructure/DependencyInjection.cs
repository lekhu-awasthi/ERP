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
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Missing 'ConnectionStrings:Default'. Set it via appsettings.Development.json " +
                "(local dev) or user-secrets (never commit a real connection string): " +
                "dotnet user-secrets set \"ConnectionStrings:Default\" \"<your connection string>\" --project src/Api");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        return services;
    }
}
