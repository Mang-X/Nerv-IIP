namespace Nerv.IIP.FileStorage.Infrastructure.Records;

public sealed class DownloadGrantRecord
{
    private DownloadGrantRecord()
    {
    }

    public string DownloadGrantId { get; private set; } = string.Empty;
    public string FileId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static DownloadGrantRecord Create(
        string downloadGrantId,
        string fileId,
        string organizationId,
        string environmentId,
        string provider,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        return new DownloadGrantRecord
        {
            DownloadGrantId = downloadGrantId,
            FileId = fileId,
            OrganizationId = organizationId,
            EnvironmentId = environmentId,
            Provider = provider,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
