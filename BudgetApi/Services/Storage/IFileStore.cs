namespace BudgetApi.Services.Storage;

/// <summary>
/// Where uploaded files (receipts, invoices, notes) live. FileBlob.StoragePath holds the
/// key returned by SaveAsync: an absolute path for the local store, a blob name for Azure.
/// </summary>
public interface IFileStore
{
    Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
}

public static class FileStoreExtensions
{
    public static async Task<string> SaveAsync(this IFileStore store, IFormFile file, CancellationToken ct = default)
    {
        await using var s = file.OpenReadStream();
        return await store.SaveAsync(file.FileName, file.ContentType ?? "application/octet-stream", s, ct);
    }

    public static string UniqueName(string fileName) => $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
}
