using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Infrastructure.MasterData;

public partial record BusinessPartnerAvailabilityId : IGuidStronglyTypedId;

public sealed class BusinessPartnerAvailability : Entity<BusinessPartnerAvailabilityId>
{
    private BusinessPartnerAvailability()
    {
    }

    private BusinessPartnerAvailability(
        string organizationId,
        string environmentId,
        string partnerCode,
        string status,
        DateTimeOffset changedAtUtc,
        string sourceEventId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        PartnerCode = partnerCode;
        Status = status;
        ChangedAtUtc = changedAtUtc;
        SourceEventId = sourceEventId;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PartnerCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public bool IsDisabled => string.Equals(Status, BusinessPartnerAvailabilityStatuses.Disabled, StringComparison.Ordinal);

    public static BusinessPartnerAvailability Create(
        string organizationId,
        string environmentId,
        string partnerCode,
        string status,
        DateTimeOffset changedAtUtc,
        string sourceEventId)
    {
        return new BusinessPartnerAvailability(
            organizationId,
            environmentId,
            partnerCode,
            status,
            changedAtUtc,
            sourceEventId);
    }

    public void Apply(string status, DateTimeOffset changedAtUtc, string sourceEventId)
    {
        if (changedAtUtc <= ChangedAtUtc)
        {
            return;
        }

        Status = status;
        ChangedAtUtc = changedAtUtc;
        SourceEventId = sourceEventId;
    }
}
