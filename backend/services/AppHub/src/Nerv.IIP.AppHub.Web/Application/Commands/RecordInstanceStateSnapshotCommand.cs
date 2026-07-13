using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.Commands;

public record RecordInstanceStateSnapshotCommand(InstanceStateSnapshot Snapshot) : ICommand;

public class RecordInstanceStateSnapshotCommandHandler(IServiceProvider services)
    : ICommandHandler<RecordInstanceStateSnapshotCommand>
{
    public async Task Handle(RecordInstanceStateSnapshotCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<ApplicationDbContext>() is null)
        {
            services.GetRequiredService<IAppHubStateStore>().RecordStateSnapshot(request.Snapshot);
            return;
        }

        var snapshot = request.Snapshot;
        var instanceRepository = services.GetRequiredService<IApplicationInstanceRepository>();
        var instance = await instanceRepository.GetByContextAsync(
            snapshot.Context.OrganizationId,
            snapshot.Context.EnvironmentId,
            snapshot.InstanceKey,
            cancellationToken)
            ?? throw new KnownException($"Instance context is invalid: {snapshot.InstanceKey}");

        instance.RecordStateSnapshot(
            snapshot.ObservedAtUtc,
            snapshot.ReportedStatus,
            snapshot.HealthStatus,
            snapshot.Summary,
            snapshot.Metadata);
        if (snapshot.CollectionHealth is not null)
        {
            instance.RecordCollectionHealth(snapshot.CollectionHealth);
            try
            {
                await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsCollectionHealthUniqueConflict(ex))
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                db.ChangeTracker.Clear();
                var concurrent = await db.ApplicationInstances
                    .Include(x => x.Heartbeat)
                    .Include(x => x.CollectionHealth)
                    .Include(x => x.StateHistory)
                    .Include(x => x.StatusChanges)
                    .SingleAsync(x => x.OrganizationId == snapshot.Context.OrganizationId
                        && x.EnvironmentId == snapshot.Context.EnvironmentId
                        && x.InstanceKey == snapshot.InstanceKey, cancellationToken);
                if (concurrent.CollectionHealth is null)
                {
                    throw;
                }

                concurrent.RecordStateSnapshot(snapshot.ObservedAtUtc, snapshot.ReportedStatus, snapshot.HealthStatus, snapshot.Summary, snapshot.Metadata);
                concurrent.RecordCollectionHealth(snapshot.CollectionHealth);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public static bool IsCollectionHealthUniqueConflict(DbUpdateException exception)
    {
        var cause = exception.InnerException;
        if (cause is null) return false;
        if (cause is CollectionHealthUniqueConstraintException) return true;
        var type = cause.GetType();
        if (type.FullName == "Npgsql.PostgresException")
        {
            var sqlState = type.GetProperty("SqlState")?.GetValue(cause) as string;
            var constraint = type.GetProperty("ConstraintName")?.GetValue(cause) as string;
            return sqlState == "23505" && constraint is "ux_connector_collection_health_instance" or "ux_connector_collection_health_scope";
        }

        if (type.FullName == "Microsoft.Data.Sqlite.SqliteException")
        {
            var code = type.GetProperty("SqliteErrorCode")?.GetValue(cause) as int?;
            return code == 19 && (cause.Message.Contains("ux_connector_collection_health_instance", StringComparison.Ordinal)
                || cause.Message.Contains("ux_connector_collection_health_scope", StringComparison.Ordinal));
        }

        return false;
    }

    public sealed class CollectionHealthUniqueConstraintException : Exception;
}
