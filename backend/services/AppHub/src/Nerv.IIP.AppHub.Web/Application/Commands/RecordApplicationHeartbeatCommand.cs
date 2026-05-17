using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.Commands;

public record RecordApplicationHeartbeatCommand(ApplicationHeartbeat Heartbeat) : ICommand;

public class RecordApplicationHeartbeatCommandHandler(IServiceProvider services)
    : ICommandHandler<RecordApplicationHeartbeatCommand>
{
    public async Task Handle(RecordApplicationHeartbeatCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<ApplicationDbContext>() is null)
        {
            services.GetRequiredService<IAppHubStateStore>().RecordHeartbeat(request.Heartbeat);
            return;
        }

        var heartbeat = request.Heartbeat;
        var instanceRepository = services.GetRequiredService<IApplicationInstanceRepository>();
        var instance = await instanceRepository.GetByContextAsync(
            heartbeat.Context.OrganizationId,
            heartbeat.Context.EnvironmentId,
            heartbeat.InstanceKey,
            cancellationToken)
            ?? throw new KnownException($"Instance context is invalid: {heartbeat.InstanceKey}");

        instance.RecordHeartbeat(heartbeat.HeartbeatAtUtc, heartbeat.Reachable, heartbeat.LatencyMs);
    }
}
