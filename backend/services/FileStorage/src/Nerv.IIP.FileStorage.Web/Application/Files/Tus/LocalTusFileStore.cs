using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public sealed class LocalTusFileStore
{
    private readonly string rootPath;

    public LocalTusFileStore(IConfiguration configuration)
    {
        rootPath = configuration["FileStorage:Tus:RootPath"]
            ?? Path.Combine(Path.GetTempPath(), "nerv-iip", "filestorage", "tus");
        Directory.CreateDirectory(rootPath);
    }

    public long GetOffset(string uploadSessionId)
    {
        var path = GetUploadPath(uploadSessionId);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    public async Task<long> AppendAsync(string uploadSessionId, long expectedOffset, Stream content, CancellationToken cancellationToken)
    {
        var path = GetUploadPath(uploadSessionId);
        await using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (file.Length != expectedOffset)
        {
            return file.Length;
        }

        file.Seek(0, SeekOrigin.End);
        await content.CopyToAsync(file, cancellationToken);
        return file.Length;
    }

    public FileStream OpenRead(string uploadSessionId)
    {
        return new FileStream(GetUploadPath(uploadSessionId), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private string GetUploadPath(string uploadSessionId)
    {
        return Path.Combine(rootPath, $"{Hash(uploadSessionId)}.bin");
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
