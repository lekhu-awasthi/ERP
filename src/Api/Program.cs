using System.Text;
using System.Text.Json.Serialization;
using ErpApp.Api.Endpoints;
using ErpApp.Api.Middleware;
using ErpApp.Api.Services;
using ErpApp.Application;
using ErpApp.Application.Common.Security;
using ErpApp.Infrastructure;
using ErpApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// Phase 20d -- QuestPDF's Community license is free for this project's size/revenue bracket but
// must be set explicitly or every document-generation call throws at runtime. A static settings
// assignment, not config-dependent, so it's safe this early (unlike the config-read-before-Build
// gotcha this file's other setup has to avoid).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Enums (MembershipRole, etc.) serialize as readable strings rather than numbers -- the first
// enums to cross the Api boundary land in this phase's Tenancy DTOs.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Explicit origin allow-list (not AllowAnyOrigin) because AllowCredentials is required for
// the httpOnly JWT cookie to flow on cross-origin requests from the Angular dev server, and
// AllowAnyOrigin can never be combined with AllowCredentials.
const string CorsPolicyName = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured via IOptions<JwtOptions> (resolved lazily, on first use) rather than reading
// builder.Configuration directly here -- this line runs before the host is fully built, so a
// direct read would snapshot configuration too early to see sources added later (test-only
// config overrides in WebApplicationFactory, notably), causing a null SigningKey there even
// though the same config resolves fine everywhere else via ValidateOnStart().
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;

        // Without this, the handler remaps "sub"/"email" to long legacy XML-namespace claim
        // types on the way in, so ClaimsPrincipal.FindFirstValue(JwtRegisteredClaimNames.Sub)
        // (used by /me) would silently return null even though the token is valid.
        bearerOptions.MapInboundClaims = false;

        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // The JWT travels in an httpOnly cookie, not an Authorization header (roadmap Phase 1a task 3).
        bearerOptions.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthEndpoints.AuthCookieName, out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAppExceptionHandler();

app.UseHttpsRedirection();

app.UseCors(CorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

// Phase 0 exit criteria: GET /health returns 200, proving DI/MediatR/EF Core all wired
// without needing a real database yet.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithTags("Health");

app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapConfigurationEndpoints();
app.MapContactsEndpoints();
app.MapCatalogEndpoints();
app.MapAccountingEndpoints();
app.MapSalesEndpoints();
app.MapPaymentsEndpoints();
app.MapPurchasingEndpoints();
app.MapInventoryEndpoints();
app.MapWorkflowEndpoints();
app.MapCrmEndpoints();
app.MapAttachmentsEndpoints();
app.MapPrintingEndpoints();
app.MapImportsEndpoints();
app.MapExportsEndpoints();

app.Run();

// Exposed for Api.IntegrationTests' WebApplicationFactory<Program>.
public partial class Program;
