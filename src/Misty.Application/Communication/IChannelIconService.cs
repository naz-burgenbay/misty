namespace Misty.Application.Communication;

public interface IChannelIconService
{
    Task<string> UploadAsync(Guid channelId, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(Guid channelId, CancellationToken ct = default);
}
