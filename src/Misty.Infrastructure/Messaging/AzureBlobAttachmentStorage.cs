using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Misty.Application.Messaging;

namespace Misty.Infrastructure.Messaging;

public sealed class AzureBlobAttachmentStorage : IAttachmentStorage
{
    private readonly BlobServiceClient _client;

    public AzureBlobAttachmentStorage(BlobServiceClient client) => _client = client;

    public async Task<string> UploadAsync(
        string container,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        var containerClient = _client.GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blob = containerClient.GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: ct);

        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string container, string blobName, CancellationToken ct = default)
    {
        var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public Task<string> GetReadUrlAsync(string container, string blobName, int validForMinutes = 60, CancellationToken ct = default)
    {
        var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);

        // BlobClient.CanGenerateSasUri is true when the client was built with a StorageSharedKeyCredential
        // (i.e. connection string with AccountKey). For Azurite and the deployed account this is always the case.
        if (blob.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(validForMinutes))
            {
                BlobContainerName = container,
                BlobName = blobName,
                Resource = "b",
            };
            return Task.FromResult(blob.GenerateSasUri(sasBuilder).ToString());
        }

        // Fallback for managed-identity clients: return the raw URI (works if the caller has network access).
        return Task.FromResult(blob.Uri.ToString());
    }
}
