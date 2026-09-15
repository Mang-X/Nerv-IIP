using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3328 判定证据：指派派工任务重放**不产生重复效果**，因为它是**覆盖语义**而不是追加语义。
/// </summary>
/// <remarks>
/// <para><b>为什么这一处要单独钉</b>：#3328 的另外几处靠守卫或早退挡住第二次，
/// 而指派派工**没有任何重放守卫**——第二次会完整跑一遍。它之所以仍然安全，
/// 是因为 handler 在写参与者前先 <c>RemoveRange</c> 掉这条工序任务已有的参与者，
/// 再整体 <c>AddRange</c>：第二次得到的是同一份集合，不是两份叠加。
/// 一旦有人把那句 <c>RemoveRange</c> 去掉或改成条件执行，重放就会**真的**造出重复参与者行，
/// 摘掉网关幂等键的前提随之失效——本用例就是那道红线。</para>
///
/// <para><b>本用例不声称什么</b>：重放并非「完全无痕」。<c>AssignedAtUtc</c> 由下游取当前时刻，
/// <c>OperationTask.Assign</c> 在带设备的手动派工上还会自增 <c>ManualDispatchRevision</c>
/// 并再发一条派工域事件。这些**漂移是有的**，只是不构成重复业务效果：
/// 下游 Scheduling 的 <c>MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride</c>
/// 按 (组织, 环境, 工序任务) upsert 同一行覆盖事实，不新增行。
/// 本用例只覆盖 MES 这一侧的参与者集合，**不**覆盖 Scheduling 那一跳。</para>
/// </remarks>
public sealed class MesDispatchAssignmentReplaySafetyTests
{
    private static readonly DateTimeOffset Anchor = DateTimeOffset.Parse("2026-07-05T10:00:00Z");

    [Fact]
    public async Task Replaying_the_same_assignment_does_not_accumulate_participants()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-REPLAY-ASSIGN", "FG-FSA", null, 10m, 20, Anchor.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001", "env-dev", "WO-REPLAY-ASSIGN", "OP-REPLAY-ASSIGN-10",
            OperationTaskLifecycleStatus.Queued, 10, "WC-FILL", [], Anchor,
            TimeSpan.FromMinutes(45), null, null, "SKU-001"));
        await dbContext.SaveChangesAsync();

        var command = new AssignDispatchTaskCommand(
            "org-001",
            "env-dev",
            "OP-REPLAY-ASSIGN-10",
            "operator-001",
            null,
            "SHIFT-A",
            Anchor.AddMinutes(15),
            Participants:
            [
                new DispatchParticipantInput("operator-001", "张三", 60m),
                new DispatchParticipantInput("operator-002", "李四", 40m),
            ]);

        await new AssignDispatchTaskCommandHandler(dbContext).Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var afterFirst = await ParticipantsAsync(dbContext);

        await new AssignDispatchTaskCommandHandler(dbContext).Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var afterSecond = await ParticipantsAsync(dbContext);

        Assert.Equal(2, afterFirst.Length);
        Assert.Equal(afterFirst, afterSecond);
    }

    private static async Task<string[]> ParticipantsAsync(ApplicationDbContext dbContext) =>
        [.. (await dbContext.OperationTaskParticipants
                .Where(x => x.OrganizationId == "org-001"
                    && x.EnvironmentId == "env-dev"
                    && x.OperationTaskId == "OP-REPLAY-ASSIGN-10")
                .ToArrayAsync())
            .Select(x => $"{x.WorkerId}|{x.WorkerName}|{x.SharePercent}")
            .Order(StringComparer.Ordinal)];
}
