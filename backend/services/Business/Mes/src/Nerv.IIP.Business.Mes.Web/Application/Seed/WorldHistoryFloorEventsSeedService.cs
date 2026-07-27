using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的**现场异常与协同**块：停机事件、班次交接、车间不良。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行——不良记录只挂在**库里真实存在**的
/// 工单/工序任务上（不凭空造工单号），候选集从 <c>mes.operation_tasks</c> 已落库的行里按
/// 确定性规则挑选。
///
/// 与既有 L1 块同一套约束：确定性纯函数产出事实（<see cref="WorldHistoryFloorEventsSpec"/>）、
/// 分批写入 + 批末清变更跟踪器、按业务单号自然键幂等、fail-closed 一致性校验。
/// </summary>
public sealed class WorldHistoryFloorEventsSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批写入条数。批末 <c>SaveChanges</c> 并清变更跟踪器。</summary>
    public const int BatchSize = 200;

    public async Task<WorldHistoryFloorEventsSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var downtimeWritten = await SeedDowntimeEventsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var handoverWritten = await SeedShiftHandoversAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var defectWritten = await SeedDefectRecordsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        // fail-closed：形状/引用/边界对不上就让 seed 失败（与工单链校验同一策略）。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateFloorEventsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryFloorEventsSeedReport(downtimeWritten, handoverWritten, defectWritten, validation);
    }

    #region 停机事件

    private async Task<int> SeedDowntimeEventsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var events = WorldHistoryFloorEventsSpec.BuildDowntimeEvents(asOfDate, scale);
        if (events.Count == 0)
        {
            return 0;
        }

        var written = 0;
        for (var batchStart = 0; batchStart < events.Count; batchStart += BatchSize)
        {
            var batch = events.Skip(batchStart).Take(BatchSize).ToArray();
            var existing = await LoadExistingCodesAsync(
                dbContext.WorkCenterUnavailabilities
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                    .Select(x => x.DowntimeEventNo),
                batch.Select(x => x.DowntimeEventNo).ToArray(),
                cancellationToken);

            var added = 0;
            foreach (var downtime in batch.Where(x => !existing.Contains(x.DowntimeEventNo)))
            {
                dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
                    organizationId,
                    environmentId,
                    downtime.DowntimeEventNo,
                    downtime.WorkCenterId,
                    downtime.FromUtc,
                    downtime.ToUtc,
                    downtime.Reason,
                    downtime.DeviceAssetId));
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    #endregion

    #region 班次交接

    private async Task<int> SeedShiftHandoversAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var handovers = WorldHistoryFloorEventsSpec.BuildShiftHandovers(asOfDate, scale);
        if (handovers.Count == 0)
        {
            return 0;
        }

        var written = 0;
        for (var batchStart = 0; batchStart < handovers.Count; batchStart += BatchSize)
        {
            var batch = handovers.Skip(batchStart).Take(BatchSize).ToArray();
            var existing = await LoadExistingCodesAsync(
                dbContext.ShiftHandovers
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                    .Select(x => x.HandoverNo),
                batch.Select(x => x.HandoverNo).ToArray(),
                cancellationToken);

            var added = 0;
            foreach (var plan in batch.Where(x => !existing.Contains(x.HandoverNo)))
            {
                var handover = ShiftHandover.Create(
                    organizationId,
                    environmentId,
                    plan.HandoverNo,
                    plan.ShiftId,
                    plan.TeamId,
                    plan.OpenIssueCount,
                    plan.CreatedAtUtc);
                if (plan.AcceptedAtUtc is { } acceptedAtUtc)
                {
                    handover.Accept(acceptedAtUtc);
                }

                dbContext.ShiftHandovers.Add(handover);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    #endregion

    #region 车间不良

    private async Task<int> SeedDefectRecordsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var slots = WorldHistoryFloorEventsSpec.BuildDefectSlots(asOfDate, scale);
        if (slots.Count == 0)
        {
            return 0;
        }

        var anchors = await LoadDefectAnchorsAsync(organizationId, environmentId, cancellationToken);
        if (anchors.Length == 0)
        {
            // 工单链还没落库（或本环境没有 L1 历史）：不良无处可挂，宁可不写也不造假工单号。
            return 0;
        }

        var total = Math.Min(slots.Count, anchors.Length);
        var written = 0;
        for (var batchStart = 0; batchStart < total; batchStart += BatchSize)
        {
            var batch = slots.Skip(batchStart).Take(Math.Min(BatchSize, total - batchStart)).ToArray();
            var existing = await LoadExistingCodesAsync(
                dbContext.DefectRecords
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                    .Select(x => x.DefectNo),
                batch.Select(x => x.DefectNo).ToArray(),
                cancellationToken);

            var added = 0;
            for (var offset = 0; offset < batch.Length; offset++)
            {
                var slot = batch[offset];
                if (existing.Contains(slot.DefectNo))
                {
                    continue;
                }

                // 槽位序号 → 候选工序任务：按候选集（工单号升序即时间升序）均匀铺开，
                // 于是不良沿 29 周历史分布，而不是全堆在某几张工单上。
                var slotIndex = batchStart + offset;
                var anchor = anchors[(int)((long)slotIndex * anchors.Length / total)];

                var defect = DefectRecord.Create(
                    organizationId,
                    environmentId,
                    slot.DefectNo,
                    anchor.WorkOrderId,
                    anchor.OperationTaskId,
                    slot.DefectCode,
                    slot.Quantity,
                    anchor.RecordedAtUtc);

                if (slot is { NcrCode: { } ncrCode, DispositionType: { } dispositionType })
                {
                    defect.AcceptDisposition(
                        ncrId: ncrCode,
                        ncrCode: ncrCode,
                        dispositionType: dispositionType,
                        dispositionReferenceId: null,
                        changedAtUtc: anchor.RecordedAtUtc.AddMinutes(slot.DispositionDelayMinutes));
                }

                // 历史回填不重放当时的领域事件：DefectRaised 会让质量域按**今天**再开一张 NCR，
                // 与设定集 §7 的 NCR 规模和历史时间戳都对不上。历史处置结论直接写在本行上。
                defect.ClearDomainEvents();
                dbContext.DefectRecords.Add(defect);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    /// <summary>
    /// 不良可挂靠的工序任务候选集：只取 L1 号段内**已完工**且有真实执行窗口的工序，
    /// 按工序任务号排序（即工单号顺序，与时间同向）保证确定性。
    /// </summary>
    private async Task<DefectAnchor[]> LoadDefectAnchorsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderId.StartsWith("WO-2026-") &&
                x.Status == OperationTaskLifecycleStatus.Completed &&
                x.ExistingEndUtc != null)
            .OrderBy(x => x.OperationTaskIdValue)
            .Select(x => new DefectAnchor(x.WorkOrderId, x.OperationTaskIdValue, x.ExistingEndUtc!.Value))
            .ToArrayAsync(cancellationToken);

    private sealed record DefectAnchor(string WorkOrderId, string OperationTaskId, DateTimeOffset RecordedAtUtc);

    #endregion

    private static async Task<HashSet<string>> LoadExistingCodesAsync(
        IQueryable<string> source,
        string[] codes,
        CancellationToken cancellationToken) =>
        (await source.Where(code => codes.Contains(code)).ToArrayAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);
}

/// <summary>一次 L1 现场异常与协同块生成的产出摘要。</summary>
public sealed record WorldHistoryFloorEventsSeedReport(
    int DowntimeEventsWritten,
    int ShiftHandoversWritten,
    int DefectRecordsWritten,
    WorldHistoryFloorEventsValidationReport Validation);
