namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>当前 NCR 的证据上传；用途与 owner 服务/类型由服务端固定。</summary>
public sealed record BusinessConsoleCreateQualityEvidenceUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    string NcrId,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum = null);

/// <summary>仅包含 Gateway 受控 tus 指令；fileId 在 complete 成功后才可作为附件。</summary>
public sealed record BusinessConsoleQualityEvidenceUploadSessionResponse(
    string UploadSessionId,
    string FileId,
    string UploadProtocol,
    DateTimeOffset ExpiresAtUtc,
    string UploadUrl,
    IReadOnlyDictionary<string, string> UploadHeaders);

public sealed record BusinessConsoleCompleteQualityEvidenceUploadRequest(
    string OrganizationId,
    string EnvironmentId,
    string NcrId,
    string? Checksum = null,
    long? SizeBytes = null);

public sealed record BusinessConsoleQualityEvidenceFile(string FileId, string FileName, string ContentType, long SizeBytes);
