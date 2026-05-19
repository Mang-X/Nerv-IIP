using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.Commands;

public sealed record RefreshInstanceStateAfterOperationCommand(OperationTaskCompletedIntegrationEvent IntegrationEvent) : ICommand<bool>;
public sealed record RefreshInstanceStateAfterFailedOperationCommand(OperationTaskFailedIntegrationEvent IntegrationEvent) : ICommand<bool>;

public sealed class RefreshInstanceStateAfterOperationCommandHandler(IServiceProvider services)
    : ICommandHandler<RefreshInstanceStateAfterOperationCommand, bool>
{
    public async Task<bool> Handle(RefreshInstanceStateAfterOperationCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<ApplicationDbContext>() is null)
        {
            return false;
        }

        var integrationEvent = request.IntegrationEvent;
        var repository = services.GetRequiredService<IApplicationInstanceRepository>();
        var instance = await repository.GetByContextAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.InstanceKey,
            cancellationToken)
            ?? throw new KnownException($"Instance context is invalid: {integrationEvent.Payload.InstanceKey}");

        return instance.RecordOperationTaskCompletedRefresh(
            integrationEvent.IdempotencyKey,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.OperationCode,
            integrationEvent.Payload.FinishedAtUtc,
            integrationEvent.CorrelationId);
    }
}

public sealed class RefreshInstanceStateAfterFailedOperationCommandHandler(IServiceProvider services)
    : ICommandHandler<RefreshInstanceStateAfterFailedOperationCommand, bool>
{
    public async Task<bool> Handle(RefreshInstanceStateAfterFailedOperationCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<ApplicationDbContext>() is null)
        {
            return false;
        }

        var integrationEvent = request.IntegrationEvent;
        var repository = services.GetRequiredService<IApplicationInstanceRepository>();
        var instance = await repository.GetByContextAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.InstanceKey,
            cancellationToken)
            ?? throw new KnownException($"Instance context is invalid: {integrationEvent.Payload.InstanceKey}");

        return instance.RecordOperationTaskFailedRefresh(
            integrationEvent.IdempotencyKey,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.OperationCode,
            integrationEvent.Payload.FinishedAtUtc,
            integrationEvent.CorrelationId,
            integrationEvent.Payload.FailureCode);
    }
}
