using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

/// <summary>
/// #3328 判定证据：网关摘掉 <c>IdempotencyKey</c> 的四个 MES 写面，**重放第二次不产生重复效果**
/// —— 而且这件事由聚合自己承担，不依赖任何幂等键。
/// </summary>
/// <remarks>
/// <para><b>为什么需要这组用例</b>：#3328 摘掉的是网关公开契约里一个下游从不消费的
/// <c>IdempotencyKey</c> 字段。摘掉它是否安全，取决于「重放第二次会发生什么」——
/// 只有当聚合自己挡住第二次（或写入本身幂等）时，那个字段才确实是空头承诺。
/// 本类把这个判定钉成断言：<b>它们红了，就说明 #3328 摘字段的前提不再成立。</b></para>
///
/// <para><b>两类不同的安全性，别混为一谈</b>：</para>
/// <list type="number">
/// <item><b>被守卫挡住</b>（下达工单、确认线边收料、接班）：第二次直接抛/早退，压根不进写入路径。</item>
/// <item><b>一次赋值、不累积</b>（确认停机恢复）：没有任何前置守卫，它对重放的抵抗力仅止于
/// 「第二次写的是同一个字段而不是追加一笔」。⚠️ **这一支不叫幂等**：真实调用点每次现铸恢复时刻，
/// 重放会把 <c>ToUtc</c> 覆盖成更晚的值（详见该用例注释）。这一类也**最脆**——
/// 写入方法一旦变成累积式，安全性立刻消失且不会有守卫兜住，
/// 所以那里断言的是「同一入参第二次施加后状态与第一次后逐字相同」，不是「第二次抛了」。</item>
/// </list>
///
/// <para><b>本类不证明什么</b>：不证明这些动作在**并发**下安全（那是乐观并发/命令锁的射程），
/// 也不证明 handler 层的第二道守卫存在——<c>ReleaseWorkOrderCommandHandler</c> 与
/// <c>ConfirmLineSideMaterialReceiptCommandHandler</c> 各自还有一道生命周期守卫，
/// 那两道由 <c>MesLifecycleConflictTests</c> 覆盖。本类只钉聚合这一层，
/// 因为它是**最后一道**、也是摘字段安全性真正依赖的那一道。</para>
/// </remarks>
public sealed class MesWriteReplaySafetyTests
{
    private static readonly DateTimeOffset Anchor = DateTimeOffset.Parse("2026-05-23T08:00:00Z");

    /// <summary>下达工单：第二次被 <c>ThrowIfCannotRelease()</c> 的 released 分支挡住。</summary>
    /// <remarks>
    /// 断言到**消息**而不只是异常类型：<c>ThrowIfCannotRelease</c> 里另有一条 closed-state 分支
    /// 抛同一个 <see cref="InvalidOperationException"/>，方法开头还有 routingSteps 的
    /// <see cref="ArgumentException"/>。只断言类型的话，相邻同型守卫会把「删掉 released 分支」
    /// 这格变异兜住（本仓判例：相邻同型守卫会兜住变异）。
    /// </remarks>
    [Fact]
    public void Releasing_an_already_released_work_order_is_rejected_by_the_aggregate()
    {
        var workOrder = NewWorkOrder();
        RoutingStepSnapshot[] routingSteps = [new("OP-10", 10, "WC-A", [], TimeSpan.FromMinutes(30))];

        var firstTasks = workOrder.Release(Anchor, WorkOrderReleaseFactTime.NotLaterThan(Anchor, null), routingSteps);
        Assert.Single(firstTasks);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workOrder.Release(Anchor, WorkOrderReleaseFactTime.NotLaterThan(Anchor, null), routingSteps));

        Assert.Equal("Work order has already been released.", exception.Message);
        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
    }

    /// <summary>
    /// 确认线边收料：第一次把状态推进「过账中」并占住 <c>PendingPostingToken</c>，
    /// 第二次被那道「上一次收料过账尚未回执」的守卫挡住。
    /// </summary>
    /// <remarks>
    /// <para>夹具只触犯这一条：第二次调用时 <c>Status</c> 不是 <c>ReservationExpired</c>、
    /// 没有任何腿过账成功（<c>hasSettledLeg</c> 为 false）、数量取 <c>Requested − Received</c>
    /// 因而不会撞「超过申请数量」那条，批次也一致。删掉目标守卫后第二次会**成功走完**，
    /// 所以这格变异不会被相邻守卫兜住。</para>
    /// <para>⚠️ 这里钉的是**重放窗口**：从确认到两条库存腿回执之间，第二次一律被挡。
    /// 回执落地、状态回到 <c>PartiallyReceived</c> 之后还能再收一次——那不是重放，
    /// 是一次新的部分收料，语义上就该放行。</para>
    /// </remarks>
    [Fact]
    public void Confirming_line_side_receipt_twice_within_the_posting_window_is_rejected()
    {
        var request = MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-000123", "WO-002", "OP-10", "MAT-OIL", "L", 10m, Anchor);

        request.ConfirmLineSideReceipt(MaterialSupplyTestFixtures.Locations, Anchor);
        var pendingAfterFirst = request.PendingReceiptQuantity;
        var attemptAfterFirst = request.ReceiptAttempt;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            request.ConfirmLineSideReceipt(MaterialSupplyTestFixtures.Locations, Anchor));

        Assert.Equal("上一次收料过账尚未回执，不能重复提交收料。", exception.Message);
        Assert.Equal(pendingAfterFirst, request.PendingReceiptQuantity);
        Assert.Equal(attemptAfterFirst, request.ReceiptAttempt);
        Assert.Equal(0m, request.ReceivedQuantity);
    }

    /// <summary>接班：第二次撞 <c>Accept</c> 首句的幂等早退，状态与接班人一字不改。</summary>
    /// <remarks>
    /// 第二次故意换一个**更晚的时刻和另一个人**：早退成立时这两个新值都不该落进聚合。
    /// 若用同样的入参重放，删掉早退后剩下的赋值会写出完全相同的状态，这格变异就杀不掉。
    /// 删掉早退后第二次会撞下一条 <c>HandoverStatus != OpenStatus</c> 守卫抛异常，
    /// 而本用例断言的是「不抛且状态不变」，两个方向都红。
    /// </remarks>
    [Fact]
    public void Accepting_an_already_accepted_shift_handover_changes_nothing()
    {
        var handover = ShiftHandover.Create(
            "org-001", "env-dev", "SH-000012", "SHIFT-A", "TEAM-1", 0, Anchor);

        handover.Accept(Anchor.AddMinutes(5), "user-a", "张三");
        handover.Accept(Anchor.AddMinutes(30), "user-b", "李四");

        Assert.Equal(ShiftHandover.AcceptedStatus, handover.HandoverStatus);
        Assert.Equal(Anchor.AddMinutes(5), handover.AcceptedAtUtc);
        Assert.Equal("user-a", handover.IncomingUserId);
        Assert.Equal("张三", handover.IncomingUserName);
    }

    /// <summary>
    /// 确认停机恢复：<c>Close</c> **没有任何前置守卫**，它对重放的全部抵抗力就是「一次赋值、不累积」。
    /// </summary>
    /// <remarks>
    /// <para><b>本用例证的是「不累积」，不是「幂等」——两者不是一回事，别读混。</b>
    /// 这里两次传同一个时刻，断言第二次写完后的 <c>ToUtc</c> 与第一次逐字相同：
    /// <c>Close</c> 一旦变成累积式（哪怕只在第二次分支上偏移），本条立刻红。
    /// 换句话说，它钉住的是「重放不产生第二笔效果」，而不是「重放没有任何影响」。</para>
    ///
    /// <para>⚠️ <b>真实调用点送来的并不是同一份入参</b>（这一条我起初写错过，登记在此避免被继承）：
    /// 控制台 <c>pages/mes/downtime.vue</c> 的 <c>confirmRecover()</c> 在**函数体内**现铸
    /// <c>new Date().toISOString()</c>，所以一次真实重放带的是**更晚的** <c>RecoveredAtUtc</c>；
    /// 网关侧 <c>RecoveredAtUtc</c> 也**不是必填**（OpenAPI 该 schema 根本没有 <c>required</c> 列表）。
    /// ⇒ 真实重放的后果是：<b>不产生重复行</b>（本用例证的那一半成立），
    /// <b>但会把恢复时刻覆盖成更晚的值</b>。所以 #3328 摘掉网关那个幂等键是安全的
    /// （它本来也拦不住这件事，下游从不消费它），但**不要**因此说这条腿「幂等」。</para>
    ///
    /// <para>⚠️ 「零前置守卫」本身是另一个缺陷（可以「恢复」一个已恢复的停机，也可写出早于
    /// <c>FromUtc</c> 的 <c>ToUtc</c>，还包括上一段那个覆盖）。#3328 明确**不修**它，编排者另行立票；
    /// 本用例只钉「不累积」这一条，不要把它读成「Close 已经被守住了」。</para>
    /// </remarks>
    [Fact]
    public void Closing_a_downtime_twice_with_the_same_instant_does_not_accumulate()
    {
        var downtime = WorkCenterUnavailability.Open(
            "org-001", "env-dev", "DTE-000007", "WC-A", Anchor, null, "设备待修", "DEV-CNC-01");
        var recoveredAtUtc = Anchor.AddHours(2);

        downtime.Close(recoveredAtUtc);
        var afterFirst = downtime.ToUtc;
        downtime.Close(recoveredAtUtc);

        Assert.Equal(recoveredAtUtc, afterFirst);
        Assert.Equal(afterFirst, downtime.ToUtc);
    }

    private static WorkOrder NewWorkOrder() => WorkOrder.Create(
        "org-001", "env-dev", "WO-002", "SKU-001", "PV-001", 5m, 10, Anchor.AddHours(2));
}
