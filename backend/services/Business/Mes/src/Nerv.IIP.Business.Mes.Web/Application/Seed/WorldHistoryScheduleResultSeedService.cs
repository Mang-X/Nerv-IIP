using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的**规则排程**块：历次排程运行及其工序分配。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行——分配只引用**库里真实存在**的
/// 工序任务，周次也只取工序任务实际用过的周计划号，不凭空造工序号或周次。
///
/// 注意 <c>schedule_results</c> 表**没有 organization_id / environment_id 列**（既有模型如此），
/// 因此本块的 scope 参数只用于挑选工序任务锚点，排程结果行本身是全局的。
/// 幂等自然键退化为 <c>schedule_version</c>（表上唯一索引）。
/// </summary>
public sealed class WorldHistoryScheduleResultSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批写入条数。批末 <c>SaveChanges</c> 并清变更跟踪器。</summary>
    public const int BatchSize = 50;

    public async Task<WorldHistoryScheduleResultSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var anchors = await LoadAssignmentAnchorsAsync(organizationId, environmentId, cancellationToken);
        if (anchors.Count == 0)
        {
            // 工单链还没落库：没有工序可排，宁可不写也不造假工序号。
            return new WorldHistoryScheduleResultSeedReport(0, new WorldHistoryScheduleResultValidationReport(0));
        }

        var runs = WorldHistoryScheduleResultSpec.BuildRuns([.. anchors.Keys], scale);
        var written = 0;

        for (var batchStart = 0; batchStart < runs.Count; batchStart += BatchSize)
        {
            var batch = runs.Skip(batchStart).Take(BatchSize).ToArray();
            var versions = batch.Select(x => x.ScheduleVersion).ToArray();
            var existing = (await dbContext.ScheduleResults
                    .AsNoTracking()
                    .Where(x => versions.Contains(x.ScheduleVersion))
                    .Select(x => x.ScheduleVersion)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet();

            var added = 0;
            foreach (var run in batch.Where(x => !existing.Contains(x.ScheduleVersion)))
            {
                var assignments = BuildAssignments(run, anchors[run.SchedulePlanId]);
                if (assignments.Count == 0)
                {
                    continue;
                }

                dbContext.ScheduleResults.Add(ScheduleResult.Create(
                    run.ScheduleVersion,
                    run.Trigger,
                    run.ScheduledAtUtc,
                    assignments,
                    [.. assignments.Select(x => x.WorkOrderId).Distinct(StringComparer.Ordinal)]));
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateScheduleResultsAsync(cancellationToken);

        return new WorldHistoryScheduleResultSeedReport(written, validation);
    }

    private static IReadOnlyCollection<ScheduledOperationSnapshot> BuildAssignments(
        WorldHistoryScheduleRun run,
        IReadOnlyList<OperationAnchor> anchors)
    {
        if (anchors.Count == 0)
        {
            return [];
        }

        var offset = run.AssignmentOffset % anchors.Count;
        var take = Math.Min(run.AssignmentTake, anchors.Count);
        var reason = WorldHistoryScheduleResultSpec.ReasonText(run.Trigger);

        return
        [
            .. Enumerable.Range(0, take)
                .Select(index => anchors[(offset + index) % anchors.Count])
                .DistinctBy(x => x.OperationTaskId, StringComparer.Ordinal)
                .Select(anchor => new ScheduledOperationSnapshot(
                    anchor.WorkOrderId,
                    anchor.OperationTaskId,
                    anchor.WorkCenterId,
                    anchor.StartUtc,
                    anchor.EndUtc,
                    reason)),
        ];
    }

    /// <summary>
    /// 排程分配的锚点：L1 号段内、已挂周计划号的工序任务，按周计划号分组。
    /// 排序键取工序任务号（含工单号与工序序号），保证确定性且与时间同向。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<OperationAnchor>>> LoadAssignmentAnchorsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderId.StartsWith("WO-2026-") &&
                x.SchedulePlanId != null &&
                x.SchedulePlanId.StartsWith(WorldHistoryScheduleResultSpec.SchedulePlanIdPrefix))
            .OrderBy(x => x.OperationTaskIdValue)
            .Select(x => new
            {
                x.SchedulePlanId,
                x.WorkOrderId,
                x.OperationTaskIdValue,
                x.WorkCenterId,
                x.EarliestStartUtc,
                x.ExistingStartUtc,
                x.ExistingEndUtc,
                x.DurationTicks,
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(x => x.SchedulePlanId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OperationAnchor>)
                [
                    .. group.Select(x =>
                    {
                        var startUtc = x.ExistingStartUtc ?? x.EarliestStartUtc;
                        var endUtc = x.ExistingEndUtc ?? startUtc.AddTicks(x.DurationTicks);
                        return new OperationAnchor(
                            x.WorkOrderId,
                            x.OperationTaskIdValue,
                            x.WorkCenterId,
                            startUtc,
                            endUtc < startUtc ? startUtc : endUtc);
                    }),
                ],
                StringComparer.Ordinal);
    }

    private sealed record OperationAnchor(
        string WorkOrderId,
        string OperationTaskId,
        string WorkCenterId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);
}

/// <summary>一次 L1 规则排程块生成的产出摘要。</summary>
public sealed record WorldHistoryScheduleResultSeedReport(
    int ScheduleResultsWritten,
    WorldHistoryScheduleResultValidationReport Validation);
