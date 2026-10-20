namespace BudgetApi.Services.Storage;

public class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IConfiguration cfg)
    {
        _root = cfg["Files:Root"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");
    }

    public async Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var fullPath = Path.Combine(_root, FileStoreExtensions.UniqueName(fileName));
        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);
        return fullPath;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(File.Exists(key) ? File.OpenRead(key) : null);
}
