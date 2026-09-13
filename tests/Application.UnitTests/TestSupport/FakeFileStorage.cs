using ErpApp.Application.Common.Storage;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>In-memory IFileStorage -- avoids touching real disk in handler tests.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = [];

    /// <summary>Phase 39 -- every key this store has been asked to write, in order, and every key it
    /// has been asked to delete. A feature that writes blobs owes its deletion story (phase-21b
    /// Decision E), and "the replaced file was deleted" is only assertable if the double remembers
    /// the calls rather than only the surviving state.</summary>
    public List<string> SavedKeys { get; } = [];

    public List<string> DeletedKeys { get; } = [];

    public Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var key = Guid.NewGuid().ToString("N");
        _files[key] = buffer.ToArray();
        SavedKeys.Add(key);
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
        DeletedKeys.Add(key);
        return Task.CompletedTask;
    }

    public bool Contains(string key) => _files.ContainsKey(key);
}
