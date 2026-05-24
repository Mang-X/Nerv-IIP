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

    Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken);
}

public interface ILocalFileContentIndex
{
    Task<string?> GetUploadSessionIdForDownloadGrantAsync(string downloadGrantId, CancellationToken cancellationToken);
}

public interface ILocalTusUploadSessionIndex
{
    Task<bool> CanAcceptTusUploadAsync(string uploadSessionId, CancellationToken cancellationToken);
    Task<LocalTusUploadSession?> GetTusUploadSessionAsync(string uploadSessionId, CancellationToken cancellationToken);
}

public sealed record LocalTusUploadSession(
    string UploadSessionId,
    long ExpectedSizeBytes,
    string? Checksum,
    DateTimeOffset ExpiresAtUtc);

public sealed class InMemoryFileStorageService : IFileStorageService, ILocalFileContentIndex, ILocalTusUploadSessionIndex
{
    private readonly ConcurrentDictionary<string, UploadSession> uploadSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileMetadata> files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> fileUploadSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DownloadGrantIndexEntry> downloadGrantFiles = new(StringComparer.Ordinal);
    private readonly IFileStorageUploadProvider uploadProvider;
    private readonly ILocalTusFileStoreAccessor? tusStoreAccessor;
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

        var tusValidation = await ValidateTusCompletionAsync(session, request, cancellationToken);
        if (!tusValidation.IsValid)
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest(tusValidation.Message);
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
            "pending",
            "available",
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
        downloadGrantFiles[grantId] = new DownloadGrantIndexEntry(file.FileId, expiresAtUtc);
        var response = new DownloadGrantResponse(
            file.FileId,
            expiresAtUtc,
            new TransferInstructions(
                $"/api/files/v1/download-grants/{grantId}/content",
                new Dictionary<string, string>
                {
                    ["x-nerv-download-mode"] = ServerProxyUploadProvider.Name
                }));

        return Task.FromResult(FileStorageResult<DownloadGrantResponse>.Ok(response));
    }

    public Task<string?> GetUploadSessionIdForDownloadGrantAsync(
        string downloadGrantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!downloadGrantFiles.TryGetValue(downloadGrantId, out var grant)
            || grant.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || !fileUploadSessions.TryGetValue(grant.FileId, out var mappedUploadSessionId))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(mappedUploadSessionId);
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

    private async Task<(bool IsValid, string Message)> ValidateTusCompletionAsync(
        UploadSession session,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(session.Provider, TusUploadProvider.Name, StringComparison.Ordinal))
        {
            return (true, string.Empty);
        }

        if (tusStoreAccessor is null || !tusStoreAccessor.TryGet(out var store))
        {
            return (false, "Tus upload store is unavailable.");
        }

        var actualSize = store.GetOffset(session.UploadSessionId);
        if (actualSize != session.ExpectedSizeBytes || (request.SizeBytes is not null && request.SizeBytes != actualSize))
        {
            return (false, "Tus upload size does not match the upload session.");
        }

        var expectedChecksum = request.Checksum ?? session.Checksum;
        if (!string.IsNullOrWhiteSpace(expectedChecksum))
        {
            var actualChecksum = await store.ComputeSha256HexAsync(session.UploadSessionId, cancellationToken);
            if (!ChecksumMatchesSha256Hex(expectedChecksum, actualChecksum))
            {
                return (false, "Tus upload checksum does not match the upload session.");
            }
        }

        return (true, string.Empty);
    }

    private bool TryGetTusUploadSession(string uploadSessionId, out UploadSession session)
    {
        return uploadSessions.TryGetValue(uploadSessionId, out session!)
            && string.Equals(session.Provider, TusUploadProvider.Name, StringComparison.Ordinal)
            && !session.Completed;
    }

    private static bool ChecksumMatchesSha256Hex(string expectedChecksum, string actualSha256Hex)
    {
        var normalized = expectedChecksum.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? expectedChecksum["sha256:".Length..]
            : expectedChecksum;

        return string.Equals(normalized, actualSha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan ResolveUploadSessionTtl(IConfiguration? configuration)
    {
        var ttlSeconds = configuration?.GetValue<int?>("FileStorage:UploadSessionTtlSeconds");
        return ttlSeconds is > 0
            ? TimeSpan.FromSeconds(ttlSeconds.Value)
            : TimeSpan.FromMinutes(15);
    }

    private static string NewId(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string BuildObjectKey(string organizationId, string fileId)
    {
        return $"{organizationId}/{fileId}";
    }
}

internal sealed record DownloadGrantIndexEntry(string FileId, DateTimeOffset ExpiresAtUtc);

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
