using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

/// <summary>
/// 受理一次 MRP 运行（#1306 异步任务模式的第一跳）：只登记 run 记录（排队态）并立刻返回 runId，
/// 不做任何快照拉取或计算——受理事务必须在网关同步超时红线内完成。
/// 实际计算由 <see cref="MrpRunWorker"/> 在独立事务中通过 <see cref="ExecuteMrpRunCommand"/> 执行。
/// </summary>
public sealed record RunMrpCommand(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd) : ICommand<MrpRunId>;

public sealed class RunMrpCommandValidator : AbstractValidator<RunMrpCommand>
{
    public RunMrpCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.HorizonEnd).GreaterThanOrEqualTo(x => x.HorizonStart);
    }
}

public sealed class RunMrpCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RunMrpCommand, MrpRunId>
{
    public Task<MrpRunId> Handle(RunMrpCommand request, CancellationToken cancellationToken)
    {
        var run = MrpRun.Create(request.OrganizationId, request.EnvironmentId, request.HorizonStart, request.HorizonEnd);
        dbContext.MrpRuns.Add(run);
        return Task.FromResult(run.Id);
    }
}

/// <summary>
/// 把一次排队中的 MRP 运行置为运行中（worker 在计算事务之前的独立事务）：
/// 先提交 Running 再计算，前端轮询才能看到「排队中 → 计算中 → 终态」的真实进程，
/// 进程崩溃时 DB 里遗留的 Running 记录也让启动恢复扫描有据可依。
/// </summary>
public sealed record MarkMrpRunRunningCommand(MrpRunId RunId) : ICommand;

public sealed class MarkMrpRunRunningCommandValidator : AbstractValidator<MarkMrpRunRunningCommand>
{
    public MarkMrpRunRunningCommandValidator()
    {
        RuleFor(x => x.RunId).NotNull();
    }
}

public sealed class MarkMrpRunRunningCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkMrpRunRunningCommand>
{
    public async Task Handle(MarkMrpRunRunningCommand request, CancellationToken cancellationToken)
    {
        var run = await dbContext.MrpRuns.FirstOrDefaultAsync(x => x.Id == request.RunId, cancellationToken)
            ?? throw new KnownException($"MRP 运行不存在：{request.RunId}");
        if (run.Status != MrpRunStatus.Created)
        {
            throw new KnownException($"MRP 运行当前状态为 {run.Status}，不能进入运行中。");
        }

        run.MarkRunning();
    }
}

/// <summary>
/// 后台执行一次已受理的 MRP 运行：拉取输入快照、展开计算、写入建议并置完成态。
/// 与受理事务分离；worker 先经 <see cref="MarkMrpRunRunningCommand"/> 独立事务置运行中，
/// 失败由调用方（worker）另起事务用 <see cref="MarkMrpRunFailedCommand"/> 置失败态。
/// </summary>
public sealed record ExecuteMrpRunCommand(MrpRunId RunId) : ICommand<ExecuteMrpRunCommandResult>;

public sealed record ExecuteMrpRunCommandResult(
    MrpRunId RunId,
    int SuggestionCount,
    bool HasInputDegradation,
    IReadOnlyCollection<string> InputDegradationSources,
    IReadOnlyCollection<string> InputSources,
    DateOnly? InputCoverageStart,
    DateOnly? InputCoverageEnd);

public sealed class ExecuteMrpRunCommandValidator : AbstractValidator<ExecuteMrpRunCommand>
{
    public ExecuteMrpRunCommandValidator()
    {
        RuleFor(x => x.RunId).NotNull();
    }
}

public sealed class ExecuteMrpRunCommandHandler(ApplicationDbContext dbContext, IPlanningInputSnapshotProvider snapshotProvider)
    : ICommandHandler<ExecuteMrpRunCommand, ExecuteMrpRunCommandResult>
{
    public async Task<ExecuteMrpRunCommandResult> Handle(ExecuteMrpRunCommand request, CancellationToken cancellationToken)
    {
        var run = await dbContext.MrpRuns.FirstOrDefaultAsync(x => x.Id == request.RunId, cancellationToken)
            ?? throw new KnownException($"MRP 运行不存在：{request.RunId}");
        // 常规路径由 worker 先置 Running；Created 也接受（直接调用/单测路径），在本事务内补置。
        if (run.Status is not (MrpRunStatus.Created or MrpRunStatus.Running))
        {
            throw new KnownException($"MRP 运行当前状态为 {run.Status}，不能重复执行。");
        }

        if (run.Status == MrpRunStatus.Created)
        {
            run.MarkRunning();
        }

        var snapshot = await snapshotProvider.GetSnapshotAsync(
            run.OrganizationId,
            run.EnvironmentId,
            run.HorizonStart,
            run.HorizonEnd,
            cancellationToken);
        var inputSources = snapshot.Demands
            .Select(x => x.SourceType)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var inputCoverageStart = snapshot.Demands.Count == 0 ? null : (DateOnly?)snapshot.Demands.Min(x => x.DueDate);
        var inputCoverageEnd = snapshot.Demands.Count == 0 ? null : (DateOnly?)snapshot.Demands.Max(x => x.DueDate);
        run.RecordInputSnapshot(new PlanningInputSnapshot(
            snapshot.ProductionEngineeringSnapshotSource,
            snapshot.InventorySnapshotSource,
            snapshot.Demands.Count,
            snapshot.Availability.Count,
            inputSources,
            inputCoverageStart,
            inputCoverageEnd));
        var calculated = MrpCalculator.Calculate(new MrpCalculationInput(
            run.OrganizationId,
            run.EnvironmentId,
            run.HorizonStart,
            run.HorizonEnd,
            snapshot.Demands,
            snapshot.Availability,
            snapshot.ProductionVersions,
            snapshot.BomComponents,
            snapshot.ScheduledReceipts,
            snapshot.PlanningParameters,
            snapshot.UomConversions));

        foreach (var calculatedSuggestion in calculated)
        {
            var suggestion = PlanningSuggestion.Create(
                run.OrganizationId,
                run.EnvironmentId,
                run.Id,
                calculatedSuggestion.SuggestionType,
                calculatedSuggestion.SkuCode,
                calculatedSuggestion.UomCode,
                calculatedSuggestion.SiteCode,
                calculatedSuggestion.Quantity,
                calculatedSuggestion.RequiredDate,
                calculatedSuggestion.ReleaseDate,
                calculatedSuggestion.ReasonCode);
            suggestion.SetNetRequirementExplanation(
                calculatedSuggestion.NetRequirementExplanation.GrossDemandQuantity,
                calculatedSuggestion.NetRequirementExplanation.OnHandQuantity,
                calculatedSuggestion.NetRequirementExplanation.ReservedQuantity,
                calculatedSuggestion.NetRequirementExplanation.AvailableToNetQuantity,
                calculatedSuggestion.NetRequirementExplanation.ScheduledReceiptQuantity,
                calculatedSuggestion.NetRequirementExplanation.SafetyStockQuantity,
                calculatedSuggestion.NetRequirementExplanation.NetRequirementQuantity,
                calculatedSuggestion.NetRequirementExplanation.PlannedQuantity,
                calculatedSuggestion.NetRequirementExplanation.ScrapRate,
                calculatedSuggestion.NetRequirementExplanation.YieldRate,
                calculatedSuggestion.NetRequirementExplanation.PrimarySourceType,
                calculatedSuggestion.NetRequirementExplanation.Formula,
                string.Join(';', calculatedSuggestion.NetRequirementExplanation.UomConversions));
            foreach (var link in calculatedSuggestion.PeggingLinks)
            {
                suggestion.AddPeggingLink(
                    link.PeggingType,
                    link.DemandSourceReference,
                    link.ParentSkuCode,
                    link.ComponentSkuCode,
                    link.Quantity,
                    link.ProductionVersionReference,
                    link.ManufacturingBomReference,
                    link.RoutingReference,
                    link.SourceType,
                    link.GrossDemandQuantity);
            }

            dbContext.PlanningSuggestions.Add(suggestion);
        }

        run.Complete(calculated.Count);
        return new ExecuteMrpRunCommandResult(
            run.Id,
            calculated.Count,
            run.HasInputDegradation,
            run.InputDegradationSources,
            run.InputSources,
            run.InputCoverageStart,
            run.InputCoverageEnd);
    }
}

/// <summary>
/// 把一次排队/运行中的 MRP 运行置为失败并记录原因（后台计算事务失败后的补偿事务）。
/// </summary>
public sealed record MarkMrpRunFailedCommand(MrpRunId RunId, string Reason) : ICommand;

public sealed class MarkMrpRunFailedCommandValidator : AbstractValidator<MarkMrpRunFailedCommand>
{
    public MarkMrpRunFailedCommandValidator()
    {
        RuleFor(x => x.RunId).NotNull();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class MarkMrpRunFailedCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkMrpRunFailedCommand>
{
    public async Task Handle(MarkMrpRunFailedCommand request, CancellationToken cancellationToken)
    {
        var run = await dbContext.MrpRuns.FirstOrDefaultAsync(x => x.Id == request.RunId, cancellationToken)
            ?? throw new KnownException($"MRP 运行不存在：{request.RunId}");
        run.Fail(request.Reason);
    }
}
