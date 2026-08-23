using ErpApp.Application.Common.Storage;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>In-memory IFileStorage -- avoids touching real disk in handler tests.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = [];

    public Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var key = Guid.NewGuid().ToString("N");
        _files[key] = buffer.ToArray();
        return Task.FromResult(key);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(key, out var bytes))
        {
            throw new FileNotFoundException($"No stored file for key '{key}'.");
        }

        Stream stream = new MemoryStream(bytes);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _files.Remove(key);
        return Task.CompletedTask;
    }

    public bool Contains(string key) => _files.ContainsKey(key);
}
