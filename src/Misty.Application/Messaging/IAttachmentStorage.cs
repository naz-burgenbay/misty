namespace Misty.Application.Messaging;

public interface IAttachmentStorage
{
    Task<string> UploadAsync(
        string container,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    Task DeleteAsync(string container, string blobName, CancellationToken ct = default);
}
