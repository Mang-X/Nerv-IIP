using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

/// <summary>
/// 从任务建检验记录的权威结论：记录 id、后端按检验计划规格 + AQL 计算的 <c>Result</c>
/// （passed / rejected / conditional-release），以及不合格时后端**同事务内自动开出**并回链的 NCR
/// id 与业务编号（<c>NcrCode</c>，供结果页展示与互查——GUID 不是人读单号）。PDA 结果页据此展示
/// 权威结论与 NCR 互链，而不是提交前的客户端预判。
/// </summary>
public sealed record CreateInspectionRecordFromTaskResult(
    InspectionRecordId InspectionRecordId,
    string Result,
    string? NonconformanceReportId,
    string? NonconformanceReportCode);

public sealed record CreateInspectionRecordFromTaskCommand(
    InspectionTaskId InspectionTaskId,
    string InspectorUserId,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds) : ICommand<CreateInspectionRecordFromTaskResult>;

public sealed class CreateInspectionRecordFromTaskCommandValidator : AbstractValidator<CreateInspectionRecordFromTaskCommand>
{
    public CreateInspectionRecordFromTaskCommandValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty();
        RuleFor(x => x.InspectorUserId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ResultLines).NotEmpty();
    }
}

public sealed class CreateInspectionRecordFromTaskCommandHandler(
    IInspectionTaskRepository inspectionTaskRepository,
    IInspectionRecordRepository inspectionRecordRepository,
    IInspectionPlanRepository inspectionPlanRepository,
    INonconformanceReportRepository nonconformanceReportRepository,
    INonconformanceReportCodeGenerator nonconformanceReportCodeGenerator)
    : ICommandHandler<CreateInspectionRecordFromTaskCommand, CreateInspectionRecordFromTaskResult>
{
    public async Task<CreateInspectionRecordFromTaskResult> Handle(CreateInspectionRecordFromTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await inspectionTaskRepository.GetAsync(request.InspectionTaskId, cancellationToken)
            ?? throw new KnownException($"Inspection task '{request.InspectionTaskId}' was not found.");

        // 幂等：任务已完成 → 回读既有记录的权威结论。仍走统一收尾（既有 rejected 记录若因常规
        // 检验流程未开 NCR，会在这里补开并回链，避免端点永久返回 NonconformanceReportId=null）。
        if (task.Status == InspectionTaskStatuses.Completed && task.InspectionRecordId is not null)
        {
            var completed = await inspectionRecordRepository.GetAsync(task.InspectionRecordId, cancellationToken);
            return await EnsureNcrAndBuildResultAsync(task.InspectionRecordId, completed, cancellationToken);
        }

        var existing = await inspectionRecordRepository.FindBySourceDocumentAsync(
            task.OrganizationId,
            task.EnvironmentId,
            task.SourceType,
            task.SourceService,
            task.SkuCode,
            task.SourceDocumentId,
            cancellationToken);
        if (existing is not null)
        {
            if (task.Status == InspectionTaskStatuses.Pending)
            {
                task.Start(request.InspectorUserId, DateTimeOffset.UtcNow);
            }

            task.Complete(existing.Id, DateTimeOffset.UtcNow);
            return await EnsureNcrAndBuildResultAsync(existing.Id, existing, cancellationToken);
        }

        var plan = await inspectionPlanRepository.GetWithCharacteristicsAsync(
                task.OrganizationId,
                task.EnvironmentId,
                task.InspectionPlanId,
                cancellationToken)
            ?? throw new KnownException($"Inspection plan '{task.InspectionPlanId}' was not found.");
        var lines = request.ResultLines.Select(x => new InspectionResultLineInput(
            x.CharacteristicCode,
            x.ObservedValue,
            x.UnitCode,
            x.Result,
            x.DefectReason,
            x.DefectQuantity,
            x.AttachmentFileIds,
            x.MeasuredValue)).ToArray();
        var record = InspectionRecord.CreateFromPlan(
            plan,
            task.SourceType,
            task.SourceService,
            task.SourceDocumentId,
            task.SkuCode,
            task.Quantity,
            task.BatchNo,
            task.SerialNo,
            null,
            lines,
            request.DispositionReason,
            request.DispositionAttachmentFileIds);

        task.Start(request.InspectorUserId, DateTimeOffset.UtcNow);
        task.Complete(record.Id, DateTimeOffset.UtcNow);
        await inspectionRecordRepository.AddAsync(record, cancellationToken);

        return await EnsureNcrAndBuildResultAsync(record.Id, record, cancellationToken);
    }

    /// <summary>
    /// 所有返回路径（新建 / 命中既有记录 / 完成重放）共用的收尾：非合格且尚未回链则**同事务内**
    /// 自动开出 NCR 并回链，使「不合格 → 已发起 NCR」在幂等回读时同样成立；已回链则回读 NCR 业务
    /// 编号供结果页展示/互查。幂等安全（已回链不重复开单，重放读同一 NCR）。
    /// </summary>
    private async Task<CreateInspectionRecordFromTaskResult> EnsureNcrAndBuildResultAsync(
        InspectionRecordId recordId,
        InspectionRecord? record,
        CancellationToken cancellationToken)
    {
        if (record is null)
        {
            return new CreateInspectionRecordFromTaskResult(recordId, string.Empty, null, null);
        }

        if (record.Result != InspectionRecordResults.Passed && record.NonconformanceReportId is null)
        {
            var ncrCode = await nonconformanceReportCodeGenerator.NextAsync(record.OrganizationId, record.EnvironmentId, cancellationToken);
            var ncr = NonconformanceReport.OpenFromInspection(
                ncrCode,
                record,
                record.DispositionReason ?? string.Empty,
                record.DispositionAttachmentFileIds);
            record.LinkNonconformanceReport(ncr.Id.ToString());
            await nonconformanceReportRepository.AddAsync(ncr, cancellationToken);
            return new CreateInspectionRecordFromTaskResult(record.Id, record.Result, record.NonconformanceReportId, ncr.NcrCode);
        }

        // 已回链 → 回读 NCR 业务编号（GUID 不是人读单号）。
        string? linkedNcrCode = null;
        if (record.NonconformanceReportId is not null
            && Guid.TryParse(record.NonconformanceReportId, out var linkedNcrGuid))
        {
            var linked = await nonconformanceReportRepository.GetAsync(new NonconformanceReportId(linkedNcrGuid), cancellationToken);
            linkedNcrCode = linked?.NcrCode;
        }

        return new CreateInspectionRecordFromTaskResult(record.Id, record.Result, record.NonconformanceReportId, linkedNcrCode);
    }
}
