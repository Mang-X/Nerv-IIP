using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Tests;

internal sealed class TusTestStaging : IDisposable
{
    internal string RootPath { get; } = Path.Combine(Path.GetTempPath(), $"filestorage-staging-{Guid.NewGuid():N}");

    internal ILocalTusFileStoreAccessor CreateAccessor() => new LocalTusFileStoreAccessor(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:Tus:RootPath"] = RootPath
        }).Build());

    internal static byte[] ZipBytes(int length)
    {
        var bytes = new byte[length];
        new byte[] { 0x50, 0x4b, 0x03, 0x04 }.CopyTo(bytes, 0);
        return bytes;
    }

    internal static string Checksum(byte[] bytes) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";

    internal static async Task StageAsync(ILocalTusFileStoreAccessor accessor, string sessionId, byte[] bytes)
    {
        Assert.True(accessor.TryGet(out var store));
        using var content = new MemoryStream(bytes);
        await store.AppendAsync(sessionId, 0, content, CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
