using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class ScheduleInsertionPreviewJobQueue
{
    private readonly Channel<ScheduleInsertionPreviewJobId> channel = Channel.CreateUnbounded<ScheduleInsertionPreviewJobId>(
        new UnboundedChannelOptions { SingleReader = true });
    public void Enqueue(ScheduleInsertionPreviewJobId id) => channel.Writer.TryWrite(id);
    public IAsyncEnumerable<ScheduleInsertionPreviewJobId> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}

public sealed class ScheduleInsertionPreviewJobWorker(ScheduleInsertionPreviewJobQueue queue, IServiceScopeFactory scopes,
    ILogger<ScheduleInsertionPreviewJobWorker> logger) : BackgroundService
{
    public const string InterruptedFailureReason = "插单预览因服务停止被中断，请重新计算。";
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
                        .Send(new StartScheduleInsertionPreviewJobCommand(id), stoppingToken)) continue;
                }
                await using var calculation = scopes.CreateAsyncScope();
                await calculation.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ExecuteScheduleInsertionPreviewJobCommand(id), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Insertion-preview job {JobId} failed.", id);
                await FailAsync(id, exception is KnownException known ? known.Message : "插单预览计算失败，请重新计算或联系管理员。", stoppingToken);
            }
        }
    }
    private async Task RecoverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await db.ScheduleInsertionPreviewJobs.AsNoTracking()
            .Where(x => x.Status == ScheduleInsertionPreviewJobStatus.Created || x.Status == ScheduleInsertionPreviewJobStatus.Running)
            .OrderBy(x => x.CreatedAtUtc).Select(x => new { x.Id, x.Status }).ToArrayAsync(ct);
        foreach (var job in pending)
        {
            if (job.Status == ScheduleInsertionPreviewJobStatus.Running) await FailAsync(job.Id, InterruptedFailureReason, ct);
            else queue.Enqueue(job.Id);
        }
    }
    private async Task FailAsync(ScheduleInsertionPreviewJobId id, string reason, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new FailScheduleInsertionPreviewJobCommand(id, reason), ct);
    }
}
