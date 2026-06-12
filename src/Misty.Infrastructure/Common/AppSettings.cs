using Microsoft.Extensions.Configuration;
using Misty.Application.Common;

namespace Misty.Infrastructure.Common;

public sealed class AppSettings : IAppSettings
{
    public string AppBaseUrl { get; }

    public AppSettings(IConfiguration config)
    {
        AppBaseUrl = config["App:BaseUrl"]?.TrimEnd('/') ?? "https://app-misty-dev.azurewebsites.net";
    }
}
