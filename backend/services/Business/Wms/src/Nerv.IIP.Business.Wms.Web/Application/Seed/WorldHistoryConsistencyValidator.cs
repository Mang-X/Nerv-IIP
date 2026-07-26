using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **仓储域侧**（二期）。
///
/// 覆盖：单据总量与计划对账、每张单据都落到终态、每张单据至少一条作业任务且计划量 == 执行量 == 行数量、
/// 上架 / 拣货的起止库位与单据一致、出库复核已通过、库存过账请求已标记过账、
/// 时间戳落在历史区间且不在周日、与固定演示事实隔离。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryWmsConsistencyException"/>。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 20;

    private const decimal QuantityTolerance = 0.000001m;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryWmsValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var documents = WorldHistoryWmsSpec.BuildDocuments(asOfDate, scale);
        var inboundPlan = documents.InboundOrders.ToDictionary(x => x.InboundOrderNo, StringComparer.Ordinal);
        var outboundPlan = documents.OutboundOrders.ToDictionary(x => x.OutboundOrderNo, StringComparer.Ordinal);
        var failures = new List<string>();

        var inbounds = await LoadInboundOrdersAsync(organizationId, environmentId, cancellationToken);
        var outbounds = await LoadOutboundOrdersAsync(organizationId, environmentId, cancellationToken);
        var tasks = await LoadWarehouseTasksAsync(organizationId, environmentId, cancellationToken);
        var postedRequestKeys = await LoadPostedRequestKeysAsync(organizationId, environmentId, cancellationToken);

        var tasksByOrder = tasks
            .GroupBy(x => x.SourceOrderNo, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TaskProjection>)[.. group], StringComparer.Ordinal);

        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        CheckPopulation(inboundPlan.Keys, inbounds.Select(x => x.InboundOrderNo), "入库单", failures);
        CheckPopulation(outboundPlan.Keys, outbounds.Select(x => x.OutboundOrderNo), "出库单", failures);

        foreach (var inbound in inbounds)
        {
            if (!inboundPlan.TryGetValue(inbound.InboundOrderNo, out var plan))
            {
                failures.Add($"库内入库单 {inbound.InboundOrderNo} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            CheckInbound(inbound, plan, tasksByOrder, postedRequestKeys, lowerBound, upperBound, failures);
        }

        foreach (var outbound in outbounds)
        {
            if (!outboundPlan.TryGetValue(outbound.OutboundOrderNo, out var plan))
            {
                failures.Add($"库内出库单 {outbound.OutboundOrderNo} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            CheckOutbound(outbound, plan, tasksByOrder, postedRequestKeys, lowerBound, upperBound, failures);
        }

        CheckIsolation(inbounds, outbounds, tasks, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryWmsConsistencyException(failures);
        }

        return new WorldHistoryWmsValidationReport(
            InboundOrdersChecked: inbounds.Count,
            OutboundOrdersChecked: outbounds.Count,
            WarehouseTasksChecked: tasks.Count,
            PutawayTasksChecked: tasks.Count(x => x.TaskType == WarehouseTaskType.Putaway),
            PickingTasksChecked: tasks.Count(x => x.TaskType == WarehouseTaskType.Picking),
            PostedMovementRequestsChecked: postedRequestKeys.Count,
            Sample: BuildSample(documents));
    }

    #region 校验项

    private static void CheckPopulation(
        IEnumerable<string> planned,
        IEnumerable<string> present,
        string label,
        List<string> failures)
    {
        var stored = present.ToHashSet(StringComparer.Ordinal);
        foreach (var missing in planned.Where(key => !stored.Contains(key)).Take(5))
        {
            failures.Add($"计划中的{label} {missing} 未落库。");
        }
    }

    private static void CheckInbound(
        InboundProjection inbound,
        WorldHistoryInboundDocument plan,
        Dictionary<string, IReadOnlyList<TaskProjection>> tasksByOrder,
        HashSet<string> postedRequestKeys,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        // 1) 终态：历史入库单必须已完成（收货单要过来料门禁，完工入库单直接完成）。
        if (inbound.Status != InboundOrderStatus.Completed)
        {
            failures.Add($"入库单 {inbound.InboundOrderNo} 状态为 {inbound.Status}，历史单据必须已完成。");
        }

        if (!string.Equals(inbound.SourceDocumentType, plan.SourceDocumentType, StringComparison.Ordinal) ||
            !string.Equals(inbound.SourceDocumentId, plan.SourceDocumentId, StringComparison.Ordinal))
        {
            failures.Add($"入库单 {inbound.InboundOrderNo} 的源单据与计划不符。");
        }

        // 2) 行数量：一单一行，数量取自共享形状。
        if (inbound.LineCount != 1 || Math.Abs(inbound.ReceivedQuantity - plan.Quantity) > QuantityTolerance)
        {
            failures.Add(
                $"入库单 {inbound.InboundOrderNo} 有 {inbound.LineCount} 行、收货 {inbound.ReceivedQuantity}，计划为 1 行 {plan.Quantity}。");
        }

        if (plan.RequiresQualityInspection && !inbound.IsReleasedForPutaway)
        {
            failures.Add($"入库单 {inbound.InboundOrderNo} 走了来料检验，却没有放行上架的检验结论。");
        }

        // 3) 作业任务：至少一条，且计划量 == 执行量 == 行数量，起止库位与计划一致。
        CheckTasks(
            inbound.InboundOrderNo, plan.Quantity, WarehouseTaskType.Putaway,
            plan.PutawayFromLocationCode, plan.PutawayToLocationCode,
            tasksByOrder, lowerBound, upperBound, failures);

        // 4) 库存过账请求：历史里必须已过账，否则页面会显示成一堆待过账积压。
        if (!postedRequestKeys.Contains(plan.MovementIdempotencyKey))
        {
            failures.Add($"入库单 {inbound.InboundOrderNo} 的库存过账请求 {plan.MovementIdempotencyKey} 未标记已过账。");
        }

        CheckMoment($"入库单 {inbound.InboundOrderNo} 创建", inbound.CreatedAtUtc, lowerBound, upperBound, failures);
        if (inbound.CompletedAtUtc is { } completedAtUtc)
        {
            CheckMoment($"入库单 {inbound.InboundOrderNo} 完成", completedAtUtc, lowerBound, upperBound, failures);
            if (completedAtUtc < inbound.CreatedAtUtc)
            {
                failures.Add($"入库单 {inbound.InboundOrderNo} 完成时间早于创建时间。");
            }
        }
        else
        {
            failures.Add($"入库单 {inbound.InboundOrderNo} 没有完成时间。");
        }
    }

    private static void CheckOutbound(
        OutboundProjection outbound,
        WorldHistoryOutboundDocument plan,
        Dictionary<string, IReadOnlyList<TaskProjection>> tasksByOrder,
        HashSet<string> postedRequestKeys,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (outbound.Status != OutboundOrderStatus.Completed)
        {
            failures.Add($"出库单 {outbound.OutboundOrderNo} 状态为 {outbound.Status}，历史单据必须已完成。");
        }

        if (!string.Equals(outbound.SourceDocumentType, plan.SourceDocumentType, StringComparison.Ordinal) ||
            !string.Equals(outbound.SourceDocumentId, plan.SourceDocumentId, StringComparison.Ordinal))
        {
            failures.Add($"出库单 {outbound.OutboundOrderNo} 的源单据与计划不符。");
        }

        if (outbound.LineCount != 1 ||
            Math.Abs(outbound.RequestedQuantity - plan.Quantity) > QuantityTolerance ||
            Math.Abs(outbound.IssuedQuantity - plan.Quantity) > QuantityTolerance)
        {
            failures.Add(
                $"出库单 {outbound.OutboundOrderNo} 有 {outbound.LineCount} 行、需求 {outbound.RequestedQuantity}、" +
                $"实发 {outbound.IssuedQuantity}，计划为 1 行 {plan.Quantity}。");
        }

        if (outbound.PackReviewPassed != true || !string.Equals(outbound.PackReviewNo, plan.PackReviewNo, StringComparison.Ordinal))
        {
            failures.Add($"出库单 {outbound.OutboundOrderNo} 的复核未通过或复核单号与计划不符。");
        }

        if (Math.Abs(outbound.BackorderQuantity) > QuantityTolerance)
        {
            failures.Add($"出库单 {outbound.OutboundOrderNo} 留下了缺量 {outbound.BackorderQuantity}，历史里不应有短拣。");
        }

        CheckTasks(
            outbound.OutboundOrderNo, plan.Quantity, WarehouseTaskType.Picking,
            plan.PickFromLocationCode, plan.PickToLocationCode,
            tasksByOrder, lowerBound, upperBound, failures);

        if (!postedRequestKeys.Contains(plan.MovementIdempotencyKey))
        {
            failures.Add($"出库单 {outbound.OutboundOrderNo} 的库存过账请求 {plan.MovementIdempotencyKey} 未标记已过账。");
        }

        CheckMoment($"出库单 {outbound.OutboundOrderNo} 创建", outbound.CreatedAtUtc, lowerBound, upperBound, failures);
        if (outbound.CompletedAtUtc is { } completedAtUtc)
        {
            CheckMoment($"出库单 {outbound.OutboundOrderNo} 完成", completedAtUtc, lowerBound, upperBound, failures);
            if (completedAtUtc < outbound.CreatedAtUtc)
            {
                failures.Add($"出库单 {outbound.OutboundOrderNo} 完成时间早于创建时间。");
            }
        }
        else
        {
            failures.Add($"出库单 {outbound.OutboundOrderNo} 没有完成时间。");
        }
    }

    private static void CheckTasks(
        string orderNo,
        decimal expectedQuantity,
        WarehouseTaskType expectedType,
        string expectedFromLocationCode,
        string expectedToLocationCode,
        Dictionary<string, IReadOnlyList<TaskProjection>> tasksByOrder,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (!tasksByOrder.TryGetValue(orderNo, out var tasks) || tasks.Count == 0)
        {
            failures.Add($"单据 {orderNo} 没有任何仓储作业任务。");
            return;
        }

        foreach (var task in tasks)
        {
            if (task.TaskType != expectedType)
            {
                failures.Add($"作业任务 {task.TaskNo} 类型为 {task.TaskType}，期望 {expectedType}。");
            }

            if (task.Status != WarehouseTaskStatus.Completed)
            {
                failures.Add($"作业任务 {task.TaskNo} 状态为 {task.Status}，历史任务必须已完成。");
            }

            if (Math.Abs(task.PlannedQuantity - expectedQuantity) > QuantityTolerance ||
                Math.Abs(task.ExecutedQuantity - expectedQuantity) > QuantityTolerance)
            {
                failures.Add(
                    $"作业任务 {task.TaskNo} 计划 {task.PlannedQuantity} / 执行 {task.ExecutedQuantity}，与单据行数量 {expectedQuantity} 不平。");
            }

            if (!string.Equals(task.FromLocationCode, expectedFromLocationCode, StringComparison.Ordinal) ||
                !string.Equals(task.ToLocationCode, expectedToLocationCode, StringComparison.Ordinal))
            {
                failures.Add(
                    $"作业任务 {task.TaskNo} 的搬运路径 {task.FromLocationCode} → {task.ToLocationCode} 与计划不符。");
            }

            CheckMoment($"作业任务 {task.TaskNo} 创建", task.CreatedAtUtc, lowerBound, upperBound, failures);
            if (task.CompletedAtUtc is { } completedAtUtc)
            {
                CheckMoment($"作业任务 {task.TaskNo} 完成", completedAtUtc, lowerBound, upperBound, failures);
            }
            else
            {
                failures.Add($"作业任务 {task.TaskNo} 没有完成时间。");
            }
        }
    }

    private static void CheckMoment(
        string label,
        DateTime moment,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (moment < lowerBound || moment > upperBound)
        {
            failures.Add($"{label}时间 {moment:O} 落在历史区间之外。");
        }

        if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(moment)))
        {
            failures.Add($"{label}时间 {moment:O} 落在周日（停产保养日）。");
        }
    }

    /// <summary>与 MAN-519 固定演示事实、千单规模块完全隔离。</summary>
    private static void CheckIsolation(
        IReadOnlyList<InboundProjection> inbounds,
        IReadOnlyList<OutboundProjection> outbounds,
        IReadOnlyList<TaskProjection> tasks,
        List<string> failures)
    {
        var codes = inbounds.Select(x => x.InboundOrderNo)
            .Concat(inbounds.Select(x => x.SourceDocumentId))
            .Concat(outbounds.Select(x => x.OutboundOrderNo))
            .Concat(outbounds.Select(x => x.SourceDocumentId))
            .Concat(tasks.Select(x => x.TaskNo));

        foreach (var code in codes)
        {
            foreach (var infix in ReservedInfixes)
            {
                if (code.Contains(infix, StringComparison.Ordinal))
                {
                    failures.Add($"仓储单据号 {code} 落进了保留号段 '{infix}'。");
                }
            }
        }
    }

    #endregion

    #region 载入紧凑投影

    // 行级聚合（Count / Sum / All）在投影里会被拆成子查询，InMemory provider 无法翻译，
    // 且行上的 RequiresQualityInspection / IsReleasedForPutaway 是计算属性、根本没有列可翻译。
    // 因此这里连行一起取回来，在内存里压成紧凑投影——一单一行，materialize 的规模与单据数同阶。
    private async Task<IReadOnlyList<InboundProjection>> LoadInboundOrdersAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        [.. (await dbContext.InboundOrders
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.InboundOrderNo.StartsWith("IB-"))
                .ToArrayAsync(cancellationToken))
            .Select(x => new InboundProjection(
                x.InboundOrderNo,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.Status,
                x.Lines.Count,
                x.Lines.Sum(line => line.ReceivedQuantity),
                x.Lines.All(line => !line.RequiresQualityInspection || line.IsReleasedForPutaway),
                x.CreatedAtUtc,
                x.CompletedAtUtc))];

    private async Task<IReadOnlyList<OutboundProjection>> LoadOutboundOrdersAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        [.. (await dbContext.OutboundOrders
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.OutboundOrderNo.StartsWith("OB-"))
                .ToArrayAsync(cancellationToken))
            .Select(x => new OutboundProjection(
                x.OutboundOrderNo,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.Status,
                x.PackReviewNo,
                x.PackReviewPassed,
                x.Lines.Count,
                x.Lines.Sum(line => line.RequestedQuantity),
                x.Lines.Sum(line => line.IssuedQuantity),
                x.Lines.Sum(line => line.BackorderQuantity),
                x.CreatedAtUtc,
                x.CompletedAtUtc))];

    private async Task<IReadOnlyList<TaskProjection>> LoadWarehouseTasksAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await dbContext.WarehouseTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.TaskNo.StartsWith("WT-"))
            .Select(x => new TaskProjection(
                x.TaskNo,
                x.SourceOrderNo,
                x.TaskType,
                x.Status,
                x.FromLocationCode,
                x.ToLocationCode,
                x.PlannedQuantity,
                x.ExecutedQuantity,
                x.CreatedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

    private async Task<HashSet<string>> LoadPostedRequestKeysAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        (await dbContext.InventoryMovementRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.Status == InventoryMovementRequestStatus.Posted)
            .Select(x => x.IdempotencyKey)
            .ToArrayAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    #endregion

    private static IReadOnlyList<string> BuildSample(WorldHistoryWarehouseDocuments documents)
    {
        var lines = new List<string>();
        foreach (var inbound in documents.InboundOrders)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{inbound.CreatedAtUtc:yyyy-MM-dd HH:mm}Z 入库 {inbound.InboundOrderNo} ({inbound.SourceDocumentType}/{inbound.SourceDocumentId}) " +
                $"{inbound.SkuCode}×{inbound.Quantity:0.##} [{inbound.LotNo}] {inbound.PutawayFromLocationCode}→{inbound.PutawayToLocationCode} " +
                $"任务={inbound.WarehouseTaskNo} 库管={inbound.ExecutorUserId}"));
        }

        foreach (var outbound in documents.OutboundOrders)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{outbound.CreatedAtUtc:yyyy-MM-dd HH:mm}Z 出库 {outbound.OutboundOrderNo} ({outbound.SourceDocumentType}/{outbound.SourceDocumentId}) " +
                $"{outbound.SkuCode}×{outbound.Quantity:0.##} [{outbound.LotNo}] {outbound.PickFromLocationCode}→{outbound.PickToLocationCode} " +
                $"任务={outbound.WarehouseTaskNo} 复核={outbound.PackReviewNo} 库管={outbound.ExecutorUserId}"));
        }

        if (lines.Count == 0)
        {
            return [];
        }

        var ordered = lines.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var stride = Math.Max(1, ordered.Length / SampleSize);
        var sample = new List<string>(SampleSize);
        for (var index = 0; index < ordered.Length && sample.Count < SampleSize; index += stride)
        {
            sample.Add(ordered[index]);
        }

        return sample;
    }

    private sealed record InboundProjection(
        string InboundOrderNo,
        string SourceDocumentType,
        string SourceDocumentId,
        InboundOrderStatus Status,
        int LineCount,
        decimal ReceivedQuantity,
        bool IsReleasedForPutaway,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc);

    private sealed record OutboundProjection(
        string OutboundOrderNo,
        string SourceDocumentType,
        string SourceDocumentId,
        OutboundOrderStatus Status,
        string? PackReviewNo,
        bool? PackReviewPassed,
        int LineCount,
        decimal RequestedQuantity,
        decimal IssuedQuantity,
        decimal BackorderQuantity,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc);

    private sealed record TaskProjection(
        string TaskNo,
        string SourceOrderNo,
        WarehouseTaskType TaskType,
        WarehouseTaskStatus Status,
        string FromLocationCode,
        string ToLocationCode,
        decimal PlannedQuantity,
        decimal ExecutedQuantity,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc);
}

/// <summary>仓储域侧一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryWmsValidationReport(
    int InboundOrdersChecked,
    int OutboundOrdersChecked,
    int WarehouseTasksChecked,
    int PutawayTasksChecked,
    int PickingTasksChecked,
    int PostedMovementRequestsChecked,
    IReadOnlyList<string> Sample);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
public sealed class WorldHistoryWmsConsistencyException : InvalidOperationException
{
    public WorldHistoryWmsConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryWmsConsistencyException()
        : base("World-history WMS consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryWmsConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryWmsConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（仓储域），共 ");
        builder.Append(failures.Count).AppendLine(" 条：");
        foreach (var failure in failures.Take(25))
        {
            builder.Append("  - ").AppendLine(failure);
        }

        if (failures.Count > 25)
        {
            builder.Append("  … 另有 ").Append(failures.Count - 25).AppendLine(" 条未列出。");
        }

        return builder.ToString();
    }
}
