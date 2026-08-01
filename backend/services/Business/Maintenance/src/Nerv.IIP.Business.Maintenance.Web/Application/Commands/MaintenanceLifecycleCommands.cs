using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Commands;

public sealed record AssignMaintenanceWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    MaintenanceWorkOrderId WorkOrderId,
    string ActorPrincipalId,
    string? TechnicianUserId,
    string? TeamId,
    string Reason,
    string IdempotencyKey,
    int ExpectedVersion) : ICommand<MaintenanceWorkOrderCommandResult>;

public sealed record TransitionMaintenanceWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    MaintenanceWorkOrderId WorkOrderId,
    MaintenanceWorkOrderAction Action,
    string ActorPrincipalId,
    string Reason,
    string IdempotencyKey,
    int ExpectedVersion,
    string? Result = null,
    string? DowntimeReasonCode = null,
    int? DowntimeMinutes = null,
    IReadOnlyCollection<MaintenanceSparePartInput>? SpareParts = null,
    int? ActualLaborMinutes = null,
    decimal? SparePartCostAmount = null,
    decimal? ExternalServiceCostAmount = null,
    string? CostCurrencyCode = null) : ICommand<MaintenanceWorkOrderCommandResult>;

public sealed class AssignMaintenanceWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AssignMaintenanceWorkOrderCommand, MaintenanceWorkOrderCommandResult>
{
    public async Task<MaintenanceWorkOrderCommandResult> Handle(
        AssignMaintenanceWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(request);
        var replay = await LifecycleReplay.FindAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.IdempotencyKey, fingerprint, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var workOrder = await FindWorkOrderAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.WorkOrderId, cancellationToken);
        EnsureVersion(workOrder, request.ExpectedVersion, MaintenanceWorkOrderAction.Assign);
        var fromStatus = workOrder.Status;
        try
        {
            workOrder.Assign(request.TechnicianUserId, request.TeamId);
        }
        catch (InvalidOperationException)
        {
            throw new MaintenanceLifecycleConflictException("assign", fromStatus.ToString());
        }

        return Record(
            dbContext, workOrder, MaintenanceWorkOrderAction.Assign, fromStatus, request.ActorPrincipalId,
            request.TechnicianUserId, request.TeamId, request.Reason, request.IdempotencyKey, fingerprint);
    }

    internal static async Task<MaintenanceWorkOrder> FindWorkOrderAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        MaintenanceWorkOrderId workOrderId,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.MaintenanceWorkOrders.Include(x => x.SparePartLines).SingleOrDefaultAsync(
            x => x.Id == workOrderId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId,
            cancellationToken);
        return workOrder ?? throw new KnownException($"Maintenance work order was not found: {workOrderId}");
    }

    internal static void EnsureVersion(
        MaintenanceWorkOrder workOrder,
        int expectedVersion,
        MaintenanceWorkOrderAction action)
    {
        if (workOrder.Version != expectedVersion)
        {
            throw new MaintenanceLifecycleConflictException(action.ToString().ToLowerInvariant(), workOrder.Status.ToString());
        }
    }

    internal static MaintenanceWorkOrderCommandResult Record(
        ApplicationDbContext dbContext,
        MaintenanceWorkOrder workOrder,
        MaintenanceWorkOrderAction action,
        MaintenanceWorkOrderStatus fromStatus,
        string actorPrincipalId,
        string? technicianUserId,
        string? teamId,
        string reason,
        string idempotencyKey,
        string fingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        var changedAtUtc = new DateTimeOffset(now.Ticks - (now.Ticks % 10), TimeSpan.Zero);
        dbContext.MaintenanceWorkOrderLifecycleEvents.Add(MaintenanceWorkOrderLifecycleEvent.Record(
            workOrder,
            action,
            fromStatus,
            actorPrincipalId,
            technicianUserId,
            teamId,
            reason,
            idempotencyKey,
            fingerprint,
            changedAtUtc));
        return new MaintenanceWorkOrderCommandResult(workOrder.Id, workOrder.Status, changedAtUtc, workOrder.Version);
    }

    internal static string Fingerprint(AssignMaintenanceWorkOrderCommand request) =>
        MaintenanceIdempotencyFingerprints.Hash(new
        {
            WorkOrderId = request.WorkOrderId.ToString(),
            request.ActorPrincipalId,
            TechnicianUserId = request.TechnicianUserId?.Trim(),
            TeamId = request.TeamId?.Trim(),
            Reason = request.Reason.Trim(),
            request.ExpectedVersion,
        });
}

public sealed class TransitionMaintenanceWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<TransitionMaintenanceWorkOrderCommand, MaintenanceWorkOrderCommandResult>
{
    public async Task<MaintenanceWorkOrderCommandResult> Handle(
        TransitionMaintenanceWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(request);
        var replay = await LifecycleReplay.FindAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.IdempotencyKey, fingerprint, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var workOrder = await AssignMaintenanceWorkOrderCommandHandler.FindWorkOrderAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.WorkOrderId, cancellationToken);
        AssignMaintenanceWorkOrderCommandHandler.EnsureVersion(workOrder, request.ExpectedVersion, request.Action);
        var fromStatus = workOrder.Status;
        try
        {
            EnsureActorOwnsAction(workOrder, request);
            await ApplyAsync(workOrder, request, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new MaintenanceLifecycleConflictException(request.Action.ToString().ToLowerInvariant(), fromStatus.ToString());
        }

        return AssignMaintenanceWorkOrderCommandHandler.Record(
            dbContext,
            workOrder,
            request.Action,
            fromStatus,
            request.ActorPrincipalId,
            workOrder.AssignedTechnicianUserId,
            workOrder.AssignedTeamId,
            request.Reason,
            request.IdempotencyKey,
            fingerprint);
    }

    private async Task ApplyAsync(
        MaintenanceWorkOrder workOrder,
        TransitionMaintenanceWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        switch (request.Action)
        {
            case MaintenanceWorkOrderAction.Accept:
                workOrder.Accept(request.ActorPrincipalId);
                break;
            case MaintenanceWorkOrderAction.Start:
                workOrder.StartWork();
                break;
            case MaintenanceWorkOrderAction.Pause:
                workOrder.Pause(waitingForParts: false);
                break;
            case MaintenanceWorkOrderAction.WaitForParts:
                workOrder.Pause(waitingForParts: true);
                break;
            case MaintenanceWorkOrderAction.Resume:
                workOrder.Resume();
                break;
            case MaintenanceWorkOrderAction.Complete:
                var reasonCode = MaintenanceText.Required(request.DowntimeReasonCode ?? string.Empty, nameof(request.DowntimeReasonCode));
                if (!await dbContext.DowntimeReasons.AnyAsync(
                        x => x.OrganizationId == workOrder.OrganizationId
                            && x.EnvironmentId == workOrder.EnvironmentId
                            && x.ReasonCode == reasonCode,
                        cancellationToken))
                {
                    throw new KnownException($"Downtime reason was not found: {reasonCode}");
                }
                workOrder.Finish(
                    MaintenanceText.Required(request.Result ?? string.Empty, nameof(request.Result)),
                    reasonCode,
                    request.DowntimeMinutes ?? throw new KnownException("Downtime minutes are required."),
                    request.SpareParts?.Select(x => new SparePartLineDraft(x.SkuCode, x.Quantity, x.UomCode)).ToArray(),
                    request.ActorPrincipalId,
                    request.ActualLaborMinutes,
                    request.SparePartCostAmount,
                    request.ExternalServiceCostAmount,
                    request.CostCurrencyCode);
                break;
            case MaintenanceWorkOrderAction.Verify:
                workOrder.Verify();
                break;
            case MaintenanceWorkOrderAction.Close:
                workOrder.Close();
                break;
            case MaintenanceWorkOrderAction.Cancel:
                workOrder.Cancel();
                break;
            default:
                throw new MaintenanceLifecycleConflictException(request.Action.ToString(), workOrder.Status.ToString());
        }
    }

    private static void EnsureActorOwnsAction(
        MaintenanceWorkOrder workOrder,
        TransitionMaintenanceWorkOrderCommand request)
    {
        if (request.Action is not (
                MaintenanceWorkOrderAction.Accept
                or MaintenanceWorkOrderAction.Start
                or MaintenanceWorkOrderAction.Pause
                or MaintenanceWorkOrderAction.WaitForParts
                or MaintenanceWorkOrderAction.Resume
                or MaintenanceWorkOrderAction.Complete))
        {
            return;
        }

        if (request.Action == MaintenanceWorkOrderAction.Accept
            && string.IsNullOrWhiteSpace(workOrder.AssignedTechnicianUserId))
        {
            return;
        }

        if (!string.Equals(workOrder.AssignedTechnicianUserId, request.ActorPrincipalId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The lifecycle action is reserved for the assigned technician.");
        }
    }

    private static string Fingerprint(TransitionMaintenanceWorkOrderCommand request) =>
        MaintenanceIdempotencyFingerprints.Hash(new
        {
            WorkOrderId = request.WorkOrderId.ToString(),
            Action = request.Action.ToString(),
            request.ActorPrincipalId,
            Reason = request.Reason.Trim(),
            request.ExpectedVersion,
            Result = request.Result?.Trim(),
            DowntimeReasonCode = request.DowntimeReasonCode?.Trim(),
            request.DowntimeMinutes,
            request.ActualLaborMinutes,
            SparePartCostAmount = MaintenanceIdempotencyFingerprints.CanonicalDecimal(request.SparePartCostAmount),
            ExternalServiceCostAmount = MaintenanceIdempotencyFingerprints.CanonicalDecimal(request.ExternalServiceCostAmount),
            CostCurrencyCode = request.CostCurrencyCode?.Trim(),
            SparePartsSpecified = request.SpareParts is not null,
            SpareParts = (request.SpareParts ?? []).Select(x => new
            {
                SkuCode = x.SkuCode.Trim(),
                Quantity = MaintenanceIdempotencyFingerprints.CanonicalDecimal(x.Quantity),
                UomCode = x.UomCode?.Trim(),
            }).OrderBy(x => x.SkuCode, StringComparer.Ordinal).ThenBy(x => x.UomCode, StringComparer.Ordinal).ToArray(),
        });
}

internal static class LifecycleReplay
{
    public static async Task<MaintenanceWorkOrderCommandResult?> FindAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var normalizedKey = MaintenanceText.Required(idempotencyKey, nameof(idempotencyKey));
        var existing = dbContext.MaintenanceWorkOrderLifecycleEvents.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.IdempotencyKey == normalizedKey)
            ?? await dbContext.MaintenanceWorkOrderLifecycleEvents.AsNoTracking().SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.IdempotencyKey == normalizedKey,
                cancellationToken);
        if (existing is null)
        {
            return null;
        }
        if (!string.Equals(existing.PayloadFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MaintenanceIdempotencyConflictException();
        }
        return new MaintenanceWorkOrderCommandResult(
            existing.WorkOrderId,
            existing.ToStatus,
            existing.OccurredAtUtc,
            existing.ResultingVersion);
    }
}

public sealed class AssignMaintenanceWorkOrderCommandValidator : AbstractValidator<AssignMaintenanceWorkOrderCommand>
{
    public AssignMaintenanceWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TechnicianUserId).MaximumLength(150);
        RuleFor(x => x.TeamId).MaximumLength(150);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.TechnicianUserId) || !string.IsNullOrWhiteSpace(x.TeamId));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class TransitionMaintenanceWorkOrderCommandValidator : AbstractValidator<TransitionMaintenanceWorkOrderCommand>
{
    public TransitionMaintenanceWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.Action).IsInEnum().NotEqual(MaintenanceWorkOrderAction.Assign);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        When(x => x.Action == MaintenanceWorkOrderAction.Complete, () =>
        {
            RuleFor(x => x.Result).NotEmpty().MaximumLength(1000);
            RuleFor(x => x.DowntimeReasonCode).NotEmpty().MaximumLength(100);
            RuleFor(x => x.DowntimeMinutes).NotNull().GreaterThan(0);
        });
    }
}

public sealed class AssignMaintenanceWorkOrderCommandLock : ICommandLock<AssignMaintenanceWorkOrderCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(AssignMaintenanceWorkOrderCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(MaintenanceLifecycleCommandLockKeys.For(
            command.OrganizationId,
            command.EnvironmentId,
            command.WorkOrderId,
            command.IdempotencyKey));
}

public sealed class TransitionMaintenanceWorkOrderCommandLock : ICommandLock<TransitionMaintenanceWorkOrderCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(TransitionMaintenanceWorkOrderCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(MaintenanceLifecycleCommandLockKeys.For(
            command.OrganizationId,
            command.EnvironmentId,
            command.WorkOrderId,
            command.IdempotencyKey));
}

internal static class MaintenanceLifecycleCommandLockKeys
{
    public static CommandLockSettings For(
        string organizationId,
        string environmentId,
        MaintenanceWorkOrderId workOrderId,
        string idempotencyKey)
    {
        var intentKey = string.Join(
            ':',
            "business-maintenance:lifecycle-idempotency",
            Uri.EscapeDataString(MaintenanceText.Required(organizationId, nameof(organizationId))),
            Uri.EscapeDataString(MaintenanceText.Required(environmentId, nameof(environmentId))),
            Uri.EscapeDataString(MaintenanceText.Required(idempotencyKey, nameof(idempotencyKey))));
        return new CommandLockSettings(
            new[] { MaintenanceWorkOrderCommandLockKeys.For(workOrderId), intentKey }
                .Order(StringComparer.Ordinal),
            30);
    }
}
