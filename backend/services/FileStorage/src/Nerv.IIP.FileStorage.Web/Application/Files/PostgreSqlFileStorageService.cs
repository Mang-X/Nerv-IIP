using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using ContractOwnerReference = Nerv.IIP.Contracts.FileStorage.OwnerReference;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed class PostgreSqlFileStorageService : IFileStorageService, ILocalFileContentIndex, ILocalTusUploadSessionIndex
{
    private readonly ApplicationDbContext dbContext;
    private readonly IFileStorageUploadProvider uploadProvider;
    private readonly ILocalTusFileStoreAccessor? tusStoreAccessor;

    public PostgreSqlFileStorageService(ApplicationDbContext dbContext)
        : this(dbContext, new ServerProxyUploadProvider())
    {
    }

    public PostgreSqlFileStorageService(
        ApplicationDbContext dbContext,
        IFileStorageUploadProvider uploadProvider,
        ILocalTusFileStoreAccessor? tusStoreAccessor = null)
    {
        this.dbContext = dbContext;
        this.uploadProvider = uploadProvider;
        this.tusStoreAccessor = tusStoreAccessor;
    }

    public async Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken)
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
        var session = UploadSessionRecord.Create(
            uploadSessionId,
            fileId,
            request.OrganizationId,
            request.EnvironmentId,
            request.Owner.OwnerService,
            request.Owner.OwnerType,
            request.Owner.OwnerId,
            request.FilePurpose,
            request.FileName,
            request.ContentType,
            request.ExpectedSizeBytes,
            request.Checksum,
            BuildObjectKey(request.OrganizationId, fileId),
            uploadProvider.Provider,
            now,
            now.AddMinutes(15));

        dbContext.UploadSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        var upload = uploadProvider.CreateUploadInstructions(session.UploadSessionId, session.FileId);

        return FileStorageResult<CreateUploadSessionResponse>.Ok(new CreateUploadSessionResponse(
            session.UploadSessionId,
            session.FileId,
            uploadProvider.UploadMode,
            uploadProvider.Provider,
            session.ExpiresAtUtc,
            upload));
    }

    public async Task<FileStorageResult<FileMetadataResponse>> CompleteUploadSessionAsync(
        string uploadSessionId,
        CompleteUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.UploadSessions.SingleOrDefaultAsync(
            x => x.UploadSessionId == uploadSessionId,
            cancellationToken);
        if (session is null)
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

        var now = DateTimeOffset.UtcNow;
        session.MarkCompleted(now);
        var file = StoredFileRecord.Create(
            session.FileId,
            session.OrganizationId,
            session.EnvironmentId,
            session.OwnerService,
            session.OwnerType,
            session.OwnerId,
            session.FilePurpose,
            session.FileName,
            session.ContentType,
            session.ExpectedSizeBytes,
            session.Checksum,
            session.ObjectKey,
            "pending",
            "available",
            session.CreatedAtUtc,
            now);

        dbContext.StoredFiles.Add(file);
        await dbContext.SaveChangesAsync(cancellationToken);

        return FileStorageResult<FileMetadataResponse>.Ok(ToResponse(file));
    }

    public async Task<FileStorageResult<FileMetadataResponse>> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.StoredFiles.SingleOrDefaultAsync(x => x.FileId == fileId, cancellationToken);
        return file is null
            ? FileStorageResult<FileMetadataResponse>.NotFound($"File '{fileId}' was not found.")
            : FileStorageResult<FileMetadataResponse>.Ok(ToResponse(file));
    }

    public async Task<FileStorageResult<DownloadGrantResponse>> CreateDownloadGrantAsync(
        string fileId,
        CreateDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.StoredFiles.SingleOrDefaultAsync(x => x.FileId == fileId, cancellationToken);
        if (file is null)
        {
            return FileStorageResult<DownloadGrantResponse>.NotFound($"File '{fileId}' was not found.");
        }

        if (!string.Equals(file.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(file.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal))
        {
            return FileStorageResult<DownloadGrantResponse>.BadRequest("File context does not match.");
        }

        var now = DateTimeOffset.UtcNow;
        var grant = DownloadGrantRecord.Create(
            NewId("dgr"),
            file.FileId,
            file.OrganizationId,
            file.EnvironmentId,
            ServerProxyUploadProvider.Name,
            now,
            now.AddMinutes(10));

        dbContext.DownloadGrants.Add(grant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return FileStorageResult<DownloadGrantResponse>.Ok(new DownloadGrantResponse(
            file.FileId,
            grant.ExpiresAtUtc,
            new TransferInstructions(
                $"/api/files/v1/download-grants/{grant.DownloadGrantId}/content",
                new Dictionary<string, string>
                {
                    ["x-nerv-download-mode"] = ServerProxyUploadProvider.Name
                })));
    }

    public async Task<string?> GetUploadSessionIdForDownloadGrantAsync(
        string downloadGrantId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var grant = await dbContext.DownloadGrants.SingleOrDefaultAsync(x =>
            x.DownloadGrantId == downloadGrantId
            && x.ExpiresAtUtc > now,
            cancellationToken);
        if (grant is null)
        {
            return null;
        }

        var session = await dbContext.UploadSessions.SingleOrDefaultAsync(
            x => x.FileId == grant.FileId,
            cancellationToken);
        if (session is null)
        {
            return null;
        }

        return session.UploadSessionId;
    }

    public Task<bool> CanAcceptTusUploadAsync(string uploadSessionId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return dbContext.UploadSessions.AnyAsync(x =>
            x.UploadSessionId == uploadSessionId
            && x.Provider == TusUploadProvider.Name
            && !x.Completed
            && x.ExpiresAtUtc > now,
            cancellationToken);
    }

    public async Task<LocalTusUploadSession?> GetTusUploadSessionAsync(
        string uploadSessionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.UploadSessions
            .Where(x => x.UploadSessionId == uploadSessionId
                && x.Provider == TusUploadProvider.Name
                && !x.Completed)
            .Select(x => new LocalTusUploadSession(
                x.UploadSessionId,
                x.ExpectedSizeBytes,
                x.Checksum,
                x.ExpiresAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<(bool IsValid, string Message)> ValidateTusCompletionAsync(
        UploadSessionRecord session,
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

    private static bool ChecksumMatchesSha256Hex(string expectedChecksum, string actualSha256Hex)
    {
        var normalized = expectedChecksum.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? expectedChecksum["sha256:".Length..]
            : expectedChecksum;

        return string.Equals(normalized, actualSha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    private static FileMetadataResponse ToResponse(StoredFileRecord file)
    {
        return new FileMetadataResponse(
            file.FileId,
            file.OrganizationId,
            file.EnvironmentId,
            new ContractOwnerReference(file.OwnerService, file.OwnerType, file.OwnerId),
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

    private static string NewId(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string BuildObjectKey(string organizationId, string fileId)
    {
        return $"{organizationId}/{fileId}";
    }
}
