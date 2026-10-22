using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Andon;

namespace Nerv.IIP.Business.Mes.Web.Application.Andon;

public sealed class AndonEscalationScanner(
    IServiceScopeFactory scopeFactory,
    IOptions<AndonEscalationOptions> options,
    TimeProvider clock,
    ILogger<AndonEscalationScanner> logger)
{
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var escalated = 0;
        foreach (var policy in options.Value.Policies)
        {
            List<AndonCallId> candidates;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deadline = now - policy.UnclaimedTimeout;
                candidates = await db.AndonCalls.AsNoTracking()
                    .Where(x => x.OrganizationId == policy.OrganizationId && x.EnvironmentId == policy.EnvironmentId
                        && x.Category == policy.Category && x.Status == AndonCallStatus.Open
                        && x.EscalatedAtUtc == null && x.RaisedAtUtc <= deadline)
                    .OrderBy(x => x.RaisedAtUtc).ThenBy(x => x.Id)
                    .Select(x => x.Id).ToListAsync(ct);
            }
            foreach (var id in candidates)
            {
                // 每个候选独立 UoW；不能用扫描时的实体覆盖其后已提交的认领/关闭。
                await using var scope = scopeFactory.CreateAsyncScope();
                try
                {
                    if (await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                            new EscalateAndonCallCommand(id, now, policy.UnclaimedTimeout, policy.RecipientId), ct)) escalated++;
                }
                catch (DbUpdateConcurrencyException exception) when (AndonCallRepository.IsWriteConflict(exception))
                {
                    // 另一次升级或认领/关闭先提交，当前 UoW 已回滚；无需重试同一业务意图。
                    logger.LogInformation("安灯呼叫 {CallId} 已由并发操作更新，本次升级未提交。", id);
                }
            }
        }
        return escalated;
    }
}

public sealed class AndonEscalationWorker(
    AndonEscalationScanner scanner,
    IOptions<AndonEscalationOptions> options,
    TimeProvider clock,
    ILogger<AndonEscalationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Policies.Count == 0)
        {
            logger.LogWarning("Mes:AndonEscalation:Policies 未配置，安灯超时升级未启用。");
            return;
        }
        logger.LogInformation("安灯超时升级已启用：{PolicyCount} 个组织/环境/类别策略，扫描间隔 {ScanInterval}。",
            options.Value.Policies.Count, options.Value.ScanInterval);
        using var timer = new PeriodicTimer(options.Value.ScanInterval, clock);
        await scanner.ScanAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await scanner.ScanAsync(stoppingToken);
    }
}
