namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>工程文档上传；用途和 owner 由服务端固定，owner 标识取认证身份。</summary>
public sealed record BusinessConsoleCreateSopFileUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum = null);

/// <summary>仅返回 BusinessGateway 受控 tus 指令。</summary>
public sealed record BusinessConsoleSopFileUploadSessionResponse(
    string UploadSessionId,
    string FileId,
    string UploadProtocol,
    DateTimeOffset ExpiresAtUtc,
    string UploadUrl,
    IReadOnlyDictionary<string, string> UploadHeaders);

public sealed record BusinessConsoleCompleteSopFileUploadRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Checksum = null,
    long? SizeBytes = null);

/// <summary>上传完成后的真实文件引用，用于工程文档登记。</summary>
public sealed record BusinessConsoleSopFile(string FileId, string FileName, string ContentType, long SizeBytes);
