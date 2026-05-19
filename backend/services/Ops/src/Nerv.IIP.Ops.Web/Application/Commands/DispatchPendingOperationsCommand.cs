using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record DispatchPendingOperationsCommand(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    int Take,
    int LeaseDurationSeconds,
    int MaxAttempts,
    DateTimeOffset Now) : ICommand<PendingOperationTasksResponse>;

public sealed class DispatchPendingOperationsCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<DispatchPendingOperationsCommand, PendingOperationTasksResponse>
{
    public async Task<PendingOperationTasksResponse> Handle(DispatchPendingOperationsCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.ClaimPendingAsync(
            new ClaimOperationTasksRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId,
                request.Take,
                request.LeaseDurationSeconds,
                request.MaxAttempts),
            request.Now,
            cancellationToken);
    }
}
