using System.Collections.Concurrent;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Domain;

public sealed record ApplicationFact(string OrganizationId, string EnvironmentId, string ApplicationKey, string ApplicationName, IReadOnlySet<string> Versions);
public sealed record ManagedNodeFact(string OrganizationId, string EnvironmentId, string NodeKey, string NodeName, string DeploymentKind);
public sealed record ApplicationInstanceFact(string OrganizationId, string EnvironmentId, string ApplicationKey, string Version, string NodeKey, string InstanceKey, string InstanceName, string ReportedStatus, string HealthStatus, IReadOnlyDictionary<string, string> Metadata);
public sealed record CapabilityManifestFact(string InstanceKey, IReadOnlyList<CapabilityDescriptor> Capabilities);
public sealed record InstanceLivenessFact(string InstanceKey, DateTimeOffset LastHeartbeatAtUtc, bool Reachable, int LatencyMs);
public sealed record InstanceStateHistoryFact(string InstanceKey, DateTimeOffset ObservedAtUtc, string ReportedStatus, string HealthStatus, string Summary);
public sealed record RegistrationResult(string RegistrationId, string InstanceKey);
public sealed record InstanceStatusChanged(string InstanceKey, string PreviousStatus, string CurrentStatus, DateTimeOffset ChangedAtUtc);
public sealed record InstanceListCriteria(string OrganizationId, string EnvironmentId, int PageIndex, int PageSize, string? SortBy, string? SortOrder, string? FilterSearch);
public sealed record InstanceListResult(int EffectivePageIndex, int EffectivePageSize, int TotalCount, IReadOnlyList<InstanceListItemFact> Items);
public sealed record InstanceListItemFact(string ApplicationKey, string ApplicationName, string Version, string NodeKey, string NodeName, string InstanceKey, string InstanceName, string ReportedStatus, string HealthStatus, DateTimeOffset? LastHeartbeatAtUtc, DateTimeOffset? LastStateAtUtc);
public sealed record InstanceDetailFact(string ApplicationKey, string ApplicationName, string Version, string NodeKey, string NodeName, string InstanceKey, string InstanceName, string ReportedStatus, string HealthStatus, DateTimeOffset? LastHeartbeatAtUtc, DateTimeOffset? LastStateAtUtc, IReadOnlyList<CapabilitySummaryFact> Capabilities, IReadOnlyDictionary<string, string> Metadata);
public sealed record CapabilitySummaryFact(string CapabilityCode, string CapabilityVersion, string Category, IReadOnlyList<string> SupportedOperations);

public interface IAppHubStateStore
{
    RegistrationResult Register(ApplicationRegistration registration);
    void RecordHeartbeat(ApplicationHeartbeat heartbeat);
    void RecordStateSnapshot(InstanceStateSnapshot snapshot);
    InstanceListResult QueryInstances(InstanceListCriteria query);
    InstanceDetailFact GetInstanceDetail(string organizationId, string environmentId, string instanceKey);
}

public sealed class InMemoryAppHubStateStore : IAppHubStateStore
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, string> _idempotency = new();

    public List<ApplicationFact> Applications { get; } = [];
    public List<ManagedNodeFact> Nodes { get; } = [];
    public List<ApplicationInstanceFact> Instances { get; } = [];
    public List<CapabilityManifestFact> CapabilityManifests { get; } = [];
    public List<InstanceLivenessFact> Liveness { get; } = [];
    public List<InstanceStateHistoryFact> StateHistory { get; } = [];
    public List<InstanceStatusChanged> PublishedStatusChanges { get; } = [];

    public RegistrationResult Register(ApplicationRegistration registration)
    {
        lock (_gate)
        {
            var scopedIdempotencyKey = $"{registration.Context.OrganizationId}\u001f{registration.Context.EnvironmentId}\u001f{registration.IdempotencyKey}";
            if (_idempotency.TryGetValue(scopedIdempotencyKey, out var existing))
            {
                return new RegistrationResult(existing, registration.InstanceKey);
            }

            var registrationId = $"reg-{_idempotency.Count + 1:000000}";
            _idempotency[scopedIdempotencyKey] = registrationId;

            var app = Applications.FirstOrDefault(x => x.OrganizationId == registration.Context.OrganizationId && x.EnvironmentId == registration.Context.EnvironmentId && x.ApplicationKey == registration.ApplicationKey);
            if (app is null)
            {
                Applications.Add(new ApplicationFact(registration.Context.OrganizationId, registration.Context.EnvironmentId, registration.ApplicationKey, registration.ApplicationName, new HashSet<string> { registration.Version }));
            }
            else
            {
                var versions = app.Versions.ToHashSet(StringComparer.Ordinal);
                versions.Add(registration.Version);
                Applications[Applications.IndexOf(app)] = app with { ApplicationName = registration.ApplicationName, Versions = versions };
            }

            Upsert(Nodes, x => x.OrganizationId == registration.Context.OrganizationId && x.EnvironmentId == registration.Context.EnvironmentId && x.NodeKey == registration.NodeKey, new ManagedNodeFact(registration.Context.OrganizationId, registration.Context.EnvironmentId, registration.NodeKey, registration.NodeName, registration.DeploymentKind));
            Upsert(Instances, x => x.OrganizationId == registration.Context.OrganizationId && x.EnvironmentId == registration.Context.EnvironmentId && x.InstanceKey == registration.InstanceKey, new ApplicationInstanceFact(registration.Context.OrganizationId, registration.Context.EnvironmentId, registration.ApplicationKey, registration.Version, registration.NodeKey, registration.InstanceKey, registration.InstanceName, "unknown", "unknown", registration.Metadata));
            Upsert(CapabilityManifests, x => x.InstanceKey == registration.InstanceKey, new CapabilityManifestFact(registration.InstanceKey, registration.Capabilities));
            return new RegistrationResult(registrationId, registration.InstanceKey);
        }
    }

    public void RecordHeartbeat(ApplicationHeartbeat heartbeat)
    {
        lock (_gate)
        {
            EnsureInstance(heartbeat.Context.OrganizationId, heartbeat.Context.EnvironmentId, heartbeat.InstanceKey);
            Upsert(Liveness, x => x.InstanceKey == heartbeat.InstanceKey, new InstanceLivenessFact(heartbeat.InstanceKey, heartbeat.HeartbeatAtUtc, heartbeat.Reachable, heartbeat.LatencyMs));
        }
    }

    public void RecordStateSnapshot(InstanceStateSnapshot snapshot)
    {
        lock (_gate)
        {
            var instance = EnsureInstance(snapshot.Context.OrganizationId, snapshot.Context.EnvironmentId, snapshot.InstanceKey);
            StateHistory.Add(new InstanceStateHistoryFact(snapshot.InstanceKey, snapshot.ObservedAtUtc, snapshot.ReportedStatus, snapshot.HealthStatus, snapshot.Summary));
            if (!string.Equals(instance.ReportedStatus, "unknown", StringComparison.Ordinal) && !string.Equals(instance.ReportedStatus, snapshot.ReportedStatus, StringComparison.Ordinal))
            {
                PublishedStatusChanges.Add(new InstanceStatusChanged(snapshot.InstanceKey, instance.ReportedStatus, snapshot.ReportedStatus, snapshot.ObservedAtUtc));
            }

            Upsert(Instances, x => x.InstanceKey == snapshot.InstanceKey, instance with { ReportedStatus = snapshot.ReportedStatus, HealthStatus = snapshot.HealthStatus, Metadata = snapshot.Metadata });
        }
    }

    public InstanceListResult QueryInstances(InstanceListCriteria query)
    {
        lock (_gate)
        {
            var filtered = Instances
                .Where(x => x.OrganizationId == query.OrganizationId && x.EnvironmentId == query.EnvironmentId)
                .Where(x =>
                {
                    var app = Applications.Single(a => a.OrganizationId == x.OrganizationId && a.EnvironmentId == x.EnvironmentId && a.ApplicationKey == x.ApplicationKey);
                    return string.IsNullOrWhiteSpace(query.FilterSearch)
                        || app.ApplicationName.Contains(query.FilterSearch, StringComparison.OrdinalIgnoreCase)
                        || x.InstanceName.Contains(query.FilterSearch, StringComparison.OrdinalIgnoreCase);
                })
                .Select(ToListItem)
                .ApplyInstanceListSort(query)
                .ToList();

            var effectivePageIndex = Math.Max(query.PageIndex, 1);
            var effectivePageSize = Math.Max(query.PageSize, 1);
            var items = filtered
                .Skip((effectivePageIndex - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToList();
            return new InstanceListResult(effectivePageIndex, effectivePageSize, filtered.Count, items);
        }
    }

    public InstanceDetailFact GetInstanceDetail(string organizationId, string environmentId, string instanceKey)
    {
        lock (_gate)
        {
            var instance = Instances.Single(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.InstanceKey == instanceKey);
            var app = Applications.Single(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.ApplicationKey == instance.ApplicationKey);
            var node = Nodes.Single(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.NodeKey == instance.NodeKey);
            var live = Liveness.LastOrDefault(x => x.InstanceKey == instance.InstanceKey);
            var state = StateHistory.LastOrDefault(x => x.InstanceKey == instance.InstanceKey);
            var capabilities = CapabilityManifests.LastOrDefault(x => x.InstanceKey == instance.InstanceKey)?.Capabilities
                .Select(x => new CapabilitySummaryFact(x.CapabilityCode, x.CapabilityVersion, x.Category, x.SupportedOperations))
                .ToList() ?? [];

            return new InstanceDetailFact(app.ApplicationKey, app.ApplicationName, instance.Version, node.NodeKey, node.NodeName, instance.InstanceKey, instance.InstanceName, instance.ReportedStatus, instance.HealthStatus, live?.LastHeartbeatAtUtc, state?.ObservedAtUtc, capabilities, instance.Metadata);
        }
    }

    private InstanceListItemFact ToListItem(ApplicationInstanceFact instance)
    {
        var app = Applications.Single(x => x.OrganizationId == instance.OrganizationId && x.EnvironmentId == instance.EnvironmentId && x.ApplicationKey == instance.ApplicationKey);
        var node = Nodes.Single(x => x.OrganizationId == instance.OrganizationId && x.EnvironmentId == instance.EnvironmentId && x.NodeKey == instance.NodeKey);
        var live = Liveness.LastOrDefault(x => x.InstanceKey == instance.InstanceKey);
        var state = StateHistory.LastOrDefault(x => x.InstanceKey == instance.InstanceKey);
        return new InstanceListItemFact(app.ApplicationKey, app.ApplicationName, instance.Version, node.NodeKey, node.NodeName, instance.InstanceKey, instance.InstanceName, instance.ReportedStatus, instance.HealthStatus, live?.LastHeartbeatAtUtc, state?.ObservedAtUtc);
    }

    private ApplicationInstanceFact EnsureInstance(string organizationId, string environmentId, string instanceKey)
    {
        return Instances.SingleOrDefault(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.InstanceKey == instanceKey)
            ?? throw new InvalidOperationException($"Instance context is invalid: {instanceKey}");
    }

    private static void Upsert<T>(List<T> list, Func<T, bool> predicate, T value)
    {
        var current = list.FirstOrDefault(predicate);
        if (current is null)
        {
            list.Add(value);
            return;
        }

        list[list.IndexOf(current)] = value;
    }
}

public static class InstanceListSorting
{
    public static IEnumerable<InstanceListItemFact> ApplyInstanceListSort(this IEnumerable<InstanceListItemFact> items, InstanceListCriteria query)
    {
        var descending = string.Equals(query.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        return (query.SortBy?.ToLowerInvariant(), descending) switch
        {
            ("instancekey", true) => items.OrderByDescending(x => x.InstanceKey, StringComparer.Ordinal),
            ("instancekey", false) => items.OrderBy(x => x.InstanceKey, StringComparer.Ordinal),
            ("instancename", true) => items.OrderByDescending(x => x.InstanceName, StringComparer.Ordinal),
            ("instancename", false) => items.OrderBy(x => x.InstanceName, StringComparer.Ordinal),
            ("reportedstatus", true) => items.OrderByDescending(x => x.ReportedStatus, StringComparer.Ordinal),
            ("reportedstatus", false) => items.OrderBy(x => x.ReportedStatus, StringComparer.Ordinal),
            ("healthstatus", true) => items.OrderByDescending(x => x.HealthStatus, StringComparer.Ordinal),
            ("healthstatus", false) => items.OrderBy(x => x.HealthStatus, StringComparer.Ordinal),
            ("lastheartbeatatutc", true) => items.OrderByDescending(x => x.LastHeartbeatAtUtc),
            ("lastheartbeatatutc", false) => items.OrderBy(x => x.LastHeartbeatAtUtc),
            ("applicationname", true) => items.OrderByDescending(x => x.ApplicationName, StringComparer.Ordinal).ThenBy(x => x.InstanceName, StringComparer.Ordinal),
            _ => items.OrderBy(x => x.ApplicationName, StringComparer.Ordinal).ThenBy(x => x.InstanceName, StringComparer.Ordinal)
        };
    }
}
