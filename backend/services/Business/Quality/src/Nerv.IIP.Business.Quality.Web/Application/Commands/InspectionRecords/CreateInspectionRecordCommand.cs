using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record InspectionResultLineCommandInput(
    string CharacteristicCode,
    string ObservedValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string> AttachmentFileIds,
    decimal? MeasuredValue = null);

public sealed record StockReleaseDimensionCommandInput(
    string UomCode,
    string SiteCode,
    string LocationCode,
    string SourceQualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record CreateInspectionRecordCommand(
    string OrganizationId,
    string EnvironmentId,
    InspectionPlanId? InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string? BatchNo,
    string? SerialNo,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    StockReleaseDimensionCommandInput? StockRelease = null,
    MeasuringDeviceId? MeasuringDeviceId = null) : ICommand<InspectionRecordId>;

public sealed class CreateInspectionRecordCommandValidator : AbstractValidator<CreateInspectionRecordCommand>
{
    public CreateInspectionRecordCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceService).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InspectedQuantity).GreaterThan(0);
        RuleFor(x => x.ResultLines).NotEmpty();
        RuleFor(x => x.DispositionReason)
            .NotEmpty()
            .When(HasNonPassedResultLine)
            .WithMessage("Disposition reason is required when any inspection result line is rejected or conditionally released.");
    }

    private static bool HasNonPassedResultLine(CreateInspectionRecordCommand command)
    {
        return command.ResultLines?.Any(line =>
            !string.Equals(line.Result, InspectionLineResults.Passed, StringComparison.OrdinalIgnoreCase)) == true;
    }
}

public sealed class CreateInspectionRecordCommandHandler(
    IInspectionRecordRepository repository,
    IInspectionPlanRepository inspectionPlanRepository,
    IInspectionTaskRepository inspectionTaskRepository,
    IInspectionUomConversionClient? uomConversionClient = null,
    IInspectionSourceDocumentVerifier? sourceDocumentVerifier = null,
    ApplicationDbContext? dbContext = null,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null)
    : ICommandHandler<CreateInspectionRecordCommand, InspectionRecordId>
{
    private readonly IInspectionUomConversionClient uomConversionClient = uomConversionClient ?? NullInspectionUomConversionClient.Instance;
    private readonly IInspectionSourceDocumentVerifier sourceDocumentVerifier = sourceDocumentVerifier ?? NullInspectionSourceDocumentVerifier.Instance;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<InspectionRecordId> Handle(CreateInspectionRecordCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.FindBySourceDocumentAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceType.Trim().ToLowerInvariant(),
            request.SourceService.Trim().ToLowerInvariant(),
            request.SkuCode.Trim(),
            request.SourceDocumentId.Trim(),
            cancellationToken);
        if (existing is not null)
        {
            if (existing.InspectedQuantity != request.InspectedQuantity)
            {
                throw new KnownException(
                    $"来源单据 {request.SourceDocumentId} 与 SKU {request.SkuCode} 已有检验记录，原检验数量为 {existing.InspectedQuantity} 件，本次为 {request.InspectedQuantity} 件，请使用相同数量重试；如需更正请在检验记录页发起复检。");
            }

            await RejectMatchingTaskBypassAsync(request, cancellationToken);
            return existing.Id;
        }

        await VerifySourceDocumentAsync(request, cancellationToken);
        var measuringDeviceUsage = await ResolveMeasuringDeviceUsageAsync(request, cancellationToken);

        var lines = request.ResultLines.Select(x => new InspectionResultLineInput(
            x.CharacteristicCode,
            x.ObservedValue,
            x.UnitCode,
            x.Result,
            x.DefectReason,
            x.DefectQuantity,
            x.AttachmentFileIds,
            x.MeasuredValue)).ToArray();
        var stockRelease = request.StockRelease is null
            ? null
            : StockReleaseDimension.Create(
                request.StockRelease.UomCode,
                request.StockRelease.SiteCode,
                request.StockRelease.LocationCode,
                request.StockRelease.SourceQualityStatus,
                request.StockRelease.OwnerType,
                request.StockRelease.OwnerId);
        InspectionRecord record;
        if (request.InspectionPlanId is not null)
        {
            var plan = await inspectionPlanRepository.GetWithCharacteristicsAsync(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.InspectionPlanId,
                    cancellationToken)
                ?? throw new KnownException($"找不到检验方案 {request.InspectionPlanId}，请在检验方案页确认方案已建档并启用后重试。");
            var uomConversions = await uomConversionClient.GetConversionsAsync(
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken);
            record = InspectionRecord.CreateFromPlan(
                plan,
                request.SourceType,
                request.SourceService,
                request.SourceDocumentId,
                request.SkuCode,
                request.InspectedQuantity,
                request.BatchNo,
                request.SerialNo,
                stockRelease,
                lines,
                request.DispositionReason,
                request.DispositionAttachmentFileIds,
                uomConversions,
                measuringDeviceUsage);
        }
        else
        {
            record = InspectionRecord.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.InspectionPlanId,
                request.SourceType,
                request.SourceService,
                request.SourceDocumentId,
                request.SkuCode,
                request.InspectedQuantity,
                request.BatchNo,
                request.SerialNo,
                lines,
                request.DispositionReason,
                request.DispositionAttachmentFileIds,
                stockRelease,
                measuringDeviceUsage);
        }

        await repository.AddAsync(record, cancellationToken);
        await RejectMatchingTaskBypassAsync(request, cancellationToken);
        return record.Id;
    }

    private async Task<InspectionMeasuringDeviceUsage?> ResolveMeasuringDeviceUsageAsync(CreateInspectionRecordCommand request, CancellationToken cancellationToken)
    {
        if (request.MeasuringDeviceId is null) return null;
        if (dbContext is null)
        {
            throw new KnownException(
                $"来源单据 {request.SourceDocumentId} 的 SKU {request.SkuCode} 需要测量设备追溯，但质量服务未启用该配置，请在质量配置页启用后重试。");
        }
        var device = await dbContext.MeasuringDevices.SingleOrDefaultAsync(x => x.Id == request.MeasuringDeviceId, cancellationToken)
            ?? throw new KnownException(
                $"找不到测量设备 {request.MeasuringDeviceId}，请在测量设备页确认设备已建档后重试。");
        if (device.OrganizationId != request.OrganizationId || device.EnvironmentId != request.EnvironmentId)
        {
            throw new KnownException(
                $"测量设备 {request.MeasuringDeviceId} 不属于来源单据 {request.SourceDocumentId} 的组织和环境范围，请选择同一质量范围内的设备后重试。");
        }
        var usage = InspectionMeasuringDeviceUsage.Create(device, timeProvider.GetUtcNow());
        var policy = configuration?["Quality:MeasuringDevice:ExpiredInspectionPolicy"] ?? "warn";
        if (MeasuringDeviceInspectionPolicy.Blocks(policy, usage.CalibrationState))
            throw new KnownException(
                $"测量设备 {request.MeasuringDeviceId} 的校准已过期，来源单据 {request.SourceDocumentId} 的检验录入已阻止，请在测量设备页完成校准后再提交。");
        return usage;
    }

    private async Task RejectMatchingTaskBypassAsync(
        CreateInspectionRecordCommand request,
        CancellationToken cancellationToken)
    {
        var task = await inspectionTaskRepository.FindOpenBySourceAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            cancellationToken);
        if (task is null)
        {
            return;
        }

        throw new QualityLifecycleConflictException(
            "create-inspection-record",
            "matching-task-requires-task-submit");
    }

    private async Task VerifySourceDocumentAsync(CreateInspectionRecordCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.SourceType, "receiving", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var verification = await sourceDocumentVerifier.VerifyAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            request.InspectedQuantity,
            cancellationToken);
        if (!verification.Exists)
        {
            throw new KnownException(
                $"找不到来源单据 {request.SourceDocumentId} 或该单据不属于当前质量范围，请在收货单页面确认单据和 SKU {request.SkuCode} 后重试。");
        }

        if (!string.IsNullOrWhiteSpace(verification.SkuCode)
            && !string.Equals(verification.SkuCode, request.SkuCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException(
                $"来源单据 {request.SourceDocumentId} 上的 SKU {verification.SkuCode} 与本次检验 SKU {request.SkuCode} 不一致，请返回收货单页面选择正确 SKU 后重试。");
        }

        if (verification.Quantity is { } sourceQuantity && request.InspectedQuantity > sourceQuantity)
        {
            throw new KnownException(
                $"来源单据 {request.SourceDocumentId} 的 SKU {request.SkuCode} 可检验数量仅为 {sourceQuantity} 件，本次提交 {request.InspectedQuantity} 件，请调整检验数量后重试。");
        }
    }
}
