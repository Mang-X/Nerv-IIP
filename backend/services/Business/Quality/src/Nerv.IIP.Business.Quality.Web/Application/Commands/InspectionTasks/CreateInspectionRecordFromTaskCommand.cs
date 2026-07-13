using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

/// <summary>
/// 从任务建检验记录的权威结论：记录 id、后端按检验计划规格 + AQL 计算的 <c>Result</c>
/// （passed / rejected / conditional-release），以及不合格时后端**同事务内自动开出**并回链的 NCR id。
/// PDA 结果页据此展示权威结论与 NCR 互链，而不是提交前的客户端预判。
/// </summary>
public sealed record CreateInspectionRecordFromTaskResult(
    InspectionRecordId InspectionRecordId,
    string Result,
    string? NonconformanceReportId);

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

        // 幂等：任务已完成 → 回读既有记录的权威结论（含此前开出的 NCR），不重复创建。
        if (task.Status == InspectionTaskStatuses.Completed && task.InspectionRecordId is not null)
        {
            var completed = await inspectionRecordRepository.GetAsync(task.InspectionRecordId, cancellationToken);
            return ToResult(task.InspectionRecordId, completed);
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
            return ToResult(existing.Id, existing);
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

        // 权威结论非合格（rejected / conditional-release）→ 同事务内自动开出 NCR 并回链，
        // 使「不合格 → 已发起 NCR」为真、并给结果页提供 NCR 互链（passed 不开）。
        if (record.Result != InspectionRecordResults.Passed && record.NonconformanceReportId is null)
        {
            var ncrCode = await nonconformanceReportCodeGenerator.NextAsync(record.OrganizationId, record.EnvironmentId, cancellationToken);
            var ncr = NonconformanceReport.OpenFromInspection(
                ncrCode,
                record,
                request.DispositionReason ?? record.DispositionReason ?? string.Empty,
                request.DispositionAttachmentFileIds);
            record.LinkNonconformanceReport(ncr.Id.ToString());
            await nonconformanceReportRepository.AddAsync(ncr, cancellationToken);
        }

        return new CreateInspectionRecordFromTaskResult(record.Id, record.Result, record.NonconformanceReportId);
    }

    private static CreateInspectionRecordFromTaskResult ToResult(InspectionRecordId recordId, InspectionRecord? record) =>
        record is null
            ? new CreateInspectionRecordFromTaskResult(recordId, string.Empty, null)
            : new CreateInspectionRecordFromTaskResult(recordId, record.Result, record.NonconformanceReportId);
}
