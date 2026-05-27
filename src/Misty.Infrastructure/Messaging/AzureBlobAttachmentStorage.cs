using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
}
