using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class ScheduleFirstPlanJobQueue
{
    private readonly Channel<ScheduleFirstPlanJobId> channel = Channel.CreateUnbounded<ScheduleFirstPlanJobId>(
        new UnboundedChannelOptions { SingleReader = true });
    public void Enqueue(ScheduleFirstPlanJobId id) => channel.Writer.TryWrite(id);
    public IAsyncEnumerable<ScheduleFirstPlanJobId> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}

public sealed class ScheduleFirstPlanJobWorker(ScheduleFirstPlanJobQueue queue, IServiceScopeFactory scopes,
    ILogger<ScheduleFirstPlanJobWorker> logger) : BackgroundService
{
    public const string InterruptedFailureReason = "首版排程计算因服务停止被中断，请重新生成。";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    if (!await scope.ServiceProvider.GetRequiredService<ISender>()
                        .Send(new StartScheduleFirstPlanJobCommand(id), stoppingToken)) continue;
                }
                await using var calculation = scopes.CreateAsyncScope();
                await calculation.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ExecuteScheduleFirstPlanJobCommand(id), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "First-plan job {JobId} failed.", id);
                await FailAsync(id, exception is KnownException known ? known.Message : "首版排程计算失败，请重新生成或联系管理员。", stoppingToken);
            }
        }
    }
    private async Task RecoverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await db.ScheduleFirstPlanJobs.AsNoTracking()
            .Where(x => x.Status == ScheduleFirstPlanJobStatus.Created || x.Status == ScheduleFirstPlanJobStatus.Running)
            .OrderBy(x => x.CreatedAtUtc).Select(x => new { x.Id, x.Status }).ToArrayAsync(ct);
        foreach (var job in pending)
        {
            if (job.Status == ScheduleFirstPlanJobStatus.Running) await FailAsync(job.Id, InterruptedFailureReason, ct);
            else queue.Enqueue(job.Id);
        }
    }
    private async Task FailAsync(ScheduleFirstPlanJobId id, string reason, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new FailScheduleFirstPlanJobCommand(id, reason), ct);
    }
}
