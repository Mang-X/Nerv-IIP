using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public sealed class LocalUploadByteStore
{
    private readonly string rootPath;
    private readonly string? volumeIdentity;
    private const string FinalPrefix = "final-";
    private readonly ConcurrentDictionary<string, SemaphoreSlim> uploadLocks = new(StringComparer.Ordinal);

    public LocalUploadByteStore(IConfiguration configuration)
    {
        // 不回落系统 temp：该目录承载已 complete 文件的字节（ADR 0024 §5）。缺配置由 Program.cs 在启动时拒绝。
        rootPath = configuration["FileStorage:Tus:RootPath"]!;
        volumeIdentity = configuration["FileStorage:Tus:VolumeIdentity"];
    }

    public void VerifyVolume(bool required)
    {
        if (!required && volumeIdentity is null)
        {
            return;
        }

        var marker = Path.Combine(rootPath, ".filestorage-volume-v1");
        try
        {
            if (string.IsNullOrWhiteSpace(volumeIdentity)
                || !File.Exists(marker)
                || !string.Equals(File.ReadAllText(marker).Trim(), volumeIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("FileStorage 持久卷身份缺失或不匹配。");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("FileStorage 无法读取持久卷身份。");
        }
    }

    public bool FinalExists(string objectKey) => File.Exists(GetFinalPath(objectKey));

    public FileStream? TryOpenFinal(string objectKey)
    {
        try
        {
            return new FileStream(GetFinalPath(objectKey), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public bool DeleteFinal(string objectKey)
    {
        var path = GetFinalPath(objectKey);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    internal Task<UploadCommitStorageResult> PromoteAsync(
        string uploadSessionId,
        string objectKey,
        long expectedSizeBytes,
        string? expectedChecksum,
        CancellationToken cancellationToken) =>
        PromoteFromPathAsync(GetUploadPath(uploadSessionId), objectKey, expectedSizeBytes, expectedChecksum, cancellationToken);

    internal Task<UploadCommitStorageResult> PromoteLegacyAsync(
        string uploadSessionId,
        string objectKey,
        long expectedSizeBytes,
        string expectedChecksum,
        CancellationToken cancellationToken) =>
        PromoteFromPathAsync(GetUploadPaths(uploadSessionId).Legacy, objectKey, expectedSizeBytes, expectedChecksum, cancellationToken);

    private async Task<UploadCommitStorageResult> PromoteFromPathAsync(
        string staging, string objectKey, long expectedSizeBytes, string? expectedChecksum, CancellationToken cancellationToken)
    {
        var destination = GetFinalPath(objectKey);
        if (!File.Exists(destination))
        {
            if (!File.Exists(staging))
            {
                return UploadCommitStorageResult.ProvenNoFinalActionStarted();
            }

            var stagingEvidence = await ReadEvidenceAsync(staging, cancellationToken);
            if (!MatchesIntent(stagingEvidence, expectedSizeBytes, expectedChecksum))
            {
                return stagingEvidence with
                {
                    FailureDisposition = UploadCommitFailureDisposition.ProvenNoFinalActionStarted
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 两种命名空间共享父目录，提交始终在同一个文件系统内。
                File.Move(staging, destination);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // 并发恢复可能已经发布了目标，统一回读最终文件。
            }
        }

        return await ReadEvidenceAsync(destination, cancellationToken);
    }

    private static bool MatchesIntent(UploadCommitStorageResult result, long size, string? checksum) =>
        result.SizeBytes == size
        && (checksum is null || string.Equals(result.CanonicalChecksum, checksum, StringComparison.Ordinal));

    private static async Task<UploadCommitStorageResult> ReadEvidenceAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var hash = await SHA256.HashDataAsync(file, cancellationToken);
        return UploadCommitStorageResult.Verified(file.Length, $"sha256:{Convert.ToHexStringLower(hash)}");
    }

    private string GetFinalPath(string objectKey)
    {
        VerifyVolume(required: false);
        return Path.Combine(rootPath, GetFinalFileName(objectKey));
    }

    internal static string GetFinalFileName(string objectKey) => $"{FinalPrefix}{Hash(objectKey)}.bin";

    internal IEnumerable<string> EnumerateVolumeEntries() => Directory.EnumerateFileSystemEntries(rootPath);

    internal static bool IsFinalFile(string path) =>
        Path.GetFileName(path).StartsWith(FinalPrefix, StringComparison.Ordinal)
        && Path.GetExtension(path) == ".bin"
        && File.Exists(path);

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
        var removed = false;
        var paths = GetUploadPaths(uploadSessionId);
        foreach (var path in new[] { paths.Staging, paths.Legacy })
        {
            if (File.Exists(path))
            {
                removed |= TryDeleteFile(path);
            }
        }

        return removed;
    }

    public int DeleteFilesExcept(IEnumerable<string> uploadSessionIds, DateTimeOffset olderThanUtc)
    {
        VerifyVolume(required: false);
        if (!Directory.Exists(rootPath))
        {
            return 0;
        }

        var retainedFileNames = uploadSessionIds
            .SelectMany(id =>
            {
                var paths = GetUploadPaths(id);
                return new[] { paths.Staging, paths.Legacy };
            })
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(rootPath, "*.bin"))
        {
            try
            {
                var fileName = Path.GetFileName(path);
                if (fileName.StartsWith(FinalPrefix, StringComparison.Ordinal)
                    || retainedFileNames.Contains(fileName)
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
        var path = GetUploadPath(uploadSessionId);
        Directory.CreateDirectory(rootPath);
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

    internal FileStream OpenWrite(string uploadSessionId)
    {
        var path = GetUploadPath(uploadSessionId);
        Directory.CreateDirectory(rootPath);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
    }

    public FileStream OpenRead(string uploadSessionId)
    {
        return new FileStream(GetUploadPath(uploadSessionId), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private string GetUploadPath(string uploadSessionId)
    {
        var paths = GetUploadPaths(uploadSessionId);
        return File.Exists(paths.Legacy) && !File.Exists(paths.Staging) ? paths.Legacy : paths.Staging;
    }

    private (string Staging, string Legacy) GetUploadPaths(string uploadSessionId)
    {
        VerifyVolume(required: false);
        var name = $"{Hash(uploadSessionId)}.bin";
        return (Path.Combine(rootPath, $"staging-{name}"), Path.Combine(rootPath, name));
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
