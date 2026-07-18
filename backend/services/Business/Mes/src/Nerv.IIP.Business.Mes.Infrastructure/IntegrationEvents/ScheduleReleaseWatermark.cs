namespace Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;

public sealed class ScheduleReleaseWatermark
{
    private ScheduleReleaseWatermark()
    {
    }

    public ScheduleReleaseWatermark(
        string organizationId,
        string environmentId,
        string revokedPlanId,
        long revokedReleaseRevision,
        DateTimeOffset revokedAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        RevokedPlanId = revokedPlanId;
        RevokedReleaseRevision = revokedReleaseRevision;
        RevokedAtUtc = revokedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string RevokedPlanId { get; private set; } = string.Empty;

    public long RevokedReleaseRevision { get; private set; }

    public DateTimeOffset RevokedAtUtc { get; private set; }

    public void RecordRevocation(string planId, long releaseRevision, DateTimeOffset revokedAtUtc)
    {
        if (releaseRevision <= RevokedReleaseRevision)
        {
            return;
        }

        RevokedPlanId = planId;
        RevokedReleaseRevision = releaseRevision;
        RevokedAtUtc = revokedAtUtc;
    }
}
