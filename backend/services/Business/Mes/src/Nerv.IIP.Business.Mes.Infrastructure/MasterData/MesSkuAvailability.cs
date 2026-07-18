using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Business.Mes.Infrastructure.MasterData;

public partial record MesSkuAvailabilityId : IGuidStronglyTypedId;

public sealed class MesSkuAvailability : Entity<MesSkuAvailabilityId>
{
    private MesSkuAvailability()
    {
    }

    private MesSkuAvailability(
        string organizationId,
        string environmentId,
        string skuCode,
        DateTimeOffset changedAtUtc,
        string disabledReason,
        string sourceEventId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        SkuCode = skuCode;
        ChangedAtUtc = changedAtUtc;
        DisabledReason = disabledReason;
        SourceEventId = sourceEventId;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = MesSkuAvailabilityStatuses.Disabled;
    public DateTimeOffset ChangedAtUtc { get; private set; }
    public string DisabledReason { get; private set; } = string.Empty;
    public string SourceEventId { get; private set; } = string.Empty;
    public bool IsDisabled => Status == MesSkuAvailabilityStatuses.Disabled;

    public static MesSkuAvailability CreateDisabled(
        string organizationId,
        string environmentId,
        string skuCode,
        DateTimeOffset changedAtUtc,
        string disabledReason,
        string sourceEventId) =>
        new(organizationId, environmentId, skuCode, changedAtUtc, disabledReason, sourceEventId);

    public void ApplyDisabled(DateTimeOffset changedAtUtc, string disabledReason, string sourceEventId)
    {
        if (changedAtUtc <= ChangedAtUtc)
        {
            return;
        }

        Status = MesSkuAvailabilityStatuses.Disabled;
        ChangedAtUtc = changedAtUtc;
        DisabledReason = disabledReason;
        SourceEventId = sourceEventId;
    }
}

public static class MesSkuAvailabilityStatuses
{
    public const string Disabled = "disabled";
}
