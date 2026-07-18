using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure.Repositories;

public interface IApplicationInstanceRepository : IRepository<ApplicationInstance, ApplicationInstanceId>, IInstanceStateSnapshotRecorder
{
    Task<ApplicationInstance?> GetByInstanceKeyAsync(string instanceKey, CancellationToken cancellationToken = default);
    Task<ApplicationInstance?> GetByContextAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken = default);
}

public class ApplicationInstanceRepository(ApplicationDbContext context)
    : RepositoryBase<ApplicationInstance, ApplicationInstanceId, ApplicationDbContext>(context), IApplicationInstanceRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<ApplicationInstance?> GetByInstanceKeyAsync(string instanceKey, CancellationToken cancellationToken = default)
    {
        return await WithAggregate()
            .FirstOrDefaultAsync(x => x.InstanceKey == instanceKey, cancellationToken);
    }

    public async Task<ApplicationInstance?> GetByContextAsync(string organizationId, string environmentId, string instanceKey, CancellationToken cancellationToken = default)
    {
        return await WithAggregate()
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.InstanceKey == instanceKey,
                cancellationToken);
    }

    public async Task RecordAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var instance = await GetByContextAsync(
                snapshot.Context.OrganizationId,
                snapshot.Context.EnvironmentId,
                snapshot.InstanceKey,
                cancellationToken)
                ?? throw new KnownException($"Instance context is invalid: {snapshot.InstanceKey}");

            ApplySnapshot(instance, snapshot);
            try
            {
                await _context.SaveEntitiesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                _context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (
                attempt < 3 && CollectionHealthUniqueConflictDetector.IsUniqueConflict(exception))
            {
                _context.ChangeTracker.Clear();
            }
        }

        throw new DbUpdateConcurrencyException("Connector collection-health merge exceeded the bounded concurrency retry limit.");
    }

    private static void ApplySnapshot(ApplicationInstance instance, InstanceStateSnapshot snapshot)
    {
        instance.RecordStateSnapshot(
            snapshot.ObservedAtUtc,
            snapshot.ReportedStatus,
            snapshot.HealthStatus,
            snapshot.Summary,
            snapshot.Metadata);
        if (snapshot.CollectionHealth is not null)
        {
            instance.RecordCollectionHealth(snapshot.CollectionHealth);
        }
    }

    private IQueryable<ApplicationInstance> WithAggregate()
    {
        return _context.ApplicationInstances
            .Include(x => x.Heartbeat)
            .Include(x => x.CollectionHealth)
            .Include(x => x.StateHistory)
            .Include(x => x.StatusChanges);
    }
}
