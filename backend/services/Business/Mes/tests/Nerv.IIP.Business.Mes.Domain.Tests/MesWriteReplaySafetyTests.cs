using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using NetCorePal.Extensions.Primitives;

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
/// <item><b>一次赋值、不累积</b>（确认停机恢复）：⚠️ <b>#3343 之后本类已空</b>。
/// 这一类原本只有「确认停机恢复」一员：<c>Close</c> 曾经零前置守卫，对重放的抵抗力仅止于
/// 「第二次写的是同一个字段而不是追加一笔」——不产生重复行，**但会把恢复时刻覆盖成更晚的值**，
/// 所以它从来不叫幂等。#3343 给 <c>Close</c> 补上「已有结束时刻则拒绝」的守卫后，
/// 它已并入第 1 类（被守卫挡住）。<b>保留这一条描述是因为它解释了本类用例为什么那样写</b>，
/// 不要读成「现在还有成员落在这一类」。</item>
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
    /// 确认停机恢复：第二次撞 <c>Close</c> 的「已有结束时刻」守卫被拒，恢复时刻保持首次值。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>本用例的前提在 #3343 被改掉了，它的分类随之从第 ②' 类挪进第 ① 类——
    /// 这是改写不是新增，别当成「旧断言过时可删」。</b>上一版（#3328）写的是
    /// 「<c>Close</c> 零前置守卫，对重放的全部抵抗力就是一次赋值、不累积」，
    /// 断言两次传<b>同一个</b>时刻后 <c>ToUtc</c> 逐字相同。#3343 给 <c>Close</c> 补了
    /// 「已有结束时刻则拒绝」的守卫之后，那个写法**证不到自己的名字**了：
    /// 同一个时刻的第二次调用现在会抛，走不到「是否累积」那一步。</para>
    ///
    /// <para><b>所以这一版换成更强的形态，并且保留原用例真正要证的那件事</b>——
    /// 「重放不产生第二笔效果」。这里第二次故意传一个<b>更晚</b>的时刻（<c>+3h</c>，正是真实调用点
    /// 会送来的形状，见下段），断言：<c>KnownException</c> 抛出，且 <c>ToUtc</c> 仍是**首次**那个值。
    /// 后半句同时钉住「守卫排在赋值之前」——把 <c>Close</c> 改成先赋值再 <c>throw</c>，
    /// 异常断言照样绿，本条的第二个断言会红。</para>
    ///
    /// <para><b>真实调用点送来的不是同一份入参</b>（#3328 登记、#3343 沿用）：
    /// 控制台 <c>pages/mes/downtime.vue</c> 的 <c>confirmRecover()</c> 在**函数体内**现铸
    /// <c>new Date().toISOString()</c>，所以一次真实重放带的是**更晚的** <c>RecoveredAtUtc</c>；
    /// 网关侧 <c>RecoveredAtUtc</c> 也**不是必填**。在 #3343 之前，这意味着重复点击「恢复」
    /// 会把恢复时刻静默改写成最后一次点击的时间；现在第二次被拒，现场事实保持首次那一次。</para>
    ///
    /// <para><b>本用例不证明什么</b>：不证明 <c>Close</c> 的另一条守卫（拒绝早于 <c>FromUtc</c>
    /// 的恢复时刻）——这里第二次的时刻晚于 <c>FromUtc</c>，那条守卫在本夹具上**不可达**，
    /// 刻意如此，免得两条同型守卫互相兜住变异。那条由
    /// <c>MesAggregateTests.Closing_a_downtime_earlier_than_its_start_is_rejected</c> 单独覆盖。</para>
    /// </remarks>
    [Fact]
    public void Closing_an_already_recovered_downtime_is_rejected_and_keeps_the_first_instant()
    {
        var downtime = WorkCenterUnavailability.Open(
            "org-001", "env-dev", "DTE-000007", "WC-A", Anchor, null, "设备待修", "DEV-CNC-01");
        var firstRecoveredAtUtc = Anchor.AddHours(2);

        downtime.Close(firstRecoveredAtUtc);

        var exception = Assert.Throws<KnownException>(() => downtime.Close(Anchor.AddHours(3)));
        Assert.Equal("该停机事件已恢复，不能重复恢复。", exception.Message);
        Assert.Equal(firstRecoveredAtUtc, downtime.ToUtc);
    }

    private static WorkOrder NewWorkOrder() => WorkOrder.Create(
        "org-001", "env-dev", "WO-002", "SKU-001", "PV-001", 5m, 10, Anchor.AddHours(2));
}
