using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests;

/// <summary>
/// #3328 判定证据：确认报警与解除搁置**早退即幂等**，重放第二次不改状态、不发第二条域事件。
/// </summary>
/// <remarks>
/// <para><b>这两条断言承载的是一个「不做什么」的决定</b>：#3328 保留了网关侧
/// <c>BusinessConsoleAcknowledgeAlarmRequest</c> / <c>BusinessConsoleUnshelveAlarmRequest</c> 的
/// <c>IdempotencyKey</c>（它的消费者是网关自己的 operation-receipt），但**没有**把它接进下游命令、
/// 也**没有**为这两条腿建去重表。理由就是本类钉住的性质：聚合自己已经幂等，再加一层去重是过度防御。</para>
///
/// <para><b>同族对照说明这不是偷懒</b>：同一个聚合上的 <c>Shelve</c> 就**有**下游幂等键，
/// 且 <c>ShelveAlarmCommandHandler</c> 为它建了 <c>AlarmShelveIdempotencies</c> 去重表 + payload 指纹。
/// 原因是搁置**不是**自然幂等——「搁置 A → 解除 → 延迟到达的重复 A」会把窗口再开一次，
/// 早退挡不住，必须靠持久化的键去重。确认与解除搁置没有这个形态。</para>
///
/// <para><b>本类红了意味着什么</b>：意味着「重放无副作用」不再成立，
/// 于是 #3328「不给这两条腿建下游去重」的裁定失去前提，
/// 同时 <c>BusinessGatewayIdempotencyKeyDownstreamBoundContractTests</c> 里那两条
/// 「下游零权威」登记的理由也需要重新审。</para>
/// </remarks>
public sealed class AlarmLifecycleReplaySafetyTests
{
    private static readonly DateTimeOffset RaisedAtUtc = new(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 确认报警：第二次撞 <c>Acknowledge</c> 里 <c>AcknowledgedAtUtc is not null</c> 的早退。
    /// </summary>
    /// <remarks>
    /// 第二次故意换**另一个时刻和另一个人**：早退成立时这两个新值都不该落进聚合。
    /// 若两次传同样的入参，删掉早退后剩下的赋值会写出完全相同的状态，这格变异就杀不掉
    /// （等价输入不产生鉴别力）。域事件计数是第二道鉴别力：早退同时也拦住了第二条
    /// <see cref="AlarmAcknowledgedDomainEvent"/>。
    /// </remarks>
    [Fact]
    public void Acknowledging_an_already_acknowledged_alarm_changes_nothing()
    {
        var alarm = AlarmEvent.Raise(
            "org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", RaisedAtUtc, "alarm-ext-001");

        alarm.Acknowledge(RaisedAtUtc.AddMinutes(5), "operator-001");
        alarm.Acknowledge(RaisedAtUtc.AddMinutes(9), "operator-002");

        Assert.Equal("acknowledged", alarm.Status);
        Assert.Equal(RaisedAtUtc.AddMinutes(5), alarm.AcknowledgedAtUtc);
        Assert.Equal("operator-001", alarm.AcknowledgedBy);
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmAcknowledgedDomainEvent>());
    }

    /// <summary>
    /// 解除搁置：第二次撞 <c>Unshelve</c> 里 <c>Status != "shelved"</c> 的早退并返回 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 夹具先确认再搁置，于是第一次解除会把状态落回 <c>acknowledged</c>。
    /// 这样第二次调用时状态**不是** <c>shelved</c>，只触犯目标那一条早退；
    /// 而且删掉早退后第二次会把 <c>Status</c> 重新写一遍并补发第二条
    /// <see cref="AlarmUnshelvedDomainEvent"/>，返回值也从 <c>false</c> 变 <c>true</c>——三处都红。
    /// </remarks>
    [Fact]
    public void Unshelving_an_already_unshelved_alarm_changes_nothing()
    {
        var alarm = AlarmEvent.Raise(
            "org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", RaisedAtUtc, "alarm-ext-001");
        alarm.Acknowledge(RaisedAtUtc.AddMinutes(2), "operator-001");
        alarm.Shelve(RaisedAtUtc.AddMinutes(3), RaisedAtUtc.AddMinutes(33), "operator-001", "maintenance window");

        Assert.True(alarm.Unshelve(RaisedAtUtc.AddMinutes(10)));
        Assert.False(alarm.Unshelve(RaisedAtUtc.AddMinutes(12)));

        Assert.Equal("acknowledged", alarm.Status);
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmUnshelvedDomainEvent>());
    }
}
