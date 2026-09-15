namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public interface ILocalTusUploadSessionIndex
{
    Task<bool> CanAcceptTusUploadAsync(string uploadSessionId, CancellationToken cancellationToken);
    Task<LocalTusUploadSession?> GetTusUploadSessionAsync(
        string uploadSessionId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed record LocalTusUploadSession(
    string UploadSessionId,
    long ExpectedSizeBytes,
    string? Checksum,
    DateTimeOffset ExpiresAtUtc,
    string OrganizationId,
    string EnvironmentId);
