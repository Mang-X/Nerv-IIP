using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public sealed class LocalTusFileStore
{
    private readonly string rootPath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> uploadLocks = new(StringComparer.Ordinal);

    public LocalTusFileStore(IConfiguration configuration)
    {
        rootPath = configuration["FileStorage:Tus:RootPath"]
            ?? Path.Combine(Path.GetTempPath(), "nerv-iip", "filestorage", "tus");
    }

    public long GetOffset(string uploadSessionId)
    {
        var path = GetUploadPath(uploadSessionId);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    public bool Exists(string uploadSessionId)
    {
        return File.Exists(GetUploadPath(uploadSessionId));
    }

    public bool Delete(string uploadSessionId)
    {
        var path = GetUploadPath(uploadSessionId);
        if (File.Exists(path))
        {
            return TryDeleteFile(path);
        }

        return false;
    }

    public int DeleteFilesExcept(IEnumerable<string> uploadSessionIds, DateTimeOffset olderThanUtc)
    {
        if (!Directory.Exists(rootPath))
        {
            return 0;
        }

        var retainedFileNames = uploadSessionIds
            .Select(uploadSessionId => Path.GetFileName(GetUploadPath(uploadSessionId)))
            .ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(rootPath, "*.bin"))
        {
            try
            {
                if (retainedFileNames.Contains(Path.GetFileName(path))
                    || File.GetLastWriteTimeUtc(path) > olderThanUtc.UtcDateTime)
                {
                    continue;
                }

                if (TryDeleteFile(path))
                {
                    removed++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    public async Task<string> ComputeSha256HexAsync(string uploadSessionId, CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(uploadSessionId);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<long> AppendAsync(string uploadSessionId, long expectedOffset, Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootPath);
        var path = GetUploadPath(uploadSessionId);
        var uploadLock = uploadLocks.GetOrAdd(uploadSessionId, _ => new SemaphoreSlim(1, 1));
        await uploadLock.WaitAsync(cancellationToken);
        try
        {
            await using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (file.Length != expectedOffset)
            {
                return file.Length;
            }

            file.Seek(0, SeekOrigin.End);
            await content.CopyToAsync(file, cancellationToken);
            file.Flush(flushToDisk: true);
            return file.Length;
        }
        finally
        {
            uploadLock.Release();
        }
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

    private static bool TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
