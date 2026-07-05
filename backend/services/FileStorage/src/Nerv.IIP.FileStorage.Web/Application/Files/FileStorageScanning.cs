using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed record FileStorageScanBatchResult(int CleanFiles, int MalwareFiles, int FailedFiles);

internal sealed record FileContentScanResult(string ScanStatus, string Detail)
{
    public static readonly FileContentScanResult Clean = new(FileStorageScanPolicy.Clean, "No malware signature detected.");
    public static FileContentScanResult Malware(string detail) => new(FileStorageScanPolicy.Malware, detail);
    public static FileContentScanResult Failed(string detail) => new(FileStorageScanPolicy.Failed, detail);
}

internal interface IFileContentScanner
{
    Task<FileContentScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public sealed record FileStorageSecurityAlertIntent(
    string OrganizationId,
    string EnvironmentId,
    string FileId,
    string FilePurpose,
    string FileName,
    string ScanDetail);

public interface IFileStorageSecurityAlertSink
{
    Task PublishMalwareDetectedAsync(FileStorageSecurityAlertIntent intent, CancellationToken cancellationToken);
}

public sealed class LoggingFileStorageSecurityAlertSink(ILogger<LoggingFileStorageSecurityAlertSink> logger)
    : IFileStorageSecurityAlertSink
{
    public Task PublishMalwareDetectedAsync(FileStorageSecurityAlertIntent intent, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "FileStorage security alert notification intent: malware detected for file {FileId} ({FilePurpose}/{FileName}) in {OrganizationId}/{EnvironmentId}: {ScanDetail}",
            intent.FileId,
            intent.FilePurpose,
            intent.FileName,
            intent.OrganizationId,
            intent.EnvironmentId,
            intent.ScanDetail);
        return Task.CompletedTask;
    }
}

internal sealed class LocalEicarFileContentScanner : IFileContentScanner
{
    private const int MaxSignatureScanBytes = 64 * 1024;

    private static readonly byte[][] MalwareTestSignatures =
    [
        Encoding.ASCII.GetBytes("EICAR-STANDARD-ANTIVIRUS-TEST-FILE"),
        Encoding.ASCII.GetBytes("NERV-IIP-MALWARE-TEST-FILE")
    ];

    public async Task<FileContentScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxSignatureScanBytes];
        var read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        var scannedBytes = buffer.AsSpan(0, read);
        foreach (var signature in MalwareTestSignatures)
        {
            if (scannedBytes.IndexOf(signature) >= 0)
            {
                return FileContentScanResult.Malware("Malware test signature detected.");
            }
        }

        return FileContentScanResult.Clean;
    }
}

internal sealed class ClamAvFileContentScanner(IConfiguration configuration) : IFileContentScanner
{
    public async Task<FileContentScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        var host = configuration["FileStorage:Scanning:ClamAv:Host"] ?? "localhost";
        var port = configuration.GetValue<int?>("FileStorage:Scanning:ClamAv:Port") ?? 3310;
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        await using var network = client.GetStream();
        await network.WriteAsync("zINSTREAM\0"u8.ToArray(), cancellationToken);
        var buffer = new byte[8192];
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            var lengthBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(read));
            await network.WriteAsync(lengthBytes, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await network.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        using var reader = new StreamReader(network, Encoding.UTF8, leaveOpen: true);
        var response = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return FileContentScanResult.Malware(response);
        }

        return response.Contains("OK", StringComparison.OrdinalIgnoreCase)
            ? FileContentScanResult.Clean
            : FileContentScanResult.Failed($"ClamAV returned '{response}'.");
    }
}

public sealed class PostgreSqlFileStorageScanner(
    ApplicationDbContext dbContext,
    ILocalTusFileStoreAccessor tusStoreAccessor,
    IConfiguration configuration,
    IFileStorageSecurityAlertSink alertSink,
    ILogger<PostgreSqlFileStorageScanner> logger)
{
    public async Task<FileStorageScanBatchResult> ScanPendingFilesAsync(CancellationToken cancellationToken)
    {
        if (!FileStorageScanPolicy.IsScanningEnabled(configuration))
        {
            return new FileStorageScanBatchResult(0, 0, 0);
        }

        var pendingFiles = await dbContext.StoredFiles
            .Where(x => x.ScannedAtUtc == null && x.Status == FileStorageScanPolicy.Available)
            .OrderBy(x => x.CompletedAtUtc)
            .Take(ResolveBatchSize(configuration))
            .ToArrayAsync(cancellationToken);
        if (pendingFiles.Length == 0)
        {
            return new FileStorageScanBatchResult(0, 0, 0);
        }

        var clean = 0;
        var malware = 0;
        var failed = 0;
        var scanner = CreateScanner(configuration);
        foreach (var file in pendingFiles)
        {
            var session = await dbContext.UploadSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.FileId == file.FileId, cancellationToken);
            if (session is null || !tusStoreAccessor.TryGet(out var store))
            {
                ApplyUnavailablePolicy(file, $"Uploaded bytes are unavailable for scanning. fileId={file.FileId}; uploadSessionId={session?.UploadSessionId ?? "<missing>"}.");
                failed++;
                continue;
            }

            try
            {
                await using var content = store.OpenRead(session.UploadSessionId);
                var result = await scanner.ScanAsync(content, cancellationToken);
                file.MarkScanned(result.ScanStatus, DateTimeOffset.UtcNow, result.Detail);
                if (result.ScanStatus == FileStorageScanPolicy.Malware)
                {
                    malware++;
                    await alertSink.PublishMalwareDetectedAsync(
                        new FileStorageSecurityAlertIntent(
                            file.OrganizationId,
                            file.EnvironmentId,
                            file.FileId,
                            file.FilePurpose,
                            file.FileName,
                            result.Detail),
                        cancellationToken);
                    logger.LogWarning(
                        "FileStorage malware scan blocked file {FileId} for {OrganizationId}/{EnvironmentId}: {ScanDetail}",
                        file.FileId,
                        file.OrganizationId,
                        file.EnvironmentId,
                        result.Detail);
                }
                else if (result.ScanStatus == FileStorageScanPolicy.Clean)
                {
                    clean++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ApplyUnavailablePolicy(file, $"Scanner unavailable: {ex.Message}");
                failed++;
                logger.LogError(ex, "FileStorage scan failed for file {FileId}.", file.FileId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new FileStorageScanBatchResult(clean, malware, failed);
    }

    private void ApplyUnavailablePolicy(Infrastructure.Records.StoredFileRecord file, string detail)
    {
        var status = string.Equals(
            configuration["FileStorage:Scanning:UnavailablePolicy"],
            "allow-with-warning",
            StringComparison.OrdinalIgnoreCase)
            ? FileStorageScanPolicy.Clean
            : FileStorageScanPolicy.Failed;
        file.MarkScanned(status, DateTimeOffset.UtcNow, detail);
    }

    private static IFileContentScanner CreateScanner(IConfiguration configuration)
    {
        return string.Equals(configuration["FileStorage:Scanning:Adapter"], "clamav", StringComparison.OrdinalIgnoreCase)
            ? new ClamAvFileContentScanner(configuration)
            : new LocalEicarFileContentScanner();
    }

    private static int ResolveBatchSize(IConfiguration configuration)
    {
        var batchSize = configuration.GetValue<int?>("FileStorage:Scanning:BatchSize");
        return batchSize is > 0 ? batchSize.Value : 25;
    }
}

public sealed class FileStorageScanHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<FileStorageScanHostedService> logger,
    IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan interval = ResolveInterval(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScanOnceAsync(stoppingToken);
        }
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<PostgreSqlFileStorageScanner>();
            var result = await scanner.ScanPendingFilesAsync(cancellationToken);
            if (result is { CleanFiles: 0, MalwareFiles: 0, FailedFiles: 0 })
            {
                return;
            }

            logger.LogInformation(
                "FileStorage scan batch completed: {CleanFiles} clean, {MalwareFiles} malware, {FailedFiles} failed.",
                result.CleanFiles,
                result.MalwareFiles,
                result.FailedFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FileStorage scan batch failed.");
        }
    }

    private static TimeSpan ResolveInterval(IConfiguration configuration)
    {
        var seconds = configuration.GetValue<double?>("FileStorage:Scanning:IntervalSeconds");
        return seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : TimeSpan.FromMinutes(1);
    }
}
