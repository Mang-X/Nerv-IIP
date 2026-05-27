using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

public sealed record MesAcceptedResponse(string Status, string ReferenceId, DateTimeOffset AcceptedAtUtc);

public sealed record MesOperationActionResponse(
    string OperationTaskId,
    string Status,
    DateTimeOffset ChangedAtUtc);

public sealed record ReleaseWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    DateTimeOffset ReleasedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class ReleaseWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReleaseWorkOrderCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ReleaseWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken);

        if (!exists)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        return new MesAcceptedResponse("Accepted", request.WorkOrderId, request.ReleasedAtUtc);
    }
}

public sealed record ConvertPlanToWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string ProductionPlanId,
    string? WorkOrderId,
    DateTimeOffset RequestedAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class ConvertPlanToWorkOrderCommandHandler(MesNumberingService? numberingService = null)
    : ICommandHandler<ConvertPlanToWorkOrderCommand, MesAcceptedResponse>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<MesAcceptedResponse> Handle(ConvertPlanToWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateWorkOrderIdAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.IdempotencyKey,
            $"{request.ProductionPlanId}|{request.WorkOrderId}",
            cancellationToken);

        return new MesAcceptedResponse("Accepted", allocation.Number, request.RequestedAtUtc);
    }
}

public sealed record CreateMaterialIssueRequestCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string? MaterialId,
    decimal? Quantity,
    DateTimeOffset RequestedAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class CreateMaterialIssueRequestCommandValidator : AbstractValidator<CreateMaterialIssueRequestCommand>
{
    public CreateMaterialIssueRequestCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0).When(x => x.Quantity.HasValue);
    }
}

public sealed class CreateMaterialIssueRequestCommandHandler(ApplicationDbContext dbContext, MesNumberingService? numberingService = null)
    : ICommandHandler<CreateMaterialIssueRequestCommand, MesAcceptedResponse>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<MesAcceptedResponse> Handle(CreateMaterialIssueRequestCommand request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken);

        if (!exists)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "material-issue-request",
            "MIR",
            null,
            request.IdempotencyKey,
            MesNumberingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.MaterialId, request.Quantity, request.RequestedAtUtc),
            cancellationToken);
        return new MesAcceptedResponse("Accepted", allocation.Number, request.RequestedAtUtc);
    }
}

public sealed record ConfirmLineSideMaterialReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string RequestId,
    DateTimeOffset ReceivedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class ConfirmLineSideMaterialReceiptCommandHandler
    : ICommandHandler<ConfirmLineSideMaterialReceiptCommand, MesAcceptedResponse>
{
    public Task<MesAcceptedResponse> Handle(ConfirmLineSideMaterialReceiptCommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesAcceptedResponse("Accepted", request.RequestId, request.ReceivedAtUtc));
    }
}

public sealed record AssignDispatchTaskCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    DateTimeOffset AssignedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class AssignDispatchTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AssignDispatchTaskCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(AssignDispatchTaskCommand request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.OperationTasks.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken);

        if (!exists)
        {
            throw new KnownException($"未找到工序任务，OperationTaskId = {request.OperationTaskId}");
        }

        return new MesAcceptedResponse("Accepted", request.OperationTaskId, request.AssignedAtUtc);
    }
}

public sealed record ChangeOperationTaskStateCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string Action,
    DateTimeOffset ChangedAtUtc) : ICommand<MesOperationActionResponse>;

public sealed class ChangeOperationTaskStateCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ChangeOperationTaskStateCommand, MesOperationActionResponse>
{
    public async Task<MesOperationActionResponse> Handle(ChangeOperationTaskStateCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken)
            ?? throw new KnownException($"未找到工序任务，OperationTaskId = {request.OperationTaskId}");

        switch (request.Action)
        {
            case "start":
                task.Start(request.ChangedAtUtc);
                break;
            case "pause":
                task.Pause();
                break;
            case "resume":
                task.Resume(request.ChangedAtUtc);
                break;
            case "complete":
                task.Complete(request.ChangedAtUtc);
                break;
            default:
                throw new KnownException($"不支持的工序动作：{request.Action}");
        }

        return new MesOperationActionResponse(task.OperationTaskIdValue, task.Status.ToString(), request.ChangedAtUtc);
    }
}

public sealed record RecordDefectCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset RecordedAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class RecordDefectCommandHandler(ApplicationDbContext dbContext, MesNumberingService? numberingService = null)
    : ICommandHandler<RecordDefectCommand, MesAcceptedResponse>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<MesAcceptedResponse> Handle(RecordDefectCommand request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken);

        if (!exists)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "defect",
            "DEF",
            null,
            request.IdempotencyKey,
            MesNumberingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.DefectCode, request.Quantity, request.RecordedAtUtc),
            cancellationToken);
        return new MesAcceptedResponse("Accepted", allocation.Number, request.RecordedAtUtc);
    }
}

public sealed record RecordDowntimeEventCommand(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string? OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class RecordDowntimeEventCommandHandler(ApplicationDbContext dbContext, MesNumberingService? numberingService = null)
    : ICommandHandler<RecordDowntimeEventCommand, MesAcceptedResponse>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<MesAcceptedResponse> Handle(RecordDowntimeEventCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "downtime-event",
            "DOWNTIME",
            null,
            request.IdempotencyKey,
            MesNumberingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.WorkCenterId, request.DeviceAssetId, request.Reason, request.FromUtc, request.ToUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MesAcceptedResponse("Accepted", allocation.Number, request.FromUtc);
        }

        var downtime = WorkCenterUnavailability.Open(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.WorkCenterId,
            request.FromUtc,
            request.ToUtc,
            request.Reason,
            request.DeviceAssetId);
        dbContext.WorkCenterUnavailabilities.Add(downtime);
        await Task.CompletedTask;
        return new MesAcceptedResponse("Accepted", downtime.DowntimeEventNo, request.FromUtc);
    }
}

public sealed record ConfirmDowntimeRecoveryCommand(
    string OrganizationId,
    string EnvironmentId,
    string DowntimeEventId,
    DateTimeOffset RecoveredAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class ConfirmDowntimeRecoveryCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ConfirmDowntimeRecoveryCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ConfirmDowntimeRecoveryCommand request, CancellationToken cancellationToken)
    {
        var downtime = await dbContext.WorkCenterUnavailabilities.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                (x.Id.Id.ToString() == request.DowntimeEventId ||
                    x.DowntimeEventNo == request.DowntimeEventId),
            cancellationToken)
            ?? throw new KnownException($"未找到停机事件，DowntimeEventId = {request.DowntimeEventId}");

        downtime.Close(request.RecoveredAtUtc);
        return new MesAcceptedResponse("Accepted", request.DowntimeEventId, request.RecoveredAtUtc);
    }
}

public sealed record CreateShiftHandoverCommand(
    string OrganizationId,
    string EnvironmentId,
    string ShiftId,
    string TeamId,
    DateTimeOffset HandoverAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class CreateShiftHandoverCommandHandler(MesNumberingService? numberingService = null)
    : ICommandHandler<CreateShiftHandoverCommand, MesAcceptedResponse>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<MesAcceptedResponse> Handle(CreateShiftHandoverCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "shift-handover",
            "SHO",
            null,
            request.IdempotencyKey,
            MesNumberingService.Fingerprint(request.ShiftId, request.TeamId, request.HandoverAtUtc),
            cancellationToken);
        return new MesAcceptedResponse("Accepted", allocation.Number, request.HandoverAtUtc);
    }
}

public sealed record AcceptShiftHandoverCommand(
    string OrganizationId,
    string EnvironmentId,
    string HandoverId,
    DateTimeOffset AcceptedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class AcceptShiftHandoverCommandHandler
    : ICommandHandler<AcceptShiftHandoverCommand, MesAcceptedResponse>
{
    public Task<MesAcceptedResponse> Handle(AcceptShiftHandoverCommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesAcceptedResponse("Accepted", request.HandoverId, request.AcceptedAtUtc));
    }
}
