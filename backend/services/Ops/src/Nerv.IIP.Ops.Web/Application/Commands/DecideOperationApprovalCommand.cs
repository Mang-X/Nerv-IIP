using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record ApproveOperationApprovalCommand(
    string OperationTaskId,
    DecideOperationApprovalRequest Request,
    DateTimeOffset Now) : ICommand<OperationTaskResponse>;

public sealed class ApproveOperationApprovalCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<ApproveOperationApprovalCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(ApproveOperationApprovalCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.ApproveAsync(
            request.OperationTaskId,
            request.Request,
            request.Now,
            cancellationToken);
    }
}

public sealed record RejectOperationApprovalCommand(
    string OperationTaskId,
    DecideOperationApprovalRequest Request,
    DateTimeOffset Now) : ICommand<OperationTaskResponse>;

public sealed class RejectOperationApprovalCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<RejectOperationApprovalCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(RejectOperationApprovalCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.RejectAsync(
            request.OperationTaskId,
            request.Request,
            request.Now,
            cancellationToken);
    }
}
