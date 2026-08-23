namespace ErpApp.Infrastructure.Storage;

/// <summary>
/// Not secret data (unlike ConnectionStrings/Jwt/Email), so this is safely settable directly in
/// appsettings.*.json rather than requiring user-secrets -- a local disk path carries no credential.
/// RootPath defaults to an "App_Data" folder (the traditional ASP.NET convention for a
/// non-web-servable data directory) under the Api project's own content root -- outside any
/// possible wwwroot, and Program.cs never calls UseStaticFiles() at all, so nothing under the
/// content root is served as static content regardless; App_Data is still the deliberate choice so
/// a later cloud migration finds one clearly-named local fallback location, not a guess.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string RootPath { get; set; } = "App_Data/attachments";
}
