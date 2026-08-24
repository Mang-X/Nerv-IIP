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
    private readonly IConfiguration? configuration;
    private readonly TimeProvider timeProvider;
    private readonly IUploadCommitStorage commitStorage;
    private readonly UploadSessionGateRegistry gateRegistry;

    public PostgreSqlFileStorageService(ApplicationDbContext dbContext, IConfiguration configuration)
        : this(dbContext, new ServerProxyUploadProvider(), configuration: configuration)
    {
    }

    public PostgreSqlFileStorageService(
        ApplicationDbContext dbContext,
        IFileStorageUploadProvider uploadProvider,
        ILocalTusFileStoreAccessor? tusStoreAccessor = null,
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null,
        IUploadCommitStorage? commitStorage = null,
        UploadSessionGateRegistry? gateRegistry = null)
    {
        this.dbContext = dbContext;
        this.uploadProvider = uploadProvider;
        this.tusStoreAccessor = tusStoreAccessor;
        this.configuration = configuration;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.commitStorage = commitStorage ?? new UnavailableUploadCommitStorage();
        this.gateRegistry = gateRegistry ?? new UploadSessionGateRegistry();
    }

    public async Task<FileStorageResult<CreateUploadSessionResponse>> CreateUploadSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        var purposeRegistration = FileStoragePurposePolicies.ResolveRegistration(request.FilePurpose, configuration);
        if (!purposeRegistration.IsRegistered)
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest(
                purposeRegistration.Message!,
                purposeRegistration.ErrorCode!);
        }

        if (!FileStorageRequestValidation.IsValidCreateUploadSessionRequest(request))
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest("Upload session request is invalid.");
        }

        var expectedSize = FileStoragePurposePolicies.ValidateExpectedSize(
            request.FilePurpose,
            request.ExpectedSizeBytes,
            configuration);
        if (!expectedSize.IsAllowed)
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest(expectedSize.Message!);
        }

        var owner = FileStoragePurposePolicies.ValidateOwner(
            request.FilePurpose,
            request.Owner.OwnerService,
            request.Owner.OwnerType,
            configuration);
        if (!owner.IsAllowed)
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest(owner.Message!);
        }

        var checksum = FileStoragePurposePolicies.ValidateChecksum(
            request.FilePurpose,
            request.Checksum,
            configuration);
        if (!checksum.IsAllowed)
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest(checksum.Message!);
        }

        var declaredType = FileStoragePurposePolicies.ValidateDeclaredType(
            request.FilePurpose,
            request.FileName,
            request.ContentType,
            configuration);
        if (!declaredType.IsAllowed)
        {
            return FileStorageResult<CreateUploadSessionResponse>.BadRequest(declaredType.Message!);
        }

        var quotaPolicy = FileStoragePurposePolicies.ResolveQuotaPolicy(
            request.OrganizationId,
            request.EnvironmentId,
            request.FilePurpose,
            configuration);
        var quotaLock = FileStoragePurposePolicies.GetQuotaReservationLock(
            request.OrganizationId,
            request.EnvironmentId,
            request.FilePurpose,
            quotaPolicy.Scope);
        await quotaLock.WaitAsync(cancellationToken);
        UploadSessionRecord session;
        try
        {
            var usedBytes = await CalculateUsedBytesAsync(
                request.OrganizationId,
                request.EnvironmentId,
                quotaPolicy.Scope == FileStorageQuotaScope.Organization ? null : request.FilePurpose,
                cancellationToken);
            var quota = FileStoragePurposePolicies.CheckQuota(
                request.OrganizationId,
                request.EnvironmentId,
                request.FilePurpose,
                request.ExpectedSizeBytes,
                usedBytes,
                configuration);
            if (!quota.IsAllowed)
            {
                return FileStorageResult<CreateUploadSessionResponse>.Conflict("File storage quota would be exceeded.");
            }

            var now = timeProvider.GetUtcNow();
            var uploadSessionId = NewId("ups");
            var fileId = NewId("file");
            session = UploadSessionRecord.Create(
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
        }
        finally
        {
            quotaLock.Release();
        }

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
        await using var executionGate = await gateRegistry.EnterCommitExecutionAsync(uploadSessionId, cancellationToken);
        var session = await dbContext.UploadSessions.SingleOrDefaultAsync(x => x.UploadSessionId == uploadSessionId, cancellationToken);
        if (session is null)
        {
            return FileStorageResult<FileMetadataResponse>.NotFound($"Upload session '{uploadSessionId}' was not found.");
        }

        if (!string.Equals(session.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(session.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(session.FilePurpose, request.FilePurpose, StringComparison.Ordinal))
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Upload session context does not match.");
        }

        if (session.Completed)
        {
            var completedFile = await dbContext.StoredFiles.SingleOrDefaultAsync(x => x.FileId == session.FileId, cancellationToken);
            return completedFile is null
                ? FileStorageResult<FileMetadataResponse>.Failure(
                    StatusCodes.Status503ServiceUnavailable,
                    "Completed upload metadata is temporarily unavailable.")
                : FileStorageResult<FileMetadataResponse>.Ok(ToResponse(completedFile));
        }

        if (string.Equals(session.State, UploadSessionState.Open, StringComparison.Ordinal)
            && session.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest("Upload session has expired.");
        }

        var completionChecksum = FileStoragePurposePolicies.ValidateCompletionChecksum(
            session.FilePurpose,
            session.Checksum,
            request.Checksum,
            configuration);
        if (!completionChecksum.IsAllowed)
        {
            return FileStorageResult<FileMetadataResponse>.BadRequest(completionChecksum.Message!);
        }

        var now = timeProvider.GetUtcNow();
        if (string.Equals(session.State, UploadSessionState.Open, StringComparison.Ordinal))
        {
            await using var patchGate = await gateRegistry.EnterPatchCommitAsync(uploadSessionId, cancellationToken);
            await dbContext.Entry(session).ReloadAsync(cancellationToken);
            if (!string.Equals(session.State, UploadSessionState.Open, StringComparison.Ordinal))
            {
                return FileStorageResult<FileMetadataResponse>.Failure(
                    StatusCodes.Status409Conflict,
                    "Upload completion is already in progress; retry later.");
            }

            session.BeginCommit(
                NewId("cmt"),
                NormalizeCanonicalChecksum(session.Checksum) ?? NormalizeCanonicalChecksum(request.Checksum),
                now);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken); // Tx1 is durably committed before any storage I/O.
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                session = await dbContext.UploadSessions.SingleOrDefaultAsync(
                    x => x.UploadSessionId == uploadSessionId,
                    cancellationToken);
                if (session is null
                    || !string.Equals(session.State, UploadSessionState.Committing, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(session.CommitId))
                {
                    return FileStorageResult<FileMetadataResponse>.Failure(
                        StatusCodes.Status409Conflict,
                        "Upload completion ownership changed concurrently; retry later.");
                }
            }
        }

        if (!string.Equals(session.State, UploadSessionState.Committing, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(session.CommitId))
        {
            return FileStorageResult<FileMetadataResponse>.Failure(
                StatusCodes.Status409Conflict,
                "Upload session cannot enter the committing state.");
        }

        var executionOwnerId = NewId("wrk");
        if (!await TryClaimCommitExecutionAsync(session, executionOwnerId, cancellationToken))
        {
            return FileStorageResult<FileMetadataResponse>.Failure(
                StatusCodes.Status409Conflict,
                "Upload completion is being recovered by another worker; retry later.");
        }

        if (session.StorageActionStartedAtUtc is null)
        {
            session.MarkStorageActionStarted(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        UploadCommitStorageResult storageResult;
        try
        {
            storageResult = await commitStorage.CommitAsync(ToCommitIntent(session), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            storageResult = UploadCommitStorageResult.RetryableUnavailable();
        }

        if (!storageResult.IsVerified
            && storageResult.FailureDisposition == UploadCommitFailureDisposition.ProvenNoFinalActionStarted)
        {
            session.ReopenAfterStorageProvedNotStarted();
            await dbContext.SaveChangesAsync(cancellationToken);
            return FileStorageResult<FileMetadataResponse>.Failure(
                storageResult.StatusCode is >= 400 and <= 599
                    ? storageResult.StatusCode
                    : StatusCodes.Status503ServiceUnavailable,
                string.IsNullOrWhiteSpace(storageResult.Message)
                    ? "Final storage action was not started; the upload session is open for retry."
                    : storageResult.Message);
        }

        if (!storageResult.IsVerified
            || storageResult.SizeBytes != session.ExpectedSizeBytes
            || NormalizeCanonicalChecksum(storageResult.CanonicalChecksum) is not { } canonicalChecksum
            || (session.CommitChecksum is not null
                && !string.Equals(session.CommitChecksum, canonicalChecksum, StringComparison.Ordinal)))
        {
            var errorCode = string.IsNullOrWhiteSpace(storageResult.ErrorCode)
                ? "invalid-final-evidence"
                : storageResult.ErrorCode;
            session.RecordRecoveryFailure(errorCode, NextRecoveryAtUtc(session.RecoveryAttemptCount, timeProvider.GetUtcNow()));
            await dbContext.SaveChangesAsync(cancellationToken);
            return FileStorageResult<FileMetadataResponse>.Failure(
                storageResult.StatusCode is >= 400 and <= 599
                    ? storageResult.StatusCode
                    : StatusCodes.Status503ServiceUnavailable,
                string.IsNullOrWhiteSpace(storageResult.Message)
                    ? "Final storage evidence is invalid; retry later."
                    : storageResult.Message);
        }

        var existingFile = await dbContext.StoredFiles.SingleOrDefaultAsync(x => x.FileId == session.FileId, cancellationToken);
        if (existingFile is not null)
        {
            session.MarkCompleted(existingFile.CompletedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return FileStorageResult<FileMetadataResponse>.Ok(ToResponse(existingFile));
        }

        var completedAtUtc = timeProvider.GetUtcNow();
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
            storageResult.SizeBytes,
            canonicalChecksum,
            session.ObjectKey,
            FileStorageFileStatus.Available,
            session.CreatedAtUtc,
            completedAtUtc);

        dbContext.StoredFiles.Add(file);
        session.MarkCompleted(completedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken); // Tx2 atomically commits metadata and completed state.

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

    public async Task<FileStorageResult<FileListResponse>> ListFilesAsync(
        ListFilesRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId) || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return FileStorageResult<FileListResponse>.BadRequest("OrganizationId and EnvironmentId are required.");
        }

        var skip = FileStorageRequestValidation.NormalizeSkip(request.Skip);
        var take = FileStorageRequestValidation.NormalizeTake(request.Take);
        var query = dbContext.StoredFiles
            .AsNoTracking()
            .Where(file => file.OrganizationId == request.OrganizationId && file.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.FilePurpose))
        {
            query = query.Where(file => file.FilePurpose == request.FilePurpose);
        }

        if (!string.IsNullOrWhiteSpace(request.UploaderId))
        {
            query = query.Where(file => file.OwnerId == request.UploaderId);
        }

        if (request.CreatedFromUtc is not null)
        {
            query = query.Where(file => file.CreatedAtUtc >= request.CreatedFromUtc.Value);
        }

        if (request.CreatedToUtc is not null)
        {
            query = query.Where(file => file.CreatedAtUtc <= request.CreatedToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(file => file.Status == request.Status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(file => file.CompletedAtUtc)
            .ThenBy(file => file.FileId)
            .Skip(skip)
            .Take(take)
            .Select(file => ToResponse(file))
            .ToArrayAsync(cancellationToken);

        return FileStorageResult<FileListResponse>.Ok(new FileListResponse(total, items));
    }

    public async Task<FileStorageResult<FileStorageUsageResponse>> GetUsageAsync(
        FileStorageUsageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId) || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return FileStorageResult<FileStorageUsageResponse>.BadRequest("OrganizationId and EnvironmentId are required.");
        }

        var quotaPurpose = request.FilePurpose ?? string.Empty;
        var quotaPolicy = FileStoragePurposePolicies.ResolveQuotaPolicy(
            request.OrganizationId,
            request.EnvironmentId,
            quotaPurpose,
            configuration);
        var usedBytes = await CalculateUsedBytesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            quotaPolicy.Scope == FileStorageQuotaScope.Organization ? null : request.FilePurpose,
            cancellationToken);
        var quota = FileStoragePurposePolicies.CheckQuota(
            request.OrganizationId,
            request.EnvironmentId,
            quotaPurpose,
            0,
            usedBytes,
            configuration).MaxBytes;

        return FileStorageResult<FileStorageUsageResponse>.Ok(new FileStorageUsageResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.FilePurpose,
            usedBytes,
            quota));
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

        var now = timeProvider.GetUtcNow();
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
                    ["x-nerv-download-mode"] = ServerProxyUploadProvider.Name,
                    [FileStorageTransferHeaders.OrganizationId] = file.OrganizationId,
                    [FileStorageTransferHeaders.EnvironmentId] = file.EnvironmentId
                })));
    }

    public async Task<string?> GetUploadSessionIdForDownloadGrantAsync(
        string downloadGrantId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var grant = await dbContext.DownloadGrants.SingleOrDefaultAsync(x =>
            x.DownloadGrantId == downloadGrantId
            && x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.ExpiresAtUtc > now,
            cancellationToken);
        if (grant is null)
        {
            return null;
        }

        var file = await dbContext.StoredFiles.SingleOrDefaultAsync(
            x => x.FileId == grant.FileId,
            cancellationToken);
        if (file is null
            || !string.Equals(file.Status, FileStorageFileStatus.Available, StringComparison.Ordinal))
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

        var consumed = dbContext.Database.IsRelational()
            ? await dbContext.DownloadGrants
                .Where(x => x.DownloadGrantId == downloadGrantId
                    && x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.ExpiresAtUtc > now)
                .ExecuteDeleteAsync(cancellationToken)
            : await ConsumeGrantForNonRelationalTestStoreAsync(grant, cancellationToken);
        return consumed == 1 ? session.UploadSessionId : null;
    }

    private async Task<int> ConsumeGrantForNonRelationalTestStoreAsync(
        DownloadGrantRecord grant,
        CancellationToken cancellationToken)
    {
        dbContext.DownloadGrants.Remove(grant);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return 1;
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0;
        }
    }

    public Task<bool> CanAcceptTusUploadAsync(string uploadSessionId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return dbContext.UploadSessions.AnyAsync(x =>
            x.UploadSessionId == uploadSessionId
            && x.Provider == TusUploadProvider.Name
            && x.State == UploadSessionState.Open
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
                && x.State == UploadSessionState.Open)
            .Select(x => new LocalTusUploadSession(
                x.UploadSessionId,
                x.ExpectedSizeBytes,
                x.Checksum,
                x.ExpiresAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
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
            file.Status,
            file.CreatedAtUtc,
            file.CompletedAtUtc);
    }

    private static string NewId(string prefix)
    {
        return $"{prefix}_{Guid.CreateVersion7():N}";
    }

    private static string BuildObjectKey(string organizationId, string fileId)
    {
        return $"{organizationId}/{fileId}";
    }

    private static UploadCommitIntent ToCommitIntent(UploadSessionRecord session) =>
        new(
            session.CommitId!,
            session.UploadSessionId,
            session.FileId,
            session.OrganizationId,
            session.EnvironmentId,
            session.FilePurpose,
            session.ObjectKey,
            session.ExpectedSizeBytes,
            session.CommitChecksum);

    private static string? NormalizeCanonicalChecksum(string? checksum)
    {
        const string prefix = "sha256:";
        if (checksum is null
            || !checksum.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || checksum.Length != prefix.Length + 64
            || checksum[prefix.Length..].Any(character => character is not (
                >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F')))
        {
            return null;
        }

        return $"{prefix}{checksum[prefix.Length..].ToLowerInvariant()}";
    }

    private static DateTimeOffset NextRecoveryAtUtc(int priorAttemptCount, DateTimeOffset now)
    {
        var seconds = Math.Min(300, 1 << Math.Min(priorAttemptCount, 8));
        return now.AddSeconds(seconds);
    }

    private async Task<bool> TryClaimCommitExecutionAsync(
        UploadSessionRecord session,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.AddMinutes(5);
        if (!dbContext.Database.IsRelational())
        {
            if (session.ExecutionLeaseUntilUtc is { } existingLease && existingLease > now)
            {
                return false;
            }

            session.ClaimExecution(executionOwnerId, leaseUntil);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var claimed = await dbContext.UploadSessions
            .Where(x => x.UploadSessionId == session.UploadSessionId
                && x.State == UploadSessionState.Committing
                && (x.ExecutionLeaseUntilUtc == null || x.ExecutionLeaseUntilUtc <= now))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.ExecutionOwnerId, executionOwnerId)
                    .SetProperty(x => x.ExecutionLeaseUntilUtc, leaseUntil)
                    .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1),
                cancellationToken);
        if (claimed != 1)
        {
            return false;
        }

        await dbContext.Entry(session).ReloadAsync(cancellationToken);
        return string.Equals(session.ExecutionOwnerId, executionOwnerId, StringComparison.Ordinal);
    }

    private async Task<long> CalculateUsedBytesAsync(
        string organizationId,
        string environmentId,
        string? filePurpose,
        CancellationToken cancellationToken)
    {
        var storedBytes = dbContext.StoredFiles
            .Where(file => file.OrganizationId == organizationId
                && file.EnvironmentId == environmentId
                && file.Status != FileStorageFileStatus.Deleted);
        if (!string.IsNullOrWhiteSpace(filePurpose))
        {
            storedBytes = storedBytes.Where(file => file.FilePurpose == filePurpose);
        }

        var storedTotal = await storedBytes.SumAsync(file => file.SizeBytes, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var reservedBytes = dbContext.UploadSessions
            .Where(session => session.State != UploadSessionState.Completed
                && session.ExpiresAtUtc > now
                && session.OrganizationId == organizationId
                && session.EnvironmentId == environmentId);
        if (!string.IsNullOrWhiteSpace(filePurpose))
        {
            reservedBytes = reservedBytes.Where(session => session.FilePurpose == filePurpose);
        }

        var reservedTotal = await reservedBytes.SumAsync(session => session.ExpectedSizeBytes, cancellationToken);

        return storedTotal + reservedTotal;
    }
}
