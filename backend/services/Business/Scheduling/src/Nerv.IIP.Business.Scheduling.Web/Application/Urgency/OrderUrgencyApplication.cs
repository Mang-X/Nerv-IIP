using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public static class OrderUrgencyContractMapper
{
    public static OrderUrgencyContract ToContract(OrderUrgencyCalculationResult result) => new(
        result.OrderId,
        result.BusinessReference,
        Name(result.Level),
        new OrderUrgencyBusinessPriorityContract(
            Name(result.BusinessPriority.Level), result.BusinessPriority.Source, result.BusinessPriority.Reason,
            result.BusinessPriority.SetAtUtc, result.BusinessPriority.ExpiresAtUtc, result.BusinessPriority.Revision,
            result.BusinessPriority.ReasonCodes),
        new OrderUrgencyTimeCriticalityContract(
            Name(result.TimeCriticality.Level), result.CriticalRatio, result.SlackHours, result.ExpectedDelayHours,
            result.TimeCriticality.DueUtc, result.TimeCriticality.EstimatedCompletionUtc,
            result.TimeCriticality.RemainingCycleHours, result.TimeCriticality.ReasonCodes),
        new OrderUrgencyExecutionRiskContract(
            Name(result.ExecutionRisk.Level), result.ExecutionRisk.IsSourceMissing, result.ExecutionRisk.IsSourceStale,
            result.ExecutionRisk.FactsObservedAtUtc, result.ExecutionRisk.ReasonCodes,
            result.ExecutionRisk.Facts.Select(x => new OrderUrgencyExecutionRiskFactContract(
                x.ReasonCode, Name(x.Category), x.IsBlocking, x.SourceReference, x.ObservedAtUtc)).ToArray()),
        result.CalculatedAtUtc,
        result.ModelVersion,
        result.InputFingerprint);

    public static string Serialize(OrderUrgencyCalculationResult result) =>
        JsonSerializer.Serialize(ToContract(result), SchedulingJson.Options);

    public static OrderUrgencyContract Deserialize(string json) =>
        JsonSerializer.Deserialize<OrderUrgencyContract>(json, SchedulingJson.Options)
        ?? throw new InvalidOperationException("Persisted order urgency snapshot is invalid.");

    private static string Name<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();
}

public sealed class OrderUrgencyService(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromHours(2);

    public async Task CapturePlanAsync(
        SchedulingProblemContract problem,
        SchedulePlanContract plan,
        string inputFingerprint,
        DateTimeOffset calculatedAtUtc,
        CancellationToken cancellationToken)
    {
        var orderIds = problem.Orders.Select(x => x.OrderId).ToArray();
        var priorities = await dbContext.OrderUrgencyBusinessPriorities
            .Where(x => x.OrganizationId == problem.OrganizationId && x.EnvironmentId == problem.EnvironmentId && orderIds.Contains(x.OrderId))
            .ToDictionaryAsync(x => x.OrderId, StringComparer.Ordinal, cancellationToken);

        foreach (var order in problem.Orders)
        {
            var priority = priorities.TryGetValue(order.OrderId, out var current)
                ? current.ToFact(calculatedAtUtc)
                : DefaultPriority();
            var result = OrderUrgencyFactAssembler.Calculate(
                problem, plan, order.OrderId, priority, calculatedAtUtc, inputFingerprint);
            await AddSnapshotIfMissingAsync(problem.OrganizationId, problem.EnvironmentId, result, cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<OrderUrgencyContract>> ListAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> references,
        CancellationToken cancellationToken)
    {
        var requested = references.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latest = await LoadLatestAsync(organizationId, environmentId, requested, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var known = latest.Values
            .Where(x => requested.Count == 0 || requested.Contains(x.OrderId) || requested.Contains(x.BusinessReference))
            .Select(x => OrderUrgencyContractMapper.Deserialize(x.ResultJson))
            .ToArray();
        var matched = known.SelectMany(x => new[] { x.OrderId, x.BusinessReference })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requested.Where(x => !matched.Contains(x))
            .Select(x => MissingContract(organizationId, environmentId, x, now));
        return known.Concat(missing)
            .OrderByDescending(x => UrgencyRank(x.Level))
            .ThenBy(x => x.BusinessReference, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<OrderUrgencyDetailContract> GetAsync(
        string organizationId,
        string environmentId,
        string orderReference,
        CancellationToken cancellationToken)
    {
        var snapshots = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                (x.OrderId == orderReference || x.BusinessReference == orderReference))
            .OrderByDescending(x => x.CalculatedAtUtc)
            .ThenByDescending(x => x.BusinessPriorityRevision)
            .ToArrayAsync(cancellationToken);
        if (snapshots.Length == 0)
        {
            var missing = MissingContract(organizationId, environmentId, orderReference, timeProvider.GetUtcNow());
            return new OrderUrgencyDetailContract(missing, [missing], []);
        }
        var changes = await LoadPriorityChangesAsync(
            organizationId, environmentId, snapshots[0].OrderId, cancellationToken);
        var history = snapshots.Select(x => OrderUrgencyContractMapper.Deserialize(x.ResultJson)).ToArray();
        return new OrderUrgencyDetailContract(history[0], history, changes);
    }

    public async Task<OrderUrgencyDetailContract> SetBusinessPriorityAsync(
        string organizationId,
        string environmentId,
        string orderReference,
        BusinessPriorityLevel level,
        string changedBy,
        string reason,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var snapshots = await dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                (x.OrderId == orderReference || x.BusinessReference == orderReference))
            .OrderByDescending(x => x.CalculatedAtUtc)
            .ThenByDescending(x => x.BusinessPriorityRevision)
            .ToArrayAsync(cancellationToken);
        var latest = snapshots.FirstOrDefault()
            ?? throw new KnownException($"Order urgency was not found, Reference = {orderReference}");
        var existingChanges = await LoadPriorityChangesAsync(
            organizationId, environmentId, latest.OrderId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var priority = await dbContext.OrderUrgencyBusinessPriorities.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.OrderId == latest.OrderId,
            cancellationToken);
        OrderUrgencyBusinessPriorityChange change;
        if (priority is null)
        {
            priority = OrderUrgencyBusinessPriority.Create(
                organizationId, environmentId, latest.OrderId, latest.BusinessReference,
                level, changedBy, reason, now, expiresAtUtc);
            change = priority.InitialChange();
            dbContext.OrderUrgencyBusinessPriorities.Add(priority);
        }
        else
        {
            change = priority.Change(level, changedBy, reason, now, expiresAtUtc);
        }
        dbContext.OrderUrgencyBusinessPriorityChanges.Add(change);
        var refreshed = await RefreshFromSnapshotAsync(
            latest, now, priority.ToFact(now), cancellationToken, force: true)
            ?? throw new InvalidOperationException("A forced priority refresh must produce an urgency snapshot.");
        var current = OrderUrgencyContractMapper.Deserialize(refreshed.ResultJson);
        var history = new[] { current }
            .Concat(snapshots.Select(x => OrderUrgencyContractMapper.Deserialize(x.ResultJson)))
            .DistinctBy(
                x => $"{x.ModelVersion}\u001f{x.InputFingerprint}\u001f{x.BusinessPriority.Revision}\u001f{x.CalculatedAtUtc:O}",
                StringComparer.Ordinal)
            .ToArray();
        var changes = new[] { ToContract(change) }
            .Concat(existingChanges)
            .OrderByDescending(x => x.Revision)
            .ToArray();
        return new OrderUrgencyDetailContract(current, history, changes);
    }

    private async Task<Dictionary<string, OrderUrgencySnapshot>> LoadLatestAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> references,
        CancellationToken cancellationToken)
    {
        var query = dbContext.OrderUrgencySnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId);
        if (references.Count > 0)
        {
            var requested = references.ToArray();
            query = query.Where(x => requested.Contains(x.OrderId) || requested.Contains(x.BusinessReference));
        }

        var latestCalculationTimes = query
            .GroupBy(x => x.OrderId)
            .Select(group => new
            {
                OrderId = group.Key,
                CalculatedAtUtc = group.Max(x => x.CalculatedAtUtc),
            });
        var snapshots = await query
            .Join(
                latestCalculationTimes,
                snapshot => new { snapshot.OrderId, snapshot.CalculatedAtUtc },
                latest => new { latest.OrderId, latest.CalculatedAtUtc },
                (snapshot, _) => snapshot)
            .OrderByDescending(x => x.BusinessPriorityRevision)
            .ToArrayAsync(cancellationToken);
        return snapshots.GroupBy(x => x.OrderId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    }

    public async Task RefreshContextAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var latest = await LoadLatestAsync(organizationId, environmentId, [], cancellationToken);
        var now = timeProvider.GetUtcNow();
        var changed = false;
        foreach (var snapshot in latest.Values)
        {
            changed |= await RefreshFromSnapshotAsync(snapshot, now, null, cancellationToken) is not null;
        }
        if (!changed) return;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Count > 0 &&
            exception.Entries.All(entry => entry.Entity is OrderUrgencySnapshot))
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<IReadOnlyCollection<OrderUrgencyBusinessPriorityChangeContract>> LoadPriorityChangesAsync(
        string organizationId,
        string environmentId,
        string orderId,
        CancellationToken cancellationToken)
    {
        var changes = await dbContext.OrderUrgencyBusinessPriorityChanges.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.OrderId == orderId)
            .OrderByDescending(x => x.Revision)
            .ToArrayAsync(cancellationToken);
        return changes.Select(ToContract).ToArray();
    }

    private static OrderUrgencyBusinessPriorityChangeContract ToContract(
        OrderUrgencyBusinessPriorityChange change) => new(
            change.Revision,
            change.PreviousLevel.HasValue ? change.PreviousLevel.Value.ToString().ToLowerInvariant() : null,
            change.NewLevel.ToString().ToLowerInvariant(),
            change.ChangedBy,
            change.Reason,
            change.ChangedAtUtc,
            change.ExpiresAtUtc);

    private async Task<OrderUrgencySnapshot?> RefreshFromSnapshotAsync(
        OrderUrgencySnapshot snapshot,
        DateTimeOffset now,
        BusinessPriorityFact? priorityOverride,
        CancellationToken cancellationToken,
        bool force = false)
    {
        var bucket = Bucket(now);
        var latestInvalidation = await dbContext.SchedulePlanInvalidations.AsNoTracking()
            .Where(x => x.OrganizationId == snapshot.OrganizationId && x.EnvironmentId == snapshot.EnvironmentId &&
                x.RecordedAtUtc > snapshot.CalculatedAtUtc &&
                (x.AffectedWorkOrderId == null || x.AffectedWorkOrderId == snapshot.OrderId))
            .OrderByDescending(x => x.RecordedAtUtc)
            .Select(x => new { x.SourceEventId, x.RecordedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        var invalidated = latestInvalidation is not null;
        if (!force && !invalidated && snapshot.CalculationBucketUtc == bucket) return null;
        var current = OrderUrgencyContractMapper.Deserialize(snapshot.ResultJson);
        var priority = priorityOverride ?? await LoadPriorityFactAsync(snapshot, now, cancellationToken);
        var observedAt = current.ExecutionRisk.FactsObservedAtUtc;
        var stale = invalidated || !observedAt.HasValue || now - observedAt.Value > FreshnessWindow;
        var remaining = current.TimeCriticality.EstimatedCompletionUtc > now
            ? current.TimeCriticality.EstimatedCompletionUtc - now
            : TimeSpan.Zero;
        var inputFingerprint = latestInvalidation is null
            ? current.InputFingerprint
            : Fingerprint($"{current.InputFingerprint}|invalidation:{latestInvalidation.SourceEventId}:{latestInvalidation.RecordedAtUtc.UtcTicks}");
        var result = OrderUrgencyCalculator.Calculate(new OrderUrgencyCalculationInput(
            current.OrderId, current.BusinessReference, now, current.TimeCriticality.DueUtc, remaining, priority,
            current.ExecutionRisk.Facts.Select(x => new ExecutionRiskFact(
                x.ReasonCode, Enum.Parse<ExecutionRiskCategory>(x.Category, true), x.IsBlocking, x.SourceReference, x.ObservedAtUtc)).ToArray(),
            current.ExecutionRisk.IsSourceMissing, stale, observedAt, inputFingerprint));
        return await AddSnapshotIfMissingAsync(snapshot.OrganizationId, snapshot.EnvironmentId, result, cancellationToken);
    }

    private async Task<BusinessPriorityFact> LoadPriorityFactAsync(OrderUrgencySnapshot snapshot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var priority = await dbContext.OrderUrgencyBusinessPriorities.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganizationId == snapshot.OrganizationId && x.EnvironmentId == snapshot.EnvironmentId && x.OrderId == snapshot.OrderId,
            cancellationToken);
        return priority?.ToFact(now) ?? DefaultPriority();
    }

    private async Task<OrderUrgencySnapshot> AddSnapshotIfMissingAsync(
        string organizationId,
        string environmentId,
        OrderUrgencyCalculationResult result,
        CancellationToken cancellationToken)
    {
        var bucket = Bucket(result.CalculatedAtUtc);
        var revision = result.BusinessPriority.Revision;
        var local = dbContext.OrderUrgencySnapshots.Local.FirstOrDefault(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.OrderId == result.OrderId &&
            x.ModelVersion == result.ModelVersion && x.InputFingerprint == result.InputFingerprint &&
            x.BusinessPriorityRevision == revision && x.CalculationBucketUtc == bucket);
        if (local is not null) return local;
        var existing = await dbContext.OrderUrgencySnapshots.AsNoTracking().FirstOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.OrderId == result.OrderId &&
            x.ModelVersion == result.ModelVersion && x.InputFingerprint == result.InputFingerprint &&
            x.BusinessPriorityRevision == revision && x.CalculationBucketUtc == bucket,
            cancellationToken);
        if (existing is not null) return existing;
        var snapshot = new OrderUrgencySnapshot(
            organizationId, environmentId, result.OrderId, result.BusinessReference, result.Level,
            result.ModelVersion, result.InputFingerprint, revision, bucket, result.CalculatedAtUtc,
            OrderUrgencyContractMapper.Serialize(result));
        dbContext.OrderUrgencySnapshots.Add(snapshot);
        return snapshot;
    }

    private static BusinessPriorityFact DefaultPriority() =>
        new(BusinessPriorityLevel.P2, "authoritative-default", "No manual business-priority override.", DateTimeOffset.UnixEpoch, null, 0);

    private static OrderUrgencyContract MissingContract(
        string organizationId,
        string environmentId,
        string orderReference,
        DateTimeOffset calculatedAtUtc)
    {
        var inputFingerprint = Fingerprint($"missing|{organizationId}|{environmentId}|{orderReference}");
        var result = OrderUrgencyCalculator.Calculate(new OrderUrgencyCalculationInput(
            orderReference,
            orderReference,
            calculatedAtUtc,
            null,
            TimeSpan.Zero,
            DefaultPriority(),
            [],
            true,
            true,
            null,
            inputFingerprint));
        return OrderUrgencyContractMapper.ToContract(result);
    }

    private static int UrgencyRank(string level) => level.ToLowerInvariant() switch
    {
        "critical" => 5,
        "urgent" => 4,
        "highrisk" => 3,
        "attention" => 2,
        "normal" => 1,
        _ => 0,
    };

    private static DateTimeOffset Bucket(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var minutes = utc.Minute - utc.Minute % 15;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minutes, 0, TimeSpan.Zero);
    }

    private static string Fingerprint(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record ListOrderUrgenciesQuery(string OrganizationId, string EnvironmentId, IReadOnlyCollection<string> References)
    : IQuery<IReadOnlyCollection<OrderUrgencyContract>>;

public sealed class ListOrderUrgenciesQueryHandler(OrderUrgencyService service)
    : IQueryHandler<ListOrderUrgenciesQuery, IReadOnlyCollection<OrderUrgencyContract>>
{
    public Task<IReadOnlyCollection<OrderUrgencyContract>> Handle(ListOrderUrgenciesQuery request, CancellationToken cancellationToken) =>
        service.ListAsync(request.OrganizationId, request.EnvironmentId, request.References, cancellationToken);
}

public sealed record GetOrderUrgencyQuery(string OrganizationId, string EnvironmentId, string OrderReference)
    : IQuery<OrderUrgencyDetailContract>;

public sealed class GetOrderUrgencyQueryHandler(OrderUrgencyService service) : IQueryHandler<GetOrderUrgencyQuery, OrderUrgencyDetailContract>
{
    public Task<OrderUrgencyDetailContract> Handle(GetOrderUrgencyQuery request, CancellationToken cancellationToken) =>
        service.GetAsync(request.OrganizationId, request.EnvironmentId, request.OrderReference, cancellationToken);
}

public sealed record SetOrderUrgencyBusinessPriorityCommand(
    string OrganizationId,
    string EnvironmentId,
    string OrderReference,
    BusinessPriorityLevel Level,
    string ChangedBy,
    string Reason,
    DateTimeOffset? ExpiresAtUtc) : ICommand<OrderUrgencyDetailContract>;

public sealed class OrderUrgencyPriorityConflictBehavior
    : IPipelineBehavior<SetOrderUrgencyBusinessPriorityCommand, OrderUrgencyDetailContract>
{
    public async Task<OrderUrgencyDetailContract> Handle(
        SetOrderUrgencyBusinessPriorityCommand request,
        RequestHandlerDelegate<OrderUrgencyDetailContract> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new KnownException("Order business priority changed concurrently; reload the latest revision and retry.");
        }
        catch (DbUpdateException)
        {
            throw new KnownException("Order business priority could not be recorded atomically; reload and retry.");
        }
    }
}

public sealed class SetOrderUrgencyBusinessPriorityCommandHandler(OrderUrgencyService service)
    : ICommandHandler<SetOrderUrgencyBusinessPriorityCommand, OrderUrgencyDetailContract>
{
    public Task<OrderUrgencyDetailContract> Handle(SetOrderUrgencyBusinessPriorityCommand request, CancellationToken cancellationToken) =>
        service.SetBusinessPriorityAsync(
            request.OrganizationId, request.EnvironmentId, request.OrderReference, request.Level,
            request.ChangedBy, request.Reason, request.ExpiresAtUtc, cancellationToken);
}
