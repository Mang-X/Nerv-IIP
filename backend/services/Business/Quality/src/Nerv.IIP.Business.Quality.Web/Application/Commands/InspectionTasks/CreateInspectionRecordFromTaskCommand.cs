using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Coding;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Errors;

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
    string? NonconformanceReportCode,
    DateTimeOffset ChangedAtUtc);

public sealed record CreateInspectionRecordFromTaskCommand(
    InspectionTaskId InspectionTaskId,
    string InspectorUserId,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    string? IdempotencyKey,
    string? OrganizationId = null,
    string? EnvironmentId = null) : ICommand<CreateInspectionRecordFromTaskResult>;

public sealed class CreateInspectionRecordFromTaskCommandLock : ICommandLock<CreateInspectionRecordFromTaskCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        CreateInspectionRecordFromTaskCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InspectionTaskCommandLocks.For(command.InspectionTaskId));
    }
}

public sealed class CreateInspectionRecordFromTaskCommandValidator : AbstractValidator<CreateInspectionRecordFromTaskCommand>
{
    public CreateInspectionRecordFromTaskCommandValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty();
        RuleFor(x => x.InspectorUserId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ResultLines).NotEmpty();
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class CreateInspectionRecordFromTaskCommandHandler(
    IInspectionTaskRepository inspectionTaskRepository,
    IInspectionRecordRepository inspectionRecordRepository,
    IInspectionPlanRepository inspectionPlanRepository,
    INonconformanceReportRepository nonconformanceReportRepository,
    INonconformanceReportCodeGenerator nonconformanceReportCodeGenerator,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateInspectionRecordFromTaskCommand, CreateInspectionRecordFromTaskResult>
{
    private const string SubmitInspectionRuleKey = "inspection-task-submit";

    public async Task<CreateInspectionRecordFromTaskResult> Handle(CreateInspectionRecordFromTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await inspectionTaskRepository.GetAsync(request.InspectionTaskId, cancellationToken)
            ?? throw new KnownException($"Inspection task '{request.InspectionTaskId}' was not found.");
        if ((request.OrganizationId is not null
                && !string.Equals(task.OrganizationId, request.OrganizationId, StringComparison.Ordinal))
            || (request.EnvironmentId is not null
                && !string.Equals(task.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal)))
        {
            throw new KnownException($"Inspection task '{request.InspectionTaskId}' was not found.");
        }
        var replay = await TryGetReplayAsync(task, request, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        try
        {
            task.EnsureAssignedInspector(request.InspectorUserId);
        }
        catch (UnauthorizedAccessException)
        {
            throw QualityAuthorizationException.Forbidden(
                "assignment-principal-mismatch");
        }

        // 幂等：任务已完成 → 回读既有记录的权威结论。仍走统一收尾（既有 rejected 记录若因常规
        // 检验流程未开 NCR，会在这里补开并回链，避免端点永久返回 NonconformanceReportId=null）。
        if (task.Status == InspectionTaskStatuses.Completed && task.InspectionRecordId is not null)
        {
            var completed = await inspectionRecordRepository.GetAsync(task.InspectionRecordId, cancellationToken);
            var completedResult = await EnsureNcrAndBuildResultAsync(task.InspectionRecordId, completed, cancellationToken);
            return AddIdempotencyRecord(task, request, completedResult);
        }

        if (task.Status != InspectionTaskStatuses.InProgress)
        {
            throw new QualityLifecycleConflictException("create-inspection-record-from-task", task.Status);
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
            task.Complete(existing.Id, DateTimeOffset.UtcNow);
            var existingResult = await EnsureNcrAndBuildResultAsync(existing.Id, existing, cancellationToken);
            return AddIdempotencyRecord(task, request, existingResult);
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

        task.Complete(record.Id, DateTimeOffset.UtcNow);
        await inspectionRecordRepository.AddAsync(record, cancellationToken);

        var result = await EnsureNcrAndBuildResultAsync(record.Id, record, cancellationToken);
        return AddIdempotencyRecord(task, request, result);
    }

    private async Task<CreateInspectionRecordFromTaskResult?> TryGetReplayAsync(
        InspectionTask task,
        CreateInspectionRecordFromTaskCommand request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        if (idempotencyKey is null || dbContext is null)
        {
            return null;
        }

        var existingKey = dbContext.CodeIdempotencyKeys.Local.FirstOrDefault(x =>
            x.OrganizationId == task.OrganizationId &&
            x.EnvironmentId == task.EnvironmentId &&
            x.RuleKey == SubmitInspectionRuleKey &&
            x.IdempotencyKey == idempotencyKey)
            ?? await dbContext.CodeIdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(
                x => x.OrganizationId == task.OrganizationId &&
                    x.EnvironmentId == task.EnvironmentId &&
                    x.RuleKey == SubmitInspectionRuleKey &&
                    x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existingKey is null)
        {
            if (task.Status == InspectionTaskStatuses.Completed && task.InspectionRecordId is not null)
            {
                var taskAlreadyBound = await dbContext.CodeIdempotencyKeys.AsNoTracking().AnyAsync(
                    x => x.OrganizationId == task.OrganizationId &&
                        x.EnvironmentId == task.EnvironmentId &&
                        x.RuleKey == SubmitInspectionRuleKey &&
                        x.Code == task.InspectionRecordId.ToString(),
                    cancellationToken);
                if (taskAlreadyBound)
                {
                    throw new KnownException("inspection-task-already-completed-with-a-different-idempotency-key");
                }
            }

            return null;
        }

        if (!string.Equals(existingKey.PayloadFingerprint, Fingerprint(request), StringComparison.Ordinal))
        {
            throw new QualityIdempotencyConflictException();
        }

        if (!Guid.TryParse(existingKey.Code, out var recordGuid))
        {
            throw new KnownException("stored-inspection-task-receipt-is-invalid");
        }

        var recordId = new InspectionRecordId(recordGuid);
        var record = await inspectionRecordRepository.GetAsync(recordId, cancellationToken);
        return await EnsureNcrAndBuildResultAsync(recordId, record, cancellationToken);
    }

    private CreateInspectionRecordFromTaskResult AddIdempotencyRecord(
        InspectionTask task,
        CreateInspectionRecordFromTaskCommand request,
        CreateInspectionRecordFromTaskResult result)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        if (idempotencyKey is null || dbContext is null)
        {
            return result;
        }

        dbContext.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            task.OrganizationId,
            task.EnvironmentId,
            SubmitInspectionRuleKey,
            idempotencyKey,
            result.InspectionRecordId.ToString(),
            Fingerprint(request),
            DateTimeOffset.UtcNow));
        return result;
    }

    private static string Fingerprint(CreateInspectionRecordFromTaskCommand request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            inspectionTaskId = request.InspectionTaskId.ToString(),
            inspectorUserId = request.InspectorUserId.Trim(),
            resultLines = request.ResultLines
                .Select(line => new
                {
                    characteristicCode = line.CharacteristicCode.Trim(),
                    observedValue = line.ObservedValue.Trim(),
                    unitCode = Normalize(line.UnitCode),
                    result = line.Result.Trim(),
                    defectReason = Normalize(line.DefectReason),
                    defectQuantity = CanonicalDecimal(line.DefectQuantity),
                    attachmentFileIds = line.AttachmentFileIds
                        .Select(value => value.Trim())
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    measuredValue = CanonicalDecimal(line.MeasuredValue),
                })
                .OrderBy(line => line.characteristicCode, StringComparer.Ordinal)
                .ThenBy(line => line.observedValue, StringComparer.Ordinal)
                .ThenBy(line => line.result, StringComparer.Ordinal)
                .ThenBy(line => line.unitCode, StringComparer.Ordinal)
                .ThenBy(line => line.defectReason, StringComparer.Ordinal)
                .ThenBy(line => line.defectQuantity, StringComparer.Ordinal)
                .ThenBy(line => JsonSerializer.Serialize(line.attachmentFileIds), StringComparer.Ordinal)
                .ThenBy(line => line.measuredValue, StringComparer.Ordinal),
            dispositionReason = request.DispositionReason?.Trim(),
            dispositionAttachmentFileIds = request.DispositionAttachmentFileIds
                .Select(value => value.Trim())
                .Order(StringComparer.Ordinal),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CanonicalDecimal(decimal? value) =>
        value?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);

    private static string? NormalizeIdempotencyKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            throw new KnownException("stored-inspection-task-receipt-points-to-missing-record");
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
            return new CreateInspectionRecordFromTaskResult(
                record.Id,
                record.Result,
                record.NonconformanceReportId,
                ncr.NcrCode,
                ToUtcOffset(record.UpdatedAtUtc));
        }

        // 已回链 → 回读 NCR 业务编号（GUID 不是人读单号）。
        string? linkedNcrCode = null;
        if (record.NonconformanceReportId is not null
            && Guid.TryParse(record.NonconformanceReportId, out var linkedNcrGuid))
        {
            var linked = await nonconformanceReportRepository.GetAsync(new NonconformanceReportId(linkedNcrGuid), cancellationToken);
            linkedNcrCode = linked?.NcrCode;
        }

        return new CreateInspectionRecordFromTaskResult(
            record.Id,
            record.Result,
            record.NonconformanceReportId,
            linkedNcrCode,
            ToUtcOffset(record.UpdatedAtUtc));
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
