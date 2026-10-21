using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BudgetApi.Services.Storage;

/// <summary>
/// Stores files in an Azure Blob container. Authenticates with a connection string when one
/// is configured (Azurite locally), otherwise with Managed Identity via DefaultAzureCredential
/// — so no storage keys exist in the cloud deployment.
/// </summary>
public class AzureBlobFileStore : IFileStore
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobFileStore> _log;
    private bool _ensured;

    public AzureBlobFileStore(IConfiguration cfg, ILogger<AzureBlobFileStore> log)
    {
        _log = log;
        var containerName = cfg["Storage:Container"] ?? "uploads";
        var connStr = cfg["Storage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            _container = new BlobContainerClient(connStr, containerName);
        }
        else
        {
            var endpoint = cfg["Storage:BlobEndpoint"]
                ?? throw new InvalidOperationException("Storage:BlobEndpoint or Storage:ConnectionString is required for the azure file store.");
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = cfg["AZURE_CLIENT_ID"]
            });
            _container = new BlobContainerClient(new Uri(new Uri(endpoint), containerName), credential);
        }
    }

    public async Task<string> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        if (!_ensured)
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            _ensured = true;
        }
        var name = FileStoreExtensions.UniqueName(fileName);
        await _container.GetBlobClient(name).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
        _log.LogInformation("Stored blob {Name} in {Container}", name, _container.Name);
        return name;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}
