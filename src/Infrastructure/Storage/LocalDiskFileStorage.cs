using ErpApp.Application.Common.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.Storage;

/// <summary>
/// Dev implementation of IFileStorage (docs/phase-18-status.md decision #1). Keys are opaque
/// GUID-named files on local disk -- the original file name is never used as the on-disk name (it's
/// stored separately on Attachment.FileName), which sidesteps both path-traversal and collision
/// concerns without needing to sanitize an arbitrary user-supplied name.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootPath);

        var key = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var path = ResolvePath(key);

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No stored file for key '{key}'.");
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>Guards against a key escaping _rootPath via path separators/traversal segments --
    /// keys are always our own Guid.NewGuid() output, but this is the one boundary where a
    /// corrupted/tampered key could otherwise reach an arbitrary path.</summary>
    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\') || key.Contains(".."))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        return Path.Combine(_rootPath, key);
    }
}
