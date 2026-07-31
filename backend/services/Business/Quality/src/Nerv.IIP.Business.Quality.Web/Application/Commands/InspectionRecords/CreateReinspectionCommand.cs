using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record CreateReinspectionCommand(
    InspectionRecordId ReinspectionOfInspectionRecordId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    MeasuringDeviceId? MeasuringDeviceId = null) : ICommand<CreateReinspectionResult>;

public sealed record CreateReinspectionResult(
    InspectionRecordId InspectionRecordId,
    int AttemptNumber);

public sealed class CreateReinspectionCommandValidator : AbstractValidator<CreateReinspectionCommand>
{
    public CreateReinspectionCommandValidator()
    {
        RuleFor(x => x.ReinspectionOfInspectionRecordId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ResultLines).NotEmpty();
        RuleFor(x => x.DispositionReason)
            .NotEmpty()
            .When(command => command.ResultLines.Any(line =>
                !string.Equals(line.Result, InspectionLineResults.Passed, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Disposition reason is required when any reinspection result line is rejected or conditionally released.");
    }
}

public sealed class CreateReinspectionCommandHandler(
    IInspectionRecordRepository inspectionRecordRepository,
    IInspectionPlanRepository inspectionPlanRepository,
    IInspectionUomConversionClient? uomConversionClient = null,
    ApplicationDbContext? dbContext = null,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null)
    : ICommandHandler<CreateReinspectionCommand, CreateReinspectionResult>
{
    private readonly IInspectionUomConversionClient uomConversionClient =
        uomConversionClient ?? NullInspectionUomConversionClient.Instance;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CreateReinspectionResult> Handle(
        CreateReinspectionCommand request,
        CancellationToken cancellationToken)
    {
        var previous = await inspectionRecordRepository.GetScopedAsync(
                request.ReinspectionOfInspectionRecordId,
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException(
                $"找不到检验记录 {request.ReinspectionOfInspectionRecordId}，请在检验记录页确认记录编号后重试。");
        // Translate the aggregate's terminal-pass invariant into the service's stable known-error contract.
        // InspectionRecord.Reinspect repeats the guard for non-HTTP callers that bypass this handler.
        if (string.Equals(previous.Result, InspectionRecordResults.Passed, StringComparison.Ordinal))
        {
            throw new KnownException(
                $"检验记录 {previous.Id} 已为合格结果，不能重复复检；如需其他检验请在检验记录页新建独立检验。");
        }

        var existing = await inspectionRecordRepository.FindByReinspectionOfAsync(
            previous.Id,
            cancellationToken);
        if (existing is not null)
        {
            return new CreateReinspectionResult(existing.Id, existing.AttemptNumber);
        }

        var plan = previous.InspectionPlanId is null
            ? null
            : await inspectionPlanRepository.GetWithCharacteristicsAsync(
                    previous.OrganizationId,
                    previous.EnvironmentId,
                    previous.InspectionPlanId,
                    cancellationToken)
                ?? throw new KnownException(
                    $"找不到检验记录 {previous.Id} 引用的检验方案 {previous.InspectionPlanId}，请在检验方案页确认方案已建档并启用后重试。");
        var conversions = plan is null
            ? []
            : await uomConversionClient.GetConversionsAsync(
                previous.OrganizationId,
                previous.EnvironmentId,
                cancellationToken);
        var measuringDeviceUsage = await ResolveMeasuringDeviceUsageAsync(
            request,
            previous.Id,
            cancellationToken);
        var resultLines = request.ResultLines.Select(line => new InspectionResultLineInput(
            line.CharacteristicCode,
            line.ObservedValue,
            line.UnitCode,
            line.Result,
            line.DefectReason,
            line.DefectQuantity,
            line.AttachmentFileIds,
            line.MeasuredValue)).ToArray();
        var reinspection = InspectionRecord.Reinspect(
            previous,
            plan,
            resultLines,
            request.DispositionReason,
            request.DispositionAttachmentFileIds,
            conversions,
            measuringDeviceUsage);

        await inspectionRecordRepository.AddAsync(reinspection, cancellationToken);
        return new CreateReinspectionResult(reinspection.Id, reinspection.AttemptNumber);
    }

    private async Task<InspectionMeasuringDeviceUsage?> ResolveMeasuringDeviceUsageAsync(
        CreateReinspectionCommand request,
        InspectionRecordId previousInspectionRecordId,
        CancellationToken cancellationToken)
    {
        if (request.MeasuringDeviceId is null)
        {
            return null;
        }

        if (dbContext is null)
        {
            throw new KnownException(
                $"测量设备 {request.MeasuringDeviceId} 的追溯配置暂不可用，请在质量配置页启用测量设备追溯后重试。");
        }

        var device = await dbContext.MeasuringDevices.SingleOrDefaultAsync(
                x => x.Id == request.MeasuringDeviceId,
                cancellationToken)
            ?? throw new KnownException(
                $"找不到测量设备 {request.MeasuringDeviceId}，请在测量设备页确认设备已建档后重试。");
        if (device.OrganizationId != request.OrganizationId
            || device.EnvironmentId != request.EnvironmentId)
        {
            throw new KnownException(
                $"测量设备 {request.MeasuringDeviceId} 不属于检验记录 {previousInspectionRecordId} 的组织和环境范围，请选择同一质量范围内的设备后重试。");
        }

        var usage = InspectionMeasuringDeviceUsage.Create(device, timeProvider.GetUtcNow());
        var policy = configuration?["Quality:MeasuringDevice:ExpiredInspectionPolicy"] ?? "warn";
        if (MeasuringDeviceInspectionPolicy.Blocks(policy, usage.CalibrationState))
        {
            throw new KnownException(
                $"测量设备 {request.MeasuringDeviceId} 的校准已过期，检验记录 {previousInspectionRecordId} 禁止录入，请在测量设备页完成校准后再提交。");
        }

        return usage;
    }
}

public sealed class CreateReinspectionUniqueConflictBehavior(
    ApplicationDbContext dbContext,
    IQualityPersistenceConflictClassifier conflictClassifier)
    : IPipelineBehavior<CreateReinspectionCommand, CreateReinspectionResult>
{
    public async Task<CreateReinspectionResult> Handle(
        CreateReinspectionCommand request,
        RequestHandlerDelegate<CreateReinspectionResult> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (DbUpdateException exception) when (conflictClassifier.IsReinspectionConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await next(cancellationToken);
        }
    }
}
