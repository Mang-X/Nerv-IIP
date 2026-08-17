using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.FileStorage.Web.Tests;

internal static class FileStorageTestConfiguration
{
    public static IConfiguration Default { get; } = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
        .Build();
}
