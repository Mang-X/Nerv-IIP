using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.Contracts.ConnectorProtocol;
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
    }
}
