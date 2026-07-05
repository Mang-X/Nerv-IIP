using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using ContractOwnerReference = Nerv.IIP.Contracts.FileStorage.OwnerReference;
using DomainOwnerReference = Nerv.IIP.FileStorage.Domain.OwnerReference;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public interface IFileStorageService
{
    Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileMetadataResponse>> CompleteUploadSessionAsync(
        string uploadSessionId,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileMetadataResponse>> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileListResponse>> ListFilesAsync(
        ListFilesRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<FileStorageUsageResponse>> GetUsageAsync(
        FileStorageUsageRequest request,
        CancellationToken cancellationToken);

    Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken);
}

public interface ILocalFileContentIndex
{
    Task<string?> GetUploadSessionIdForDownloadGrantAsync(
        string downloadGrantId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed class InMemoryFileStorageService : IFileStorageService, ILocalFileContentIndex, ILocalTusUploadSessionIndex
{
    private readonly ConcurrentDictionary<string, UploadSession> uploadSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileMetadata> files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> fileUploadSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DownloadGrantIndexEntry> downloadGrantFiles = new(StringComparer.Ordinal);
    private readonly IFileStorageUploadProvider uploadProvider;
    private readonly ILocalTusFileStoreAccessor? tusStoreAccessor;
    private readonly IConfiguration? configuration;
    private readonly TimeSpan uploadSessionTtl;

    public InMemoryFileStorageService()
        : this(new ServerProxyUploadProvider())
    {
    }

    public InMemoryFileStorageService(
        IFileStorageUploadProvider uploadProvider,
        IConfiguration? configuration = null,
        ILocalTusFileStoreAccessor? tusStoreAccessor = null)
    {
        this.uploadProvider = uploadProvider;
        this.tusStoreAccessor = tusStoreAccessor;
        this.configuration = configuration;
        uploadSessionTtl = ResolveUploadSessionTtl(configuration);
    }

    public Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!FilePurposePolicy.IsAllowed(request.FilePurpose))
        {
            return Task.FromResult(FileStorageResult<CreateUploadSessionResponse>.BadRequest($"Unsupported file purpose '{request.FilePurpose}'."));
        }

        if (!FileStorageRequestValidation.IsValidCreateUploadSessionRequest(request))
        {
            return Task.FromResult(FileStorageResult<CreateUploadSessionResponse>.BadRequest("Upload session request is invalid."));
        }

        var declaredType = FileStoragePurposePolicies.ValidateDeclaredType(
            request.FilePurpose,
            request.FileName,
            request.ContentType,
            configuration);
        if (!declaredType.IsAllowed)
        {
            return Task.FromResult(FileStorageResult<CreateUploadSessionResponse>.BadRequest(declaredType.Message!));
        }

        var quota = FileStoragePurposePolicies.CheckQuota(
            request.OrganizationId,
            request.EnvironmentId,
            request.FilePurpose,
            request.ExpectedSizeBytes,
            CalculateUsedBytes(request.OrganizationId, request.EnvironmentId, request.FilePurpose),
            configuration);
        if (!quota.IsAllowed)
        {
            return Task.FromResult(FileStorageResult<CreateUploadSessionResponse>.Conflict("File storage quota would be exceeded."));
        }

        var now = DateTimeOffset.UtcNow;
        var uploadSessionId = NewId("ups");
        var fileId = NewId("file");
        var session = new UploadSession(
            uploadSessionId,
            fileId,
            request.OrganizationId,
            request.EnvironmentId,
            new DomainOwnerReference(request.Owner.OwnerService, request.Owner.OwnerType, request.Owner.OwnerId),
            request.FilePurpose,
            request.FileName,
            request.ContentType,
            request.ExpectedSizeBytes,
            request.Checksum,
            uploadProvider.Provider,
            now,
            now.Add(uploadSessionTtl),
            Completed: false);

        uploadSessions[session.UploadSessionId] = session;
        var upload = uploadProvider.CreateUploadInstructions(session.UploadSessionId, session.FileId);

        var response = new CreateUploadSessionResponse(
            session.UploadSessionId,
            session.FileId,
            uploadProvider.UploadMode,
            uploadProvider.Provider,
            session.ExpiresAtUtc,
            upload);

        return Task.FromResult(FileStorageResult<CreateUploadSessionResponse>.Ok(response));
    }

    public async Task<FileStorageResult<FileMetadataResponse>> CompleteUploadSessionAsync(
        string uploadSessionId,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!uploadSessions.TryGetValue(uploadSessionId, out var session))
        {
            return FileStorageResult<FileMetadataResponse>.NotFound($"Upload session '{uploadSessionId}' was not found.");
        }

        if (session.Completed)
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Upload session is already completed.");
        }

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Upload session has expired.");
        }

        if (!string.Equals(session.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(session.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(session.FilePurpose, request.FilePurpose, StringComparison.Ordinal))
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Upload session context does not match.");
        }

        var tusValidation = await TusUploadCompletionValidator.ValidateAsync(
            session.Provider,
            session.UploadSessionId,
            session.ExpectedSizeBytes,
            session.Checksum,
            request,
            tusStoreAccessor,
            cancellationToken);
        if (tusValidation is not null)
        {
            return FileStorageResult<FileMetadataResponse>.Failure(tusValidation.StatusCode, tusValidation.Message);
        }

        if (!await FileStoragePurposePolicies.MatchesDeclaredContentAsync(
                session.FileName,
                session.ContentType,
                session.Provider,
                session.UploadSessionId,
                tusStoreAccessor,
                cancellationToken))
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Uploaded content does not match the declared file type.");
        }

        var completedSession = session with { Completed = true };
        if (!uploadSessions.TryUpdate(uploadSessionId, completedSession, session))
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Upload session could not be completed.");
        }

        var now = DateTimeOffset.UtcNow;
        var file = new FileMetadata(
            session.FileId,
            session.OrganizationId,
            session.EnvironmentId,
            session.Owner,
            session.FilePurpose,
            session.FileName,
            session.ContentType,
            session.ExpectedSizeBytes,
            session.Checksum,
            BuildObjectKey(session.OrganizationId, session.FileId),
            FileStorageScanPolicy.InitialScanStatus(configuration),
            FileStorageScanPolicy.Available,
            session.CreatedAtUtc,
            now);

        files[file.FileId] = file;
        fileUploadSessions[file.FileId] = uploadSessionId;
        return FileStorageResult<FileMetadataResponse>.Ok(file.ToResponse());
    }

    public Task<FileStorageResult<FileMetadataResponse>> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = files.TryGetValue(fileId, out var file)
            ? FileStorageResult<FileMetadataResponse>.Ok(file.ToResponse())
            : FileStorageResult<FileMetadataResponse>.NotFound($"File '{fileId}' was not found.");

        return Task.FromResult(result);
    }

    public Task<FileStorageResult<FileListResponse>> ListFilesAsync(
        ListFilesRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.OrganizationId) || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return Task.FromResult(FileStorageResult<FileListResponse>.BadRequest("OrganizationId and EnvironmentId are required."));
        }

        var skip = NormalizeSkip(request.Skip);
        var take = NormalizeTake(request.Take);
        var query = files.Values
            .Where(file =>
                string.Equals(file.OrganizationId, request.OrganizationId, StringComparison.Ordinal) &&
                string.Equals(file.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal));
        query = ApplyFileFilters(
            query,
            request.FilePurpose,
            request.UploaderId,
            request.CreatedFromUtc,
            request.CreatedToUtc,
            request.Status);

        var ordered = query
            .OrderByDescending(file => file.CompletedAtUtc)
            .ThenBy(file => file.FileId, StringComparer.Ordinal)
            .ToArray();
        var response = new FileListResponse(
            ordered.Length,
            ordered
                .Skip(skip)
                .Take(take)
                .Select(file => file.ToResponse())
                .ToArray());

        return Task.FromResult(FileStorageResult<FileListResponse>.Ok(response));
    }

    public Task<FileStorageResult<FileStorageUsageResponse>> GetUsageAsync(
        FileStorageUsageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.OrganizationId) || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return Task.FromResult(FileStorageResult<FileStorageUsageResponse>.BadRequest("OrganizationId and EnvironmentId are required."));
        }

        var usedBytes = CalculateUsedBytes(request.OrganizationId, request.EnvironmentId, request.FilePurpose);
        var quota = request.FilePurpose is null
            ? FileStoragePurposePolicies.CheckQuota(request.OrganizationId, request.EnvironmentId, string.Empty, 0, usedBytes, configuration).MaxBytes
            : FileStoragePurposePolicies.CheckQuota(request.OrganizationId, request.EnvironmentId, request.FilePurpose, 0, usedBytes, configuration).MaxBytes;
        return Task.FromResult(FileStorageResult<FileStorageUsageResponse>.Ok(new FileStorageUsageResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.FilePurpose,
            usedBytes,
            quota)));
    }

    public Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!files.TryGetValue(fileId, out var file))
        {
            return Task.FromResult(FileStorageResult<DownloadGrantResponse>.NotFound($"File '{fileId}' was not found."));
        }

        if (!string.Equals(file.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(file.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            return Task.FromResult(FileStorageResult<DownloadGrantResponse>.BadRequest("File context does not match."));
        }

        var grantId = NewId("dgr");
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        downloadGrantFiles[grantId] = new DownloadGrantIndexEntry(file.FileId, file.OrganizationId, file.EnvironmentId, expiresAtUtc);
        var response = new DownloadGrantResponse(
            file.FileId,
            expiresAtUtc,
            new TransferInstructions(
                $"/api/files/v1/download-grants/{grantId}/content",
                new Dictionary<string, string>
                {
                    ["x-nerv-download-mode"] = ServerProxyUploadProvider.Name,
                    [FileStorageTransferHeaders.OrganizationId] = file.OrganizationId,
                    [FileStorageTransferHeaders.EnvironmentId] = file.EnvironmentId
                }));

        return Task.FromResult(FileStorageResult<DownloadGrantResponse>.Ok(response));
    }

    public Task<string?> GetUploadSessionIdForDownloadGrantAsync(
        string downloadGrantId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!downloadGrantFiles.TryGetValue(downloadGrantId, out var grant)
            || grant.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || !string.Equals(grant.OrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(grant.EnvironmentId, environmentId, StringComparison.Ordinal)
            || !files.TryGetValue(grant.FileId, out var file)
            || !FileStorageScanPolicy.CanDownload(file.ScanStatus, file.Status, configuration)
            || !fileUploadSessions.TryGetValue(grant.FileId, out var mappedUploadSessionId))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(
            downloadGrantFiles.TryRemove(downloadGrantId, out _)
                ? mappedUploadSessionId
                : null);
    }

    public Task<bool> CanAcceptTusUploadAsync(string uploadSessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var canAccept = TryGetTusUploadSession(uploadSessionId, out var session)
            && session.ExpiresAtUtc > DateTimeOffset.UtcNow;

        return Task.FromResult(canAccept);
    }

    public Task<LocalTusUploadSession?> GetTusUploadSessionAsync(
        string uploadSessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localSession = TryGetTusUploadSession(uploadSessionId, out var session)
            ? new LocalTusUploadSession(
                session.UploadSessionId,
                session.ExpectedSizeBytes,
                session.Checksum,
                session.ExpiresAtUtc)
            : null;

        return Task.FromResult(localSession);
    }

    private bool TryGetTusUploadSession(string uploadSessionId, out UploadSession session)
    {
        return uploadSessions.TryGetValue(uploadSessionId, out session!)
            && string.Equals(session.Provider, TusUploadProvider.Name, StringComparison.Ordinal)
            && !session.Completed;
    }

    private long CalculateUsedBytes(string organizationId, string environmentId, string? filePurpose)
    {
        var storedBytes = files.Values
            .Where(file => string.Equals(file.OrganizationId, organizationId, StringComparison.Ordinal)
                && string.Equals(file.EnvironmentId, environmentId, StringComparison.Ordinal)
                && !string.Equals(file.Status, "deleted", StringComparison.Ordinal)
                && (filePurpose is null || string.Equals(file.FilePurpose, filePurpose, StringComparison.Ordinal)))
            .Sum(file => file.SizeBytes);

        var now = DateTimeOffset.UtcNow;
        var reservedBytes = uploadSessions.Values
            .Where(session => !session.Completed
                && session.ExpiresAtUtc > now
                && string.Equals(session.OrganizationId, organizationId, StringComparison.Ordinal)
                && string.Equals(session.EnvironmentId, environmentId, StringComparison.Ordinal)
                && (filePurpose is null || string.Equals(session.FilePurpose, filePurpose, StringComparison.Ordinal)))
            .Sum(session => session.ExpectedSizeBytes);

        return storedBytes + reservedBytes;
    }

    private static TimeSpan ResolveUploadSessionTtl(IConfiguration? configuration)
    {
        var ttlSeconds = configuration?.GetValue<double?>("FileStorage:UploadSessionTtlSeconds");
        return ttlSeconds is > 0
            ? TimeSpan.FromSeconds(ttlSeconds.Value)
            : TimeSpan.FromMinutes(15);
    }

    private static string NewId(string prefix)
    {
        return $"{prefix}_{Guid.CreateVersion7():N}";
    }

    private static string BuildObjectKey(string organizationId, string fileId)
    {
        return $"{organizationId}/{fileId}";
    }

    internal static IEnumerable<FileMetadata> ApplyFileFilters(
        IEnumerable<FileMetadata> query,
        string? filePurpose,
        string? uploaderId,
        DateTimeOffset? createdFromUtc,
        DateTimeOffset? createdToUtc,
        string? status)
    {
        if (!string.IsNullOrWhiteSpace(filePurpose))
        {
            query = query.Where(file => string.Equals(file.FilePurpose, filePurpose, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(uploaderId))
        {
            query = query.Where(file => string.Equals(file.Owner.OwnerId, uploaderId, StringComparison.Ordinal));
        }

        if (createdFromUtc is not null)
        {
            query = query.Where(file => file.CreatedAtUtc >= createdFromUtc.Value);
        }

        if (createdToUtc is not null)
        {
            query = query.Where(file => file.CreatedAtUtc <= createdToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(file => string.Equals(file.Status, status, StringComparison.Ordinal));
        }

        return query;
    }

    internal static int NormalizeSkip(int? skip) => skip is > 0 ? skip.Value : 0;

    internal static int NormalizeTake(int? take) => take is > 0 ? Math.Min(take.Value, 200) : 50;
}

internal sealed record DownloadGrantIndexEntry(
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset ExpiresAtUtc);

internal static class FileStorageRequestValidation
{
    public static bool IsValidCreateUploadSessionRequest(CreateUploadSessionRequest request)
    {
        return request.Owner is not null
            && !HasBlankRequiredValue(
                request.OrganizationId,
                request.EnvironmentId,
                request.Owner.OwnerService,
                request.Owner.OwnerType,
                request.Owner.OwnerId,
                request.FileName,
                request.ContentType)
            && IsWithinMaxLength(request.OrganizationId, 128)
            && IsWithinMaxLength(request.EnvironmentId, 128)
            && IsWithinMaxLength(request.Owner.OwnerService, 128)
            && IsWithinMaxLength(request.Owner.OwnerType, 128)
            && IsWithinMaxLength(request.Owner.OwnerId, 128)
            && IsWithinMaxLength(request.FilePurpose, 128)
            && IsWithinMaxLength(request.FileName, 512)
            && IsWithinMaxLength(request.ContentType, 256)
            && IsWithinMaxLength(request.Checksum, 256)
            && request.ExpectedSizeBytes >= 0;
    }

    private static bool HasBlankRequiredValue(params string[] values)
    {
        return values.Any(string.IsNullOrWhiteSpace);
    }

    private static bool IsWithinMaxLength(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength;
    }
}

public sealed record FileStorageError(string Message);

public sealed record FileStorageResult<T>(T? Value, FileStorageError? Error, int StatusCode)
{
    public static FileStorageResult<T> Ok(T value) => new(value, null, StatusCodes.Status200OK);
    public static FileStorageResult<T> BadRequest(string message) => new(default, new FileStorageError(message), StatusCodes.Status400BadRequest);
    public static FileStorageResult<T> NotFound(string message) => new(default, new FileStorageError(message), StatusCodes.Status404NotFound);
    public static FileStorageResult<T> Conflict(string message) => new(default, new FileStorageError(message), StatusCodes.Status409Conflict);
    public static FileStorageResult<T> ServiceUnavailable(string message) => new(default, new FileStorageError(message), StatusCodes.Status503ServiceUnavailable);
    internal static FileStorageResult<T> Failure(int statusCode, string message) => new(default, new FileStorageError(message), statusCode);
}

internal static class FileMetadataMapping
{
    public static FileMetadataResponse ToResponse(this FileMetadata file)
    {
        return new FileMetadataResponse(
            file.FileId,
            file.OrganizationId,
            file.EnvironmentId,
            new ContractOwnerReference(file.Owner.OwnerService, file.Owner.OwnerType, file.Owner.OwnerId),
            file.FilePurpose,
            file.FileName,
            file.ContentType,
            file.SizeBytes,
            file.Checksum,
            file.ScanStatus,
            file.Status,
            file.CreatedAtUtc,
            file.CompletedAtUtc);
    }
}
