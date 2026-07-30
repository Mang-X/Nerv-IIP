using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Commands;

public sealed record MaintenanceSparePartInput(string SkuCode, decimal Quantity, string? UomCode);

public sealed record MaintenanceInspectionMeasurementInput(
    string CharacteristicCode,
    decimal MeasuredValue,
    string UomCode,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit);

public sealed class MaintenanceCompletionOptions
{
    public bool RequireActualLaborMinutes { get; set; }
}

internal static class MaintenanceNumericValidation
{
    private const decimal MaxNumeric18Scale6 = 999_999_999_999.999999m;

    public static bool FitsNumeric18Scale6(decimal value)
    {
        return decimal.Abs(value) <= MaxNumeric18Scale6;
    }

    public static bool FitsNullableNumeric18Scale6(decimal? value)
    {
        return value is null || FitsNumeric18Scale6(value.Value);
    }
}

public sealed record CreateMaintenanceWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string Priority,
    string? SourceAlarmId,
    string OpenedBy,
    string? AssetUnavailableReason,
    string? DiagnosticDescription = null,
    string? FailureModeCode = null,
    string? FailureCauseCode = null,
    string? AssignedTechnicianUserId = null,
    int? EstimatedLaborMinutes = null,
    string? IdempotencyKey = null) : ICommand<MaintenanceWorkOrderCommandResult>;

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
        RuleFor(x => x.DiagnosticDescription).MaximumLength(1000);
        RuleFor(x => x.FailureModeCode).MaximumLength(100);
        RuleFor(x => x.FailureCauseCode).MaximumLength(100);
        RuleFor(x => x.AssignedTechnicianUserId).MaximumLength(150);
        RuleFor(x => x.EstimatedLaborMinutes).GreaterThan(0).When(x => x.EstimatedLaborMinutes is not null);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class CreateMaintenanceWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateMaintenanceWorkOrderCommand, MaintenanceWorkOrderCommandResult>
{
    private const string CreateRuleKey = "maintenance-work-order-create";
    private const string CreateReceiptVersion = "v1";

    public async Task<MaintenanceWorkOrderCommandResult> Handle(
        CreateMaintenanceWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var fingerprint = CreateFingerprint(request);
        var receipt = idempotencyKey is null
            ? null
            : await dbContext.CodeIdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.RuleKey == CreateRuleKey
                    && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (receipt is not null)
        {
            if (!string.Equals(receipt.PayloadFingerprint, fingerprint, StringComparison.Ordinal) ||
                !TryParseCreateReceipt(receipt.Code, out var parsedReceipt))
            {
                throw new MaintenanceIdempotencyConflictException();
            }

            var replayed = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(
                x => x.Id == parsedReceipt.WorkOrderId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
                ?? throw new KnownException("stored-maintenance-work-order-receipt-is-invalid");
            return new MaintenanceWorkOrderCommandResult(
                replayed.Id,
                parsedReceipt.Status,
                parsedReceipt.OpenedAtUtc ?? replayed.OpenedAtUtc);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceAlarmId))
        {
            var existing = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.SourceAlarmId == request.SourceAlarmId,
                cancellationToken);
            if (existing is not null)
            {
                throw new KnownException("source-alarm-already-bound-to-a-different-create-intent");
            }
        }

        var workOrder = string.IsNullOrWhiteSpace(request.SourceAlarmId)
            ? MaintenanceWorkOrder.OpenManual(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.Priority,
                request.OpenedBy,
                request.AssignedTechnicianUserId,
                request.EstimatedLaborMinutes)
            : MaintenanceWorkOrder.OpenFromAlarm(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.SourceAlarmId,
                request.Priority,
                request.OpenedBy,
                request.DiagnosticDescription,
                request.FailureModeCode,
                request.FailureCauseCode,
                request.AssignedTechnicianUserId,
                request.EstimatedLaborMinutes);

        if (!string.IsNullOrWhiteSpace(request.AssetUnavailableReason))
        {
            workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, request.AssetUnavailableReason);
        }

        dbContext.MaintenanceWorkOrders.Add(workOrder);
        if (idempotencyKey is not null)
        {
            dbContext.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
                request.OrganizationId,
                request.EnvironmentId,
                CreateRuleKey,
                idempotencyKey,
                CreateReceiptCode(workOrder),
                fingerprint,
                DateTimeOffset.UtcNow));
        }
        return new MaintenanceWorkOrderCommandResult(workOrder.Id, workOrder.Status, workOrder.OpenedAtUtc);
    }

    private static string CreateFingerprint(CreateMaintenanceWorkOrderCommand request) =>
        MaintenanceIdempotencyFingerprints.Hash(new
        {
            DeviceAssetId = request.DeviceAssetId.Trim(),
            Priority = request.Priority.Trim().ToUpperInvariant(),
            SourceAlarmId = MaintenanceText.Optional(request.SourceAlarmId),
            OpenedBy = request.OpenedBy.Trim(),
            AssetUnavailableReason = MaintenanceText.Optional(request.AssetUnavailableReason),
            DiagnosticDescription = MaintenanceText.Optional(request.DiagnosticDescription),
            FailureModeCode = MaintenanceText.Optional(request.FailureModeCode),
            FailureCauseCode = MaintenanceText.Optional(request.FailureCauseCode),
            AssignedTechnicianUserId = MaintenanceText.Optional(request.AssignedTechnicianUserId),
            request.EstimatedLaborMinutes,
        });

    private static string? NormalizeIdempotencyKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateReceiptCode(MaintenanceWorkOrder workOrder) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{CreateReceiptVersion}|{workOrder.Id}|{workOrder.Status}|{workOrder.OpenedAtUtc:O}");

    private static bool TryParseCreateReceipt(string code, out CreateWorkOrderReceipt receipt)
    {
        if (Guid.TryParse(code, out var legacyWorkOrderId))
        {
            receipt = new CreateWorkOrderReceipt(
                new MaintenanceWorkOrderId(legacyWorkOrderId),
                MaintenanceWorkOrderStatus.Open,
                null);
            return true;
        }

        var parts = code.Split('|');
        if (parts.Length == 4
            && string.Equals(parts[0], CreateReceiptVersion, StringComparison.Ordinal)
            && Guid.TryParse(parts[1], out var workOrderId)
            && Enum.TryParse<MaintenanceWorkOrderStatus>(parts[2], out var status)
            && status == MaintenanceWorkOrderStatus.Open
            && DateTimeOffset.TryParseExact(
                parts[3],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var openedAtUtc))
        {
            receipt = new CreateWorkOrderReceipt(new MaintenanceWorkOrderId(workOrderId), status, openedAtUtc);
            return true;
        }

        receipt = default!;
        return false;
    }

    private sealed record CreateWorkOrderReceipt(
        MaintenanceWorkOrderId WorkOrderId,
        MaintenanceWorkOrderStatus Status,
        DateTimeOffset? OpenedAtUtc);
}

public sealed class CreateMaintenanceWorkOrderCommandLock : ICommandLock<CreateMaintenanceWorkOrderCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        CreateMaintenanceWorkOrderCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operationReference = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? $"legacy:{command.DeviceAssetId.Trim()}"
            : command.IdempotencyKey.Trim();
        return Task.FromResult(new CommandLockSettings(
            $"business-maintenance:work-order-create:{command.OrganizationId}:{command.EnvironmentId}:{operationReference}",
            MaintenancePmCommandLockKeys.AcquireTimeoutSeconds));
    }
}

public sealed record CompleteMaintenanceWorkOrderCommand(
    MaintenanceWorkOrderId WorkOrderId,
    string Result,
    string DowntimeReasonCode,
    int DowntimeMinutes,
    IReadOnlyCollection<MaintenanceSparePartInput> SpareParts,
    int? ActualLaborMinutes = null,
    decimal? SparePartCostAmount = null,
    decimal? ExternalServiceCostAmount = null,
    string? CostCurrencyCode = null,
    string? ActualTechnicianUserId = null,
    string? IdempotencyKey = null,
    string? OrganizationId = null,
    string? EnvironmentId = null) : ICommand<MaintenanceWorkOrderCommandResult>;

public sealed record MaintenanceWorkOrderCommandResult(
    MaintenanceWorkOrderId WorkOrderId,
    MaintenanceWorkOrderStatus Status,
    DateTimeOffset ChangedAtUtc);

public sealed class CompleteMaintenanceWorkOrderCommandLock : ICommandLock<CompleteMaintenanceWorkOrderCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        CompleteMaintenanceWorkOrderCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-maintenance:work-order-complete:{command.WorkOrderId}",
            MaintenancePmCommandLockKeys.AcquireTimeoutSeconds));
    }
}

public sealed class CompleteMaintenanceWorkOrderCommandValidator : AbstractValidator<CompleteMaintenanceWorkOrderCommand>
{
    public CompleteMaintenanceWorkOrderCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.Result).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.DowntimeReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DowntimeMinutes).GreaterThan(0);
        RuleFor(x => x.ActualLaborMinutes).GreaterThan(0).When(x => x.ActualLaborMinutes is not null);
        RuleFor(x => x.SparePartCostAmount)
            .GreaterThanOrEqualTo(0)
            .Must(MaintenanceNumericValidation.FitsNullableNumeric18Scale6)
            .WithMessage("Spare part cost amount must fit numeric(18,6).")
            .When(x => x.SparePartCostAmount is not null);
        RuleFor(x => x.ExternalServiceCostAmount)
            .GreaterThanOrEqualTo(0)
            .Must(MaintenanceNumericValidation.FitsNullableNumeric18Scale6)
            .WithMessage("External service cost amount must fit numeric(18,6).")
            .When(x => x.ExternalServiceCostAmount is not null);
        RuleFor(x => x.CostCurrencyCode).MaximumLength(10);
        RuleFor(x => x.ActualTechnicianUserId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
        RuleForEach(x => x.SpareParts).ChildRules(x =>
        {
            x.RuleFor(p => p.SkuCode).NotEmpty().MaximumLength(100);
            x.RuleFor(p => p.Quantity)
                .GreaterThan(0)
                .Must(MaintenanceNumericValidation.FitsNumeric18Scale6)
                .WithMessage("Spare part quantity must fit numeric(18,6).");
            // The spare part's unit drives the inventory issue posting; it must come from the part's
            // master data. Reject the write instead of letting the integration event converter guess.
            x.RuleFor(p => p.UomCode).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class CompleteMaintenanceWorkOrderCommandHandler(
    ApplicationDbContext dbContext,
    IOptions<MaintenanceCompletionOptions>? completionOptions = null)
    : ICommandHandler<CompleteMaintenanceWorkOrderCommand, MaintenanceWorkOrderCommandResult>
{
    private const string CompleteRuleKey = "maintenance-work-order-complete";

    public async Task<MaintenanceWorkOrderCommandResult> Handle(
        CompleteMaintenanceWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.MaintenanceWorkOrders.Include(x => x.SparePartLines).SingleOrDefaultAsync(
            x => x.Id == request.WorkOrderId
                && (request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
                && (request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId),
            cancellationToken)
            ?? throw new KnownException($"Maintenance work order was not found: {request.WorkOrderId}");
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await TryGetCompletionReplayAsync(workOrder, request, cancellationToken);
            if (replay is not null)
            {
                return replay;
            }
        }

        if (workOrder.Status != MaintenanceWorkOrderStatus.Open)
        {
            throw new MaintenanceLifecycleConflictException("complete", workOrder.Status.ToString());
        }

        var downtimeReasonCode = MaintenanceText.Required(request.DowntimeReasonCode, nameof(request.DowntimeReasonCode));
        var downtimeReasonExists = await dbContext.DowntimeReasons.AnyAsync(
            x => x.OrganizationId == workOrder.OrganizationId
                && x.EnvironmentId == workOrder.EnvironmentId
                && x.ReasonCode == downtimeReasonCode,
            cancellationToken);
        if (!downtimeReasonExists)
        {
            throw new KnownException($"Downtime reason was not found: {downtimeReasonCode}");
        }

        if (completionOptions?.Value.RequireActualLaborMinutes == true && request.ActualLaborMinutes is null)
        {
            throw new KnownException("Actual labor minutes are required to complete this maintenance work order.");
        }

        workOrder.Complete(
            request.Result,
            downtimeReasonCode,
            request.DowntimeMinutes,
            request.SpareParts.Select(x => new SparePartLineDraft(x.SkuCode, x.Quantity, x.UomCode)),
            request.ActualLaborMinutes,
            request.SparePartCostAmount,
            request.ExternalServiceCostAmount,
            request.CostCurrencyCode,
            request.ActualTechnicianUserId);
        var result = new MaintenanceWorkOrderCommandResult(
            workOrder.Id,
            workOrder.Status,
            workOrder.CompletedAtUtc
                ?? throw new KnownException("Maintenance completion did not record an authoritative completion time."));
        AddCompletionIdempotencyRecord(workOrder, request, result);
        return result;
    }

    private async Task<MaintenanceWorkOrderCommandResult?> TryGetCompletionReplayAsync(
        MaintenanceWorkOrder workOrder,
        CompleteMaintenanceWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        if (idempotencyKey is null)
        {
            return null;
        }

        var existing = dbContext.CodeIdempotencyKeys.Local.FirstOrDefault(x =>
                x.OrganizationId == workOrder.OrganizationId &&
                x.EnvironmentId == workOrder.EnvironmentId &&
                x.RuleKey == CompleteRuleKey &&
                x.IdempotencyKey == idempotencyKey)
            ?? await dbContext.CodeIdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(x =>
                x.OrganizationId == workOrder.OrganizationId &&
                x.EnvironmentId == workOrder.EnvironmentId &&
                x.RuleKey == CompleteRuleKey &&
                x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.PayloadFingerprint, CompletionFingerprint(request), StringComparison.Ordinal))
        {
            throw new MaintenanceIdempotencyConflictException();
        }

        var parts = existing.Code.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !Enum.TryParse<MaintenanceWorkOrderStatus>(parts[0], out var status) ||
            !DateTimeOffset.TryParseExact(
                parts[1],
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var changedAtUtc))
        {
            throw new KnownException("stored-maintenance-completion-receipt-is-invalid");
        }

        return new MaintenanceWorkOrderCommandResult(
            request.WorkOrderId,
            status,
            changedAtUtc);
    }

    private void AddCompletionIdempotencyRecord(
        MaintenanceWorkOrder workOrder,
        CompleteMaintenanceWorkOrderCommand request,
        MaintenanceWorkOrderCommandResult result)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        if (idempotencyKey is null)
        {
            return;
        }

        dbContext.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            CompleteRuleKey,
            idempotencyKey,
            $"{result.Status}|{result.ChangedAtUtc:O}",
            CompletionFingerprint(request),
            result.ChangedAtUtc));
    }

    private static string CompletionFingerprint(CompleteMaintenanceWorkOrderCommand request) =>
        MaintenanceIdempotencyFingerprints.Hash(new
        {
            WorkOrderId = request.WorkOrderId.ToString(),
            Result = request.Result.Trim(),
            DowntimeReasonCode = request.DowntimeReasonCode.Trim().ToUpperInvariant(),
            request.DowntimeMinutes,
            request.ActualLaborMinutes,
            SparePartCostAmount = MaintenanceIdempotencyFingerprints.CanonicalDecimal(request.SparePartCostAmount),
            ExternalServiceCostAmount = MaintenanceIdempotencyFingerprints.CanonicalDecimal(request.ExternalServiceCostAmount),
            CostCurrencyCode = request.CostCurrencyCode?.Trim().ToUpperInvariant(),
            ActualTechnicianUserId = MaintenanceText.Optional(request.ActualTechnicianUserId),
            SpareParts = request.SpareParts
                .Select(x => new
                {
                    SkuCode = x.SkuCode.Trim().ToUpperInvariant(),
                    Quantity = MaintenanceIdempotencyFingerprints.CanonicalDecimal(x.Quantity),
                    UomCode = x.UomCode?.Trim().ToUpperInvariant(),
                })
                .OrderBy(x => x.SkuCode, StringComparer.Ordinal)
                .ThenBy(x => x.UomCode, StringComparer.Ordinal)
                .ThenBy(x => x.Quantity)
                .ToArray(),
        });

    private static string? NormalizeIdempotencyKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class MaintenanceIdempotencyFingerprints
{
    public static string CanonicalDecimal(decimal value) =>
        value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);

    public static string? CanonicalDecimal(decimal? value) =>
        value is null ? null : CanonicalDecimal(value.Value);

    public static string Hash<T>(T value)
    {
        var canonicalJson = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }
}

public sealed record StartMaintenanceRepairCommand(
    MaintenanceWorkOrderId WorkOrderId,
    DateTimeOffset RepairStartedAtUtc) : ICommand;

public sealed class StartMaintenanceRepairCommandValidator : AbstractValidator<StartMaintenanceRepairCommand>
{
    public StartMaintenanceRepairCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
    }
}

public sealed class StartMaintenanceRepairCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<StartMaintenanceRepairCommand>
{
    public async Task Handle(StartMaintenanceRepairCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(x => x.Id == request.WorkOrderId, cancellationToken)
            ?? throw new KnownException($"Maintenance work order was not found: {request.WorkOrderId}");
        workOrder.MarkRepairStarted(request.RepairStartedAtUtc);
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
        var workOrders = await dbContext.MaintenanceWorkOrders
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.SourceAlarmId == request.SourceAlarmId)
            .Where(x => x.Status == MaintenanceWorkOrderStatus.Open)
            .OrderBy(x => x.OpenedAtUtc)
            .ToArrayAsync(cancellationToken);
        foreach (var workOrder in workOrders)
        {
            workOrder.MarkAlarmCleared(request.ClearedAtUtc);
        }
    }
}

public sealed record ApplyMaintenanceDeviceStateCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    bool Disabled,
    DateTimeOffset ChangedAtUtc,
    string SourceEventId) : ICommand;

public sealed class ApplyMaintenanceDeviceStateCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ApplyMaintenanceDeviceStateCommand>
{
    public async Task Handle(ApplyMaintenanceDeviceStateCommand request, CancellationToken cancellationToken)
    {
        var state = await dbContext.MaintenanceDeviceStates.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId,
            cancellationToken);
        var applied = state is null;
        if (state is null)
        {
            state = MaintenanceDeviceState.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.Disabled,
                request.ChangedAtUtc,
                request.SourceEventId);
            dbContext.MaintenanceDeviceStates.Add(state);
        }
        else
        {
            applied = state.Apply(request.Disabled, request.ChangedAtUtc, request.SourceEventId);
        }

        if (!applied || !state.Disabled)
        {
            return;
        }

        var plans = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => !x.Paused)
            .ToArrayAsync(cancellationToken);

        foreach (var plan in plans)
        {
            plan.Pause();
        }
    }
}

internal static class MaintenancePmCommandLockKeys
{
    public const int AcquireTimeoutSeconds = 30;

    public static string For(string organizationId, string environmentId)
    {
        return string.Join(':',
            "business-maintenance",
            "pm-generation",
            Normalize(organizationId),
            Normalize(environmentId));
    }

    private static string Normalize(string value)
    {
        return Uri.EscapeDataString(value.Trim().ToLowerInvariant());
    }
}

public sealed class ApplyMaintenanceDeviceStateCommandLock : ICommandLock<ApplyMaintenanceDeviceStateCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(ApplyMaintenanceDeviceStateCommand command, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(new CommandLockSettings(
            MaintenancePmCommandLockKeys.For(command.OrganizationId, command.EnvironmentId),
            MaintenancePmCommandLockKeys.AcquireTimeoutSeconds));
    }
}

public sealed record GenerateDueMaintenanceWorkOrdersCommand(
    string OrganizationId,
    string EnvironmentId,
    DateOnly BusinessDate,
    string OpenedBy) : ICommand<GenerateDueMaintenanceWorkOrdersResult>;

public sealed class GenerateDueMaintenanceWorkOrdersCommandLock : ICommandLock<GenerateDueMaintenanceWorkOrdersCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(GenerateDueMaintenanceWorkOrdersCommand command, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var lockKey = MaintenancePmCommandLockKeys.For(command.OrganizationId, command.EnvironmentId);
        return Task.FromResult(new CommandLockSettings(lockKey, MaintenancePmCommandLockKeys.AcquireTimeoutSeconds));
    }
}

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

public sealed class GenerateDueMaintenanceWorkOrdersCommandHandler(
    ApplicationDbContext dbContext,
    IAssetRuntimeHoursProvider? runtimeHoursProvider = null,
    ILogger<GenerateDueMaintenanceWorkOrdersCommandHandler>? logger = null)
    : ICommandHandler<GenerateDueMaintenanceWorkOrdersCommand, GenerateDueMaintenanceWorkOrdersResult>
{
    private readonly ILogger<GenerateDueMaintenanceWorkOrdersCommandHandler> logger = logger ?? NullLogger<GenerateDueMaintenanceWorkOrdersCommandHandler>.Instance;

    public async Task<GenerateDueMaintenanceWorkOrdersResult> Handle(GenerateDueMaintenanceWorkOrdersCommand request, CancellationToken cancellationToken)
    {
        var plans = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => !x.Paused)
            .Where(x => !dbContext.MaintenanceDeviceStates.Any(state =>
                state.OrganizationId == x.OrganizationId
                && state.EnvironmentId == x.EnvironmentId
                && state.DeviceAssetId == x.DeviceAssetId
                && state.Disabled))
            .Where(x => (x.NextDueOn != null && x.NextDueOn <= request.BusinessDate) || x.RuntimeHourInterval != null)
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.PlanCode)
            .ToArrayAsync(cancellationToken);

        var workOrderIds = new List<MaintenanceWorkOrderId>();
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var dueDate in plan.ConsumeDueDates(request.BusinessDate))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddPlanWorkOrder(plan, request.OpenedBy, $"date:{dueDate:yyyyMMdd}", workOrderIds);
            }

            if (plan.RuntimeHourInterval is null)
            {
                continue;
            }

            if (runtimeHoursProvider is null)
            {
                logger.LogWarning(
                    "Runtime-hour PM plan {PlanCode} for {DeviceAssetId} was skipped because no IAssetRuntimeHoursProvider is registered; threshold {NextDueRuntimeHours} remains pending for retry.",
                    plan.PlanCode,
                    plan.DeviceAssetId,
                    plan.NextDueRuntimeHours);
                continue;
            }

            var runtime = await runtimeHoursProvider.CalculateAsync(
                plan.OrganizationId,
                plan.EnvironmentId,
                plan.DeviceAssetId,
                new DateTimeOffset(plan.StartsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                new DateTimeOffset(request.BusinessDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.HasRuntimeSamples)
            {
                logger.LogWarning(
                    "Runtime-hour PM plan {PlanCode} for {DeviceAssetId} was skipped because runtime provider returned no real samples from {RuntimeSource}; threshold {NextDueRuntimeHours} remains pending for retry.",
                    plan.PlanCode,
                    plan.DeviceAssetId,
                    runtime.RuntimeSource,
                    plan.NextDueRuntimeHours);
                continue;
            }

            var runtimeThresholds = plan.ConsumeRuntimeDue(runtime.RuntimeHours).ToArray();
            for (var i = 0; i < runtimeThresholds.Length; i++)
            {
                AddPlanWorkOrder(plan, request.OpenedBy, $"runtime:{runtimeThresholds[i]:0.######}:{i + 1}", workOrderIds);
            }
        }

        return new GenerateDueMaintenanceWorkOrdersResult(workOrderIds.Count, workOrderIds);
    }

    private void AddPlanWorkOrder(MaintenancePlan plan, string openedBy, string dueSuffix, List<MaintenanceWorkOrderId> workOrderIds)
    {
        var workOrder = MaintenanceWorkOrder.OpenFromPlan(
            plan.OrganizationId,
            plan.EnvironmentId,
            plan.DeviceAssetId,
            plan.PlanCode,
            openedBy,
            $"{plan.PlanCode}:{dueSuffix}");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        workOrderIds.Add(workOrder.Id);
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
        // Same rule as the completion path: no unit, no inventory issue — do not fabricate one downstream.
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
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
    string? Interval,
    DateOnly StartsOn,
    string Owner,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    string? IdempotencyKey = null,
    decimal? RuntimeHourInterval = null) : ICommand<MaintenancePlanId>;

public sealed class CreateMaintenancePlanCommandValidator : AbstractValidator<CreateMaintenancePlanCommand>
{
    public CreateMaintenancePlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PlanCode).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
        RuleFor(x => x.Interval).MaximumLength(50);
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RuntimeHourInterval).GreaterThan(0);
        // A plan needs at least one trigger: calendar interval, runtime-hour interval, or both.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Interval) || x.RuntimeHourInterval is not null)
            .WithMessage("Maintenance plan must have a calendar interval, a runtime-hour interval, or both.");
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
                request.WindowEndUtc,
                request.RuntimeHourInterval),
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
        var plan = MaintenancePlan.Create(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, allocation.Code, request.Interval, request.StartsOn, request.Owner, windowStartUtc, windowEndUtc, request.RuntimeHourInterval);
        var deviceDisabled = await dbContext.MaintenanceDeviceStates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.Disabled,
            cancellationToken);
        if (deviceDisabled)
        {
            plan.Pause();
        }

        dbContext.MaintenancePlans.Add(plan);
        await Task.CompletedTask;
        return plan.Id;
    }
}

public sealed class CreateMaintenancePlanCommandLock : ICommandLock<CreateMaintenancePlanCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(CreateMaintenancePlanCommand command, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(new CommandLockSettings(
            MaintenancePmCommandLockKeys.For(command.OrganizationId, command.EnvironmentId),
            MaintenancePmCommandLockKeys.AcquireTimeoutSeconds));
    }
}

public sealed record UpdateMaintenancePlanCommand(
    string OrganizationId,
    string EnvironmentId,
    MaintenancePlanId PlanId,
    string? Interval,
    decimal? RuntimeHourInterval) : ICommand;

public sealed class UpdateMaintenancePlanCommandValidator : AbstractValidator<UpdateMaintenancePlanCommand>
{
    public UpdateMaintenancePlanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Interval).MaximumLength(50);
        RuleFor(x => x.RuntimeHourInterval).GreaterThan(0).When(x => x.RuntimeHourInterval is not null);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Interval) || x.RuntimeHourInterval is not null)
            .WithMessage("Maintenance plan must have a calendar interval, a runtime-hour interval, or both.");
    }
}

public sealed class UpdateMaintenancePlanCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UpdateMaintenancePlanCommand>
{
    public async Task Handle(UpdateMaintenancePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.MaintenancePlans.SingleOrDefaultAsync(
                x => x.Id == request.PlanId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException($"Maintenance plan was not found: {request.PlanId}");

        plan.UpdateTriggerConfiguration(request.Interval, request.RuntimeHourInterval);
    }
}

public sealed class UpdateMaintenancePlanCommandLock : ICommandLock<UpdateMaintenancePlanCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(UpdateMaintenancePlanCommand command, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(new CommandLockSettings(
            MaintenancePmCommandLockKeys.For(command.OrganizationId, command.EnvironmentId),
            MaintenancePmCommandLockKeys.AcquireTimeoutSeconds));
    }
}

public sealed record RecordMaintenanceInspectionCommand(
    string OrganizationId,
    string EnvironmentId,
    MaintenancePlanId? PlanId,
    MaintenanceWorkOrderId? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc,
    IReadOnlyCollection<MaintenanceInspectionMeasurementInput>? Measurements = null) : ICommand<MaintenanceInspectionId>;

public sealed class RecordMaintenanceInspectionCommandValidator : AbstractValidator<RecordMaintenanceInspectionCommand>
{
    public RecordMaintenanceInspectionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Inspector).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Result).NotEmpty().MaximumLength(1000);
        RuleFor(x => x).Must(x => x.PlanId is not null || x.WorkOrderId is not null).WithMessage("Inspection must reference a maintenance plan or work order.");
        RuleForEach(x => x.Measurements).ChildRules(x =>
        {
            x.RuleFor(m => m.CharacteristicCode).NotEmpty().MaximumLength(100);
            x.RuleFor(m => m.MeasuredValue)
                .Must(MaintenanceNumericValidation.FitsNumeric18Scale6)
                .WithMessage("Measured value must fit numeric(18,6).");
            x.RuleFor(m => m.UomCode).NotEmpty().MaximumLength(50);
            x.RuleFor(m => m.LowerSpecLimit)
                .Must(MaintenanceNumericValidation.FitsNullableNumeric18Scale6)
                .WithMessage("Lower spec limit must fit numeric(18,6).");
            x.RuleFor(m => m.UpperSpecLimit)
                .Must(MaintenanceNumericValidation.FitsNullableNumeric18Scale6)
                .WithMessage("Upper spec limit must fit numeric(18,6).");
            x.RuleFor(m => m).Must(m => m.LowerSpecLimit is null || m.UpperSpecLimit is null || m.LowerSpecLimit <= m.UpperSpecLimit)
                .WithMessage("Lower spec limit cannot be greater than upper spec limit.");
        });
    }
}

public sealed class RecordMaintenanceInspectionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordMaintenanceInspectionCommand, MaintenanceInspectionId>
{
    public async Task<MaintenanceInspectionId> Handle(RecordMaintenanceInspectionCommand request, CancellationToken cancellationToken)
    {
        var inspectedAtUtc = request.InspectedAtUtc.ToUniversalTime();
        var normalizedResult = MaintenanceInspectionResults.Normalize(request.Result);
        var measurementDrafts = (request.Measurements ?? [])
            .Select(x => new MaintenanceInspectionMeasurementDraft(x.CharacteristicCode, x.MeasuredValue, x.UomCode, x.LowerSpecLimit, x.UpperSpecLimit))
            .ToArray();
        var matchingInspections = await dbContext.MaintenanceInspections
            .Include(x => x.Measurements)
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.PlanId == request.PlanId)
            .Where(x => x.WorkOrderId == request.WorkOrderId)
            .Where(x => x.Inspector == request.Inspector)
            .Where(x => x.Result == normalizedResult)
            .Where(x => x.InspectedAtUtc == inspectedAtUtc)
            .ToListAsync(cancellationToken);
        var replayCandidates = matchingInspections
            .Where(x => MeasurementSetsMatch(x.Measurements, measurementDrafts))
            .ToArray();
        var inspection = await SelectInspectionForReplayAsync(request.OrganizationId, request.EnvironmentId, replayCandidates, cancellationToken);
        if (inspection is null)
        {
            inspection = MaintenanceInspection.Record(request.OrganizationId, request.EnvironmentId, request.PlanId, request.WorkOrderId, request.Inspector, normalizedResult, inspectedAtUtc, measurementDrafts);
            dbContext.MaintenanceInspections.Add(inspection);
        }

        if (MaintenanceInspectionResults.IsFailed(inspection.Result))
        {
            await OpenInspectionWorkOrderIfNeededAsync(inspection, cancellationToken);
        }

        return inspection.Id;
    }

    private async Task<MaintenanceInspection?> SelectInspectionForReplayAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<MaintenanceInspection> inspections,
        CancellationToken cancellationToken)
    {
        if (inspections.Count <= 1)
        {
            return inspections.SingleOrDefault();
        }

        var sourceReferenceIds = inspections.Select(x => x.Id.ToString()).ToArray();
        var existingSourceReferenceId = await dbContext.MaintenanceWorkOrders
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.SourceType == MaintenanceWorkOrderSourceTypes.Inspection)
            .Where(x => x.SourceReferenceId != null && sourceReferenceIds.Contains(x.SourceReferenceId))
            .Select(x => x.SourceReferenceId)
            .FirstOrDefaultAsync(cancellationToken);

        return inspections.FirstOrDefault(x => x.Id.ToString() == existingSourceReferenceId)
            ?? inspections.First();
    }

    private static bool MeasurementSetsMatch(
        IReadOnlyCollection<MaintenanceInspectionMeasurement> persisted,
        IReadOnlyCollection<MaintenanceInspectionMeasurementDraft> requested)
    {
        return persisted.Select(ToKey).Order().SequenceEqual(requested.Select(ToKey).Order());
    }

    private static InspectionMeasurementKey ToKey(MaintenanceInspectionMeasurement measurement)
    {
        return new InspectionMeasurementKey(
            measurement.CharacteristicCode,
            measurement.MeasuredValue,
            measurement.UomCode,
            measurement.LowerSpecLimit,
            measurement.UpperSpecLimit);
    }

    private static InspectionMeasurementKey ToKey(MaintenanceInspectionMeasurementDraft measurement)
    {
        return new InspectionMeasurementKey(
            MaintenanceText.Required(measurement.CharacteristicCode, nameof(measurement.CharacteristicCode)),
            measurement.MeasuredValue,
            MaintenanceText.Required(measurement.UomCode, nameof(measurement.UomCode)),
            measurement.LowerSpecLimit,
            measurement.UpperSpecLimit);
    }

    private sealed record InspectionMeasurementKey(
        string CharacteristicCode,
        decimal MeasuredValue,
        string UomCode,
        decimal? LowerSpecLimit,
        decimal? UpperSpecLimit) : IComparable<InspectionMeasurementKey>
    {
        public int CompareTo(InspectionMeasurementKey? other)
        {
            if (other is null)
            {
                return 1;
            }

            var characteristicComparison = string.Compare(CharacteristicCode, other.CharacteristicCode, StringComparison.Ordinal);
            if (characteristicComparison != 0)
            {
                return characteristicComparison;
            }

            var valueComparison = MeasuredValue.CompareTo(other.MeasuredValue);
            if (valueComparison != 0)
            {
                return valueComparison;
            }

            var uomComparison = string.Compare(UomCode, other.UomCode, StringComparison.Ordinal);
            if (uomComparison != 0)
            {
                return uomComparison;
            }

            var lowerComparison = Nullable.Compare(LowerSpecLimit, other.LowerSpecLimit);
            return lowerComparison != 0
                ? lowerComparison
                : Nullable.Compare(UpperSpecLimit, other.UpperSpecLimit);
        }
    }

    private async Task OpenInspectionWorkOrderIfNeededAsync(MaintenanceInspection inspection, CancellationToken cancellationToken)
    {
        var sourceReferenceId = inspection.Id.ToString();
        var existing = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == inspection.OrganizationId
                && x.EnvironmentId == inspection.EnvironmentId
                && x.SourceType == MaintenanceWorkOrderSourceTypes.Inspection
                && x.SourceReferenceId == sourceReferenceId,
            cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var deviceAssetId = await ResolveInspectionDeviceAssetIdAsync(inspection, cancellationToken);
        var workOrder = MaintenanceWorkOrder.OpenFromInspection(
            inspection.OrganizationId,
            inspection.EnvironmentId,
            deviceAssetId,
            inspection.Id,
            inspection.Result);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
    }

    private async Task<string> ResolveInspectionDeviceAssetIdAsync(MaintenanceInspection inspection, CancellationToken cancellationToken)
    {
        if (inspection.PlanId is not null)
        {
            var plan = await dbContext.MaintenancePlans.SingleOrDefaultAsync(
                x => x.Id == inspection.PlanId
                    && x.OrganizationId == inspection.OrganizationId
                    && x.EnvironmentId == inspection.EnvironmentId,
                cancellationToken)
                ?? throw new KnownException($"Maintenance inspection plan was not found: {inspection.PlanId}");
            return plan.DeviceAssetId;
        }

        var workOrder = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(
            x => x.Id == inspection.WorkOrderId
                && x.OrganizationId == inspection.OrganizationId
                && x.EnvironmentId == inspection.EnvironmentId,
            cancellationToken)
            ?? throw new KnownException($"Maintenance inspection work order was not found: {inspection.WorkOrderId}");
        return workOrder.DeviceAssetId;
    }
}

public sealed record CreateDowntimeReasonCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string Description,
    string ReasonCategory,
    string LossCategory) : ICommand<DowntimeReasonId>;

public sealed class CreateDowntimeReasonCommandValidator : AbstractValidator<CreateDowntimeReasonCommand>
{
    public CreateDowntimeReasonCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ReasonCategory).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LossCategory).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateDowntimeReasonCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateDowntimeReasonCommand, DowntimeReasonId>
{
    public async Task<DowntimeReasonId> Handle(CreateDowntimeReasonCommand request, CancellationToken cancellationToken)
    {
        var normalizedReasonCode = request.ReasonCode.Trim();
        var existing = await dbContext.DowntimeReasons.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ReasonCode == normalizedReasonCode,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var reason = DowntimeReason.Create(request.OrganizationId, request.EnvironmentId, normalizedReasonCode, request.Description, request.ReasonCategory, request.LossCategory);
        dbContext.DowntimeReasons.Add(reason);
        return reason.Id;
    }
}

public sealed record UpdateDowntimeReasonCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string Description,
    string ReasonCategory,
    string LossCategory) : ICommand;

public sealed class UpdateDowntimeReasonCommandValidator : AbstractValidator<UpdateDowntimeReasonCommand>
{
    public UpdateDowntimeReasonCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ReasonCategory).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LossCategory).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateDowntimeReasonCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UpdateDowntimeReasonCommand>
{
    public async Task Handle(UpdateDowntimeReasonCommand request, CancellationToken cancellationToken)
    {
        var normalizedReasonCode = request.ReasonCode.Trim();
        var reason = await dbContext.DowntimeReasons.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ReasonCode == normalizedReasonCode,
            cancellationToken)
            ?? throw new KnownException($"Downtime reason was not found: {request.ReasonCode}");

        reason.Update(request.Description, request.ReasonCategory, request.LossCategory);
    }
}

public sealed record DeleteDowntimeReasonCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode) : ICommand;

public sealed class DeleteDowntimeReasonCommandValidator : AbstractValidator<DeleteDowntimeReasonCommand>
{
    public DeleteDowntimeReasonCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class DeleteDowntimeReasonCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<DeleteDowntimeReasonCommand>
{
    public async Task Handle(DeleteDowntimeReasonCommand request, CancellationToken cancellationToken)
    {
        var normalizedReasonCode = request.ReasonCode.Trim();
        var reason = await dbContext.DowntimeReasons.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ReasonCode == normalizedReasonCode,
            cancellationToken)
            ?? throw new KnownException($"Downtime reason was not found: {request.ReasonCode}");

        var hasWorkOrders = await dbContext.MaintenanceWorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DowntimeReasonCode == normalizedReasonCode,
            cancellationToken);
        if (hasWorkOrders)
        {
            throw new KnownException($"Downtime reason is referenced by maintenance work orders and cannot be deleted: {request.ReasonCode}");
        }

        dbContext.DowntimeReasons.Remove(reason);
    }
}
