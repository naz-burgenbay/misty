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

    /// <summary>Returns a URL valid for <paramref name="validForMinutes"/> minutes that allows the bearer to read the blob without additional credentials.</summary>
    Task<string> GetReadUrlAsync(string container, string blobName, int validForMinutes = 60, CancellationToken ct = default);
}
