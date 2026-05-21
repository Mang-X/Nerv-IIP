using System.Collections.Concurrent;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using ContractOwnerReference = Nerv.IIP.Contracts.FileStorage.OwnerReference;
using DomainOwnerReference = Nerv.IIP.FileStorage.Domain.OwnerReference;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public interface IFileStorageService
{
    FileStorageResult<CreateUploadSessionResponse> CreateUploadSession(CreateUploadSessionRequest request);
    FileStorageResult<FileMetadataResponse> CompleteUploadSession(string uploadSessionId, CompleteUploadSessionRequest request);
    FileStorageResult<FileMetadataResponse> GetFileMetadata(string fileId);
    FileStorageResult<DownloadGrantResponse> CreateDownloadGrant(string fileId, CreateDownloadGrantRequest request);
}

public interface ILocalFileContentIndex
{
    bool TryGetUploadSessionIdForDownloadGrant(string downloadGrantId, out string uploadSessionId);
}

public interface ILocalTusUploadSessionIndex
{
    bool UploadSessionExists(string uploadSessionId);
}

public sealed class InMemoryFileStorageService : IFileStorageService, ILocalFileContentIndex, ILocalTusUploadSessionIndex
{
    private readonly ConcurrentDictionary<string, UploadSession> uploadSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileMetadata> files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> fileUploadSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> downloadGrantFiles = new(StringComparer.Ordinal);
    private readonly IFileStorageUploadProvider uploadProvider;

    public InMemoryFileStorageService()
        : this(new ServerProxyUploadProvider())
    {
    }

    public InMemoryFileStorageService(IFileStorageUploadProvider uploadProvider)
    {
        this.uploadProvider = uploadProvider;
    }

    public FileStorageResult<CreateUploadSessionResponse> CreateUploadSession(CreateUploadSessionRequest request)
    {
        if (!FilePurposePolicy.IsAllowed(request.FilePurpose))
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest($"Unsupported file purpose '{request.FilePurpose}'.");
        }

        if (!FileStorageRequestValidation.IsValidCreateUploadSessionRequest(request))
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest("Upload session request is invalid.");
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
            now.AddMinutes(15),
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

        return FileStorageResult<CreateUploadSessionResponse>.Ok(response);
    }

    public FileStorageResult<FileMetadataResponse> CompleteUploadSession(string uploadSessionId, CompleteUploadSessionRequest request)
    {
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

    public FileStorageResult<FileMetadataResponse> GetFileMetadata(string fileId)
    {
        return files.TryGetValue(fileId, out var file)
            ? FileStorageResult<FileMetadataResponse>.Ok(file.ToResponse())
            : FileStorageResult<FileMetadataResponse>.NotFound($"File '{fileId}' was not found.");
    }

    public FileStorageResult<DownloadGrantResponse> CreateDownloadGrant(string fileId, CreateDownloadGrantRequest request)
    {
        if (!files.TryGetValue(fileId, out var file))
        {
            return FileStorageResult<DownloadGrantResponse>.NotFound($"File '{fileId}' was not found.");
        }

        if (!string.Equals(file.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(file.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            return FileStorageResult<DownloadGrantResponse>.BadRequest("File context does not match.");
        }

        var grantId = NewId("dgr");
        downloadGrantFiles[grantId] = file.FileId;
        var response = new DownloadGrantResponse(
            file.FileId,
            DateTimeOffset.UtcNow.AddMinutes(10),
            new TransferInstructions(
                $"/api/files/v1/download-grants/{grantId}/content",
                new Dictionary<string, string>
                {
                    ["x-nerv-download-mode"] = ServerProxyUploadProvider.Name
                }));

        return FileStorageResult<DownloadGrantResponse>.Ok(response);
    }

    public bool TryGetUploadSessionIdForDownloadGrant(string downloadGrantId, out string uploadSessionId)
    {
        uploadSessionId = string.Empty;
        if (!downloadGrantFiles.TryGetValue(downloadGrantId, out var fileId)
            || !fileUploadSessions.TryGetValue(fileId, out var mappedUploadSessionId))
        {
            return false;
        }

        uploadSessionId = mappedUploadSessionId;
        return true;
    }

    public bool UploadSessionExists(string uploadSessionId)
    {
        return uploadSessions.ContainsKey(uploadSessionId);
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
