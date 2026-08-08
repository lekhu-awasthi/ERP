using ErpApp.Application;
using ErpApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// TODO: this is a wide-open dev-only policy (any origin/method/header, no credentials).
// Before Phase 1 ships auth, tighten this to an explicit allow-list of the Angular
// app's actual origin(s) via configuration — AllowAnyOrigin can never be combined
// with AllowCredentials, so it won't work once cookie-based JWT auth is wired up.
const string AllowAllCorsPolicy = "AllowAll";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AllowAllCorsPolicy, policy =>
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(AllowAllCorsPolicy);

// Phase 0 exit criteria: GET /health returns 200, proving DI/MediatR/EF Core all wired
// without needing a real database yet.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Health");

app.Run();

// Exposed for Api.IntegrationTests' WebApplicationFactory<Program>.
public partial class Program;
