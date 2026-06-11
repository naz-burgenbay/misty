using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Misty.Application.Users;

namespace Misty.Infrastructure.Users;

public sealed class AzureBlobAvatarService : IAvatarService
{
    private const string ContainerName = "avatars";
    private readonly BlobServiceClient _client;

    public AzureBlobAvatarService(BlobServiceClient client) => _client = client;

    public async Task<string> UploadAsync(Guid userId, Stream content, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = container.GetBlobClient(userId.ToString());
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
        await blob.UploadAsync(content, options, ct);

        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(Guid userId, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        var blob = container.GetBlobClient(userId.ToString());
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }
}
