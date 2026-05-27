namespace Misty.Domain.Messaging;

public enum AttachmentOwnerType
{
    Message = 1,
    Avatar = 2,
    ChannelIcon = 3,
}

public sealed class Attachment
{
    private Attachment() { } // For EF Core

    public Guid Id { get; private set; }
    public AttachmentOwnerType OwnerType { get; private set; }

    // Exactly one of the following is non-null, enforced by a CHECK constraint matching OwnerType.
    public Guid? MessageId { get; private set; }
    public Guid? AvatarUserId { get; private set; }
    public Guid? ChannelIconChannelId { get; private set; }

    public string BlobContainer { get; private set; } = null!;
    public string BlobName { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string CdnUrl { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    public static Attachment CreateForMessage(
        Guid id,
        Guid messageId,
        string blobContainer,
        string blobName,
        string fileName,
        string contentType,
        long sizeBytes,
        string cdnUrl)
        => new()
        {
            Id = id,
            OwnerType = AttachmentOwnerType.Message,
            MessageId = messageId,
            BlobContainer = blobContainer,
            BlobName = blobName,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CdnUrl = cdnUrl,
            CreatedAt = DateTime.UtcNow,
        };

    public static Attachment CreateForAvatar(
        Guid id,
        Guid avatarUserId,
        string blobContainer,
        string blobName,
        string fileName,
        string contentType,
        long sizeBytes,
        string cdnUrl)
        => new()
        {
            Id = id,
            OwnerType = AttachmentOwnerType.Avatar,
            AvatarUserId = avatarUserId,
            BlobContainer = blobContainer,
            BlobName = blobName,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CdnUrl = cdnUrl,
            CreatedAt = DateTime.UtcNow,
        };

    public static Attachment CreateForChannelIcon(
        Guid id,
        Guid channelId,
        string blobContainer,
        string blobName,
        string fileName,
        string contentType,
        long sizeBytes,
        string cdnUrl)
        => new()
        {
            Id = id,
            OwnerType = AttachmentOwnerType.ChannelIcon,
            ChannelIconChannelId = channelId,
            BlobContainer = blobContainer,
            BlobName = blobName,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CdnUrl = cdnUrl,
            CreatedAt = DateTime.UtcNow,
        };
}
