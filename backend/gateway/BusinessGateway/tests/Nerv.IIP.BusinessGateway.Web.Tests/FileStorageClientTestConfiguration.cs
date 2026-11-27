using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

internal static class FileStorageClientTestConfiguration
{
    internal const string DownloadToken = "file-download-test-token";

    internal static IConfiguration Create() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:DownloadBearerToken"] = DownloadToken
        })
        .Build();
}
