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

public sealed record MarkMaintenanceWorkOrderAlarmClearedCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceAlarmId,
    DateTimeOffset ClearedAtUtc) : ICommand;

public sealed class MarkMaintenanceWorkOrderAlarmClearedCommandValidator : AbstractValidator<MarkMaintenanceWorkOrderAlarmClearedCommand>
{
    public MarkMaintenanceWorkOrderAlarmClearedCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceAlarmId).NotEmpty().MaximumLength(150);
    }
}

public sealed class MarkMaintenanceWorkOrderAlarmClearedCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkMaintenanceWorkOrderAlarmClearedCommand>
{
    public async Task Handle(MarkMaintenanceWorkOrderAlarmClearedCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceAlarmId == request.SourceAlarmId
                && x.Status == MaintenanceWorkOrderStatus.Open,
            cancellationToken);
        workOrder?.MarkAlarmCleared(request.ClearedAtUtc);
    }
}

public sealed record GenerateDueMaintenanceWorkOrdersCommand(
    string OrganizationId,
    string EnvironmentId,
    DateOnly BusinessDate,
    string OpenedBy) : ICommand<GenerateDueMaintenanceWorkOrdersResult>;

public sealed record GenerateDueMaintenanceWorkOrdersResult(int GeneratedCount, IReadOnlyCollection<MaintenanceWorkOrderId> WorkOrderIds);

public sealed class GenerateDueMaintenanceWorkOrdersCommandValidator : AbstractValidator<GenerateDueMaintenanceWorkOrdersCommand>
{
    public GenerateDueMaintenanceWorkOrdersCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OpenedBy).NotEmpty().MaximumLength(150);
    }
}

public sealed class GenerateDueMaintenanceWorkOrdersCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<GenerateDueMaintenanceWorkOrdersCommand, GenerateDueMaintenanceWorkOrdersResult>
{
    public async Task<GenerateDueMaintenanceWorkOrdersResult> Handle(GenerateDueMaintenanceWorkOrdersCommand request, CancellationToken cancellationToken)
    {
        var duePlans = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.NextDueOn <= request.BusinessDate)
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.PlanCode)
            .ToArrayAsync(cancellationToken);

        var workOrderIds = new List<MaintenanceWorkOrderId>();
        foreach (var plan in duePlans)
        {
            var workOrder = MaintenanceWorkOrder.OpenFromPlan(
                plan.OrganizationId,
                plan.EnvironmentId,
                plan.DeviceAssetId,
                plan.PlanCode,
                request.OpenedBy);
            dbContext.MaintenanceWorkOrders.Add(workOrder);
            plan.MarkGenerated(request.BusinessDate);
            workOrderIds.Add(workOrder.Id);
        }

        return new GenerateDueMaintenanceWorkOrdersResult(workOrderIds.Count, workOrderIds);
    }
}

public sealed record CreateMaintenanceSparePartCommand(
    string OrganizationId,
    string EnvironmentId,
    MaintenanceWorkOrderId WorkOrderId,
    string SkuCode,
    decimal Quantity,
    string? UomCode) : ICommand<SparePartLineId>;

public sealed class CreateMaintenanceSparePartCommandValidator : AbstractValidator<CreateMaintenanceSparePartCommand>
{
    public CreateMaintenanceSparePartCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UomCode).MaximumLength(50);
    }
}

public sealed class CreateMaintenanceSparePartCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateMaintenanceSparePartCommand, SparePartLineId>
{
    public async Task<SparePartLineId> Handle(CreateMaintenanceSparePartCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.MaintenanceWorkOrders
            .Include(x => x.SparePartLines)
            .SingleOrDefaultAsync(
                x => x.Id == request.WorkOrderId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"Maintenance work order was not found: {request.WorkOrderId}");
        if (workOrder.Status != MaintenanceWorkOrderStatus.Open)
        {
            throw new KnownException("Completed maintenance work orders are immutable.");
        }

        var sparePart = workOrder.AddSparePartLine(new SparePartLineDraft(request.SkuCode, request.Quantity, request.UomCode));
        return sparePart.Id;
    }
}

public sealed record CreateMaintenancePlanCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? PlanCode,
    string Interval,
    DateOnly StartsOn,
    string Owner,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    string? IdempotencyKey = null) : ICommand<MaintenancePlanId>;

public sealed class CreateMaintenancePlanCommandValidator : AbstractValidator<CreateMaintenancePlanCommand>
{
    public CreateMaintenancePlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PlanCode).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
        RuleFor(x => x.Interval).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(150);
        RuleFor(x => x)
            .Must(x => (x.WindowStartUtc is null) == (x.WindowEndUtc is null))
            .WithMessage("Maintenance availability window start and end must be provided together.");
        RuleFor(x => x)
            .Must(x => x.WindowStartUtc is null || x.WindowEndUtc is null || x.WindowEndUtc > x.WindowStartUtc)
            .WithMessage("Maintenance availability window end must be after start.");
    }
}

public sealed class CreateMaintenancePlanCommandHandler(
    ApplicationDbContext dbContext,
    MaintenanceCodingService? codingService = null)
    : ICommandHandler<CreateMaintenancePlanCommand, MaintenancePlanId>
{
    private readonly MaintenanceCodingService _codingService = codingService ?? new MaintenanceCodingService();

    public async Task<MaintenancePlanId> Handle(CreateMaintenancePlanCommand request, CancellationToken cancellationToken)
    {
        if ((request.WindowStartUtc is null) != (request.WindowEndUtc is null))
        {
            throw new KnownException("Maintenance availability window start and end must be provided together.");
        }

        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "maintenance-plan",
            request.PlanCode,
            request.IdempotencyKey,
            MaintenanceCodingService.Fingerprint(
                request.DeviceAssetId,
                request.Interval,
                request.StartsOn,
                request.Owner,
                request.WindowStartUtc,
                request.WindowEndUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var persisted = await dbContext.MaintenancePlans.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.PlanCode == allocation.Code,
                cancellationToken);
            if (persisted is null)
            {
                throw new KnownException($"Maintenance plan '{allocation.Code}' idempotency record exists but resource was not found.");
            }

            return persisted.Id;
        }

        var windowStartUtc = request.WindowStartUtc?.ToUniversalTime();
        var windowEndUtc = request.WindowEndUtc?.ToUniversalTime();
        var plan = MaintenancePlan.Create(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, allocation.Code, request.Interval, request.StartsOn, request.Owner, windowStartUtc, windowEndUtc);
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
        var inspection = MaintenanceInspection.Record(request.OrganizationId, request.EnvironmentId, request.PlanId, request.WorkOrderId, request.Inspector, request.Result, request.InspectedAtUtc.ToUniversalTime());
        dbContext.MaintenanceInspections.Add(inspection);
        await Task.CompletedTask;
        return inspection.Id;
    }
}
