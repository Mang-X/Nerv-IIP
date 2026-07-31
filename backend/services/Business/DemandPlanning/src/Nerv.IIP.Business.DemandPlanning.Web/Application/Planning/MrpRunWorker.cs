using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

/// <summary>
/// MRP 后台执行队列（#1306 异步任务模式）。受理端点在受理事务提交后入队，
/// <see cref="MrpRunWorker"/> 逐条消费。进程内单实例即可：run 记录本身是权威状态，
/// 服务重启后由 worker 启动恢复扫描接管遗留的排队/运行中记录。
/// </summary>
public interface IMrpRunExecutionQueue
{
    void Enqueue(MrpRunId runId);

    IAsyncEnumerable<MrpRunId> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed class MrpRunExecutionQueue : IMrpRunExecutionQueue
{
    private readonly Channel<MrpRunId> channel = Channel.CreateUnbounded<MrpRunId>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(MrpRunId runId)
    {
        // Unbounded channel：TryWrite 只在 channel 完成后失败，进程存续期内不会发生。
        channel.Writer.TryWrite(runId);
    }

    public IAsyncEnumerable<MrpRunId> DequeueAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// MRP 运行后台执行器：受理事务与计算事务分离（netcorepal UoW 约定）。
/// 每条 run 在独立 scope 内经 MediatR 管道执行 <see cref="ExecuteMrpRunCommand"/>（UoW 行为自动提交、
/// 派发领域事件）；计算失败时另起 scope 用 <see cref="MarkMrpRunFailedCommand"/> 置失败态并记录原因，
/// 保证用户在运行列表里看到真实终态而不是永远运行中。
/// </summary>
public sealed class MrpRunWorker(
    IMrpRunExecutionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<MrpRunWorker> logger) : BackgroundService
{
    /// <summary>服务重启导致计算中断时写入的失败原因。</summary>
    public const string InterruptedFailureReason = "MRP 计算因服务重启被中断，请重新运行。";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverPendingRunsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            // 恢复扫描失败不拦截新任务消费（例如库尚未迁移完成），只记录。
            logger.LogError(exception, "MRP run startup recovery scan failed.");
        }

        await foreach (var runId in queue.DequeueAllAsync(stoppingToken))
        {
            await ExecuteRunAsync(runId, stoppingToken);
        }
    }

    private async Task RecoverPendingRunsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await dbContext.MrpRuns.AsNoTracking()
            .Where(x => x.Status == MrpRunStatus.Created || x.Status == MrpRunStatus.Running)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.Status })
            .ToArrayAsync(cancellationToken);
        foreach (var run in pending)
        {
            if (run.Status == MrpRunStatus.Running)
            {
                // 上次进程在计算事务提交前退出：计算写入已随事务回滚，只剩 Running 头记录。
                await MarkRunFailedAsync(run.Id, InterruptedFailureReason, cancellationToken);
            }
            else
            {
                queue.Enqueue(run.Id);
            }
        }
    }

    private async Task ExecuteRunAsync(MrpRunId runId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new ExecuteMrpRunCommand(runId), cancellationToken);
            logger.LogInformation(
                "MRP run {RunId} completed with {SuggestionCount} suggestions.",
                runId,
                result.SuggestionCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停机取消：不写失败态，交给下次启动的恢复扫描判定。
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MRP run {RunId} execution failed.", runId);
            var reason = exception is KnownException known
                ? known.Message
                : $"MRP 计算失败：{exception.Message}";
            await MarkRunFailedAsync(runId, reason, cancellationToken);
        }
    }

    private async Task MarkRunFailedAsync(MrpRunId runId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new MarkMrpRunFailedCommand(runId, reason), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 置失败态本身失败也不能拖垮 worker；run 留在原状态，由下次启动恢复扫描兜底。
            logger.LogError(exception, "Failed to mark MRP run {RunId} as failed.", runId);
        }
    }
}
