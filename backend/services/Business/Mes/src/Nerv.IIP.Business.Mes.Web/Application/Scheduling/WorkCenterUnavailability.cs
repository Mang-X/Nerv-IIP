namespace Nerv.IIP.Business.Mes.Web.Application.Scheduling;

public enum OperationTaskStatus
{
    Queued,
    InProgress,
    Completed,
    Cancelled,
}

public sealed record WorkCenterUnavailability(
    string WorkCenterId,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    string Reason,
    string? DeviceAssetId = null,
    string? OrganizationId = null,
    string? EnvironmentId = null);
