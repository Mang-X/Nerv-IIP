using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record DispatchPendingOperationsCommand(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    int Take,
    DateTimeOffset Now) : ICommand<PendingOperationTasksResponse>;

public sealed class DispatchPendingOperationsCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<DispatchPendingOperationsCommand, PendingOperationTasksResponse>
{
    public async Task<PendingOperationTasksResponse> Handle(DispatchPendingOperationsCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.DispatchPendingAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId,
            request.Take,
            request.Now,
            cancellationToken);
    }
}
