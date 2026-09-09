using ErpApp.Application.Platform.Commands.SetUserPreference;
using ErpApp.Application.Platform.Queries.GetUserPreferences;
using ErpApp.Application.Platform.Queries.GlobalSearch;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 33 -- the platform chrome's two endpoints: the top bar's global search, and the per-user
/// preference store behind Quick Links and the calendar toggle.
/// </summary>
public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Platform")
            .RequireAuthorization();

        group.MapGet("/search", async (
            Guid organizationId, string term, int? limit, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GlobalSearchQuery(organizationId, term, limit), ct);
            return Results.Ok(result);
        });

        // Every preference in one round trip -- see GetUserPreferencesQuery's remarks for why there
        // is no read-one endpoint.
        group.MapGet("/preferences", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetUserPreferencesQuery(organizationId), ct);
            return Results.Ok(result);
        });

        // PUT, not POST: writing a preference is idempotent and addressed by its key, so the same
        // request twice leaves the same single row. The key travels in the route rather than the
        // body so the address of the thing being replaced is the URL, which is what makes that true.
        group.MapPut("/preferences/{key}", async (
            Guid organizationId, string key, SetUserPreferenceRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SetUserPreferenceCommand(organizationId, key, request.Value), ct);
            return Results.Ok(result);
        });
    }
}

/// <summary>
/// The value, already serialized. It reaches the command as a string and is validated there per key
/// (<c>SetUserPreferenceCommandValidator</c>) -- binding it as <c>JsonElement</c> here would move
/// half of that validation into the Api layer for no gain.
/// </summary>
public sealed record SetUserPreferenceRequest(string Value);
