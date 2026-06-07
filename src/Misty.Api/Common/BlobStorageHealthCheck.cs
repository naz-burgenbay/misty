using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Misty.Api.Common;

internal sealed class BlobStorageHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    private readonly BlobClientOptions _options;
    private readonly string _containerName;

    public BlobStorageHealthCheck(string connectionString, BlobClientOptions options, string containerName)
    {
        _connectionString = connectionString;
        _options = options;
        _containerName = containerName;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var blobServiceClient = new BlobServiceClient(_connectionString, _options);
            var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Blob storage container '{_containerName}' is not accessible: {ex.Message}", ex);
        }
    }
}
