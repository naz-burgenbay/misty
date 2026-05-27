namespace Misty.Application.Messaging;

// Abstraction over Blob Storage (Azurite locally, Azure Blob Storage in cloud).
// The repository handles the database row; the storage handles the blob bytes.
public interface IAttachmentStorage
{
    // Uploads the content to the named container/blob and returns a public URL (CDN URL in production, Azurite URL locally).
    Task<string> UploadAsync(
        string container,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    Task DeleteAsync(string container, string blobName, CancellationToken ct = default);
}
