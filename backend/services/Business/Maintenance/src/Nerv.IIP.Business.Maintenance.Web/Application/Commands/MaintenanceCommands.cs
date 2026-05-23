using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Commands;

public sealed record MaintenanceSparePartInput(string SkuCode, decimal Quantity, string? UomCode);

public sealed record CreateMaintenanceWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string Priority,
    string? SourceAlarmId,
    string OpenedBy,
    string? AssetUnavailableReason) : ICommand<MaintenanceWorkOrderId>;

public sealed class CreateMaintenanceWorkOrderCommandValidator : AbstractValidator<CreateMaintenanceWorkOrderCommand>
{
    public CreateMaintenanceWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Priority).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceAlarmId).MaximumLength(150);
        RuleFor(x => x.OpenedBy).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AssetUnavailableReason).MaximumLength(500);
    }
}

public sealed class CreateMaintenanceWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateMaintenanceWorkOrderCommand, MaintenanceWorkOrderId>
{
    public async Task<MaintenanceWorkOrderId> Handle(CreateMaintenanceWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceAlarmId))
        {
            var existing = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.SourceAlarmId == request.SourceAlarmId,
                cancellationToken);
            if (existing is not null)
            {
                return existing.Id;
            }
        }

        var workOrder = string.IsNullOrWhiteSpace(request.SourceAlarmId)
            ? MaintenanceWorkOrder.OpenManual(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.Priority, request.OpenedBy)
            : MaintenanceWorkOrder.OpenFromAlarm(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.SourceAlarmId, request.Priority, request.OpenedBy);

        if (!string.IsNullOrWhiteSpace(request.AssetUnavailableReason))
        {
            workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, request.AssetUnavailableReason);
        }

        dbContext.MaintenanceWorkOrders.Add(workOrder);
        return workOrder.Id;
    }
}

public sealed record CompleteMaintenanceWorkOrderCommand(
    MaintenanceWorkOrderId WorkOrderId,
    string Result,
    string DowntimeReasonCode,
    int DowntimeMinutes,
    IReadOnlyCollection<MaintenanceSparePartInput> SpareParts) : ICommand;

public sealed class CompleteMaintenanceWorkOrderCommandValidator : AbstractValidator<CompleteMaintenanceWorkOrderCommand>
{
    public CompleteMaintenanceWorkOrderCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.Result).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.DowntimeReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DowntimeMinutes).GreaterThan(0);
        RuleForEach(x => x.SpareParts).ChildRules(x =>
        {
            x.RuleFor(p => p.SkuCode).NotEmpty().MaximumLength(100);
            x.RuleFor(p => p.Quantity).GreaterThan(0);
            x.RuleFor(p => p.UomCode).MaximumLength(50);
        });
    }
}

public sealed class CompleteMaintenanceWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteMaintenanceWorkOrderCommand>
{
    public async Task Handle(CompleteMaintenanceWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.MaintenanceWorkOrders.Include(x => x.SparePartLines).SingleOrDefaultAsync(x => x.Id == request.WorkOrderId, cancellationToken)
            ?? throw new KnownException($"Maintenance work order was not found: {request.WorkOrderId}");
        workOrder.Complete(
            request.Result,
            request.DowntimeReasonCode,
            request.DowntimeMinutes,
            request.SpareParts.Select(x => new SparePartLineDraft(x.SkuCode, x.Quantity, x.UomCode)));
    }
}

public sealed record CreateMaintenancePlanCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string PlanCode,
    string Interval,
    DateOnly StartsOn,
    string Owner) : ICommand<MaintenancePlanId>;

public sealed class CreateMaintenancePlanCommandValidator : AbstractValidator<CreateMaintenancePlanCommand>
{
    public CreateMaintenancePlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Interval).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(150);
    }
}

public sealed class CreateMaintenancePlanCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateMaintenancePlanCommand, MaintenancePlanId>
{
    public async Task<MaintenancePlanId> Handle(CreateMaintenancePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = MaintenancePlan.Create(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.PlanCode, request.Interval, request.StartsOn, request.Owner);
        dbContext.MaintenancePlans.Add(plan);
        await Task.CompletedTask;
        return plan.Id;
    }
}

public sealed record RecordMaintenanceInspectionCommand(
    string OrganizationId,
    string EnvironmentId,
    MaintenancePlanId? PlanId,
    MaintenanceWorkOrderId? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc) : ICommand<MaintenanceInspectionId>;

public sealed class RecordMaintenanceInspectionCommandValidator : AbstractValidator<RecordMaintenanceInspectionCommand>
{
    public RecordMaintenanceInspectionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Inspector).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Result).NotEmpty().MaximumLength(1000);
        RuleFor(x => x).Must(x => x.PlanId is not null || x.WorkOrderId is not null).WithMessage("Inspection must reference a maintenance plan or work order.");
    }
}

public sealed class RecordMaintenanceInspectionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordMaintenanceInspectionCommand, MaintenanceInspectionId>
{
    public async Task<MaintenanceInspectionId> Handle(RecordMaintenanceInspectionCommand request, CancellationToken cancellationToken)
    {
        var inspection = MaintenanceInspection.Record(request.OrganizationId, request.EnvironmentId, request.PlanId, request.WorkOrderId, request.Inspector, request.Result, request.InspectedAtUtc);
        dbContext.MaintenanceInspections.Add(inspection);
        await Task.CompletedTask;
        return inspection.Id;
    }
}
