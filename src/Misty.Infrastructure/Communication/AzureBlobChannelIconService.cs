using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Misty.Application.Communication;

namespace Misty.Infrastructure.Communication;

public sealed class AzureBlobChannelIconService : IChannelIconService
{
    private const string ContainerName = "channel-icons";
    private readonly BlobServiceClient _client;

    public AzureBlobChannelIconService(BlobServiceClient client) => _client = client;

    public async Task<string> UploadAsync(Guid channelId, Stream content, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        await DeletePrefixAsync(container, channelId, ct);

        var ext = ExtensionFor(contentType);
        var blobName = $"{channelId}/{Guid.NewGuid():N}.{ext}";
        var blob = container.GetBlobClient(blobName);
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
        await blob.UploadAsync(content, options, ct);

        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(Guid channelId, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        await DeletePrefixAsync(container, channelId, ct);
    }

    private static async Task DeletePrefixAsync(BlobContainerClient container, Guid channelId, CancellationToken ct)
    {
        var prefix = $"{channelId}/";
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            await container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: ct);
        }
    }

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        "image/gif" => "gif",
        _ => "bin",
    };
}
