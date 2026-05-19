using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate
{
    public record ApplicationRegisteredDomainEvent(string OrganizationId, string EnvironmentId, string ApplicationKey, string Version) : IDomainEvent;
    public record ApplicationDeactivatedDomainEvent(string OrganizationId, string EnvironmentId, string ApplicationKey) : IDomainEvent;
}

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate
{
    public record ManagedNodeRegisteredDomainEvent(string OrganizationId, string EnvironmentId, string NodeKey) : IDomainEvent;
}

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate
{
    public record ApplicationInstanceRegisteredDomainEvent(string OrganizationId, string EnvironmentId, string InstanceKey) : IDomainEvent;
    public record ApplicationHeartbeatRecordedDomainEvent(string InstanceKey, DateTimeOffset HeartbeatAtUtc, bool Reachable) : IDomainEvent;
    public record InstanceStateSnapshotRecordedDomainEvent(string InstanceKey, DateTimeOffset ObservedAtUtc, string ReportedStatus, string HealthStatus) : IDomainEvent;
    public record ApplicationInstanceStatusChangedDomainEvent(string InstanceKey, string PreviousStatus, string CurrentStatus, DateTimeOffset ChangedAtUtc) : IDomainEvent;
}
