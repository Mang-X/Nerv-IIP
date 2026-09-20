using System.Reflection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

/// <summary>
/// #3476：交接班「未完工单」的 <c>workOrderStatus</c> 一列原先只有 <c>RequiredBounded(30)</c>——
/// 只管长度、不管取值，而它由公开写端点 <c>POST /api/business-console/v1/mes/shift-handovers</c> 接收，
/// 任何持权限的客户端都能打，前端窄化约束不了端点。失效方向是静默的：后端不拒、PDA 显示「未知状态」、
/// PC 按自己更宽的 label 表解出中文，两屏并排才看得出是两种读数。
///
/// 权威值域取自 <see cref="WorkOrder.UnfinishedStatuses"/>（工单状态全集减终态），
/// 不在 ShiftHandover 里手抄字面量。
/// </summary>
public sealed class ShiftHandoverUnfinishedWorkOrderStatusTests
{
    private static readonly DateTimeOffset HandoverAtUtc = DateTimeOffset.Parse("2026-09-19T08:00:00Z");

    /// <summary>
    /// 值域的完备性锚：全集是 <see cref="WorkOrder.AllStatuses"/>，终态与未完态是它的一个划分。
    /// 反射兜住「新增了 <c>*Status</c> 常量却没登记进全集」——那正是两边静默分叉的入口。
    /// </summary>
    [Fact]
    public void Work_order_status_sets_partition_every_declared_status_constant()
    {
        var declared = typeof(WorkOrder)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Where(field => field.Name.EndsWith("Status", StringComparison.Ordinal))
            // 物料需求快照状态是工单上的另一个维度（captured / no-requirements），不是工单生命周期状态。
            .Where(field => !field.Name.StartsWith("MaterialRequirementSnapshot", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared, WorkOrder.AllStatuses.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal([], WorkOrder.TerminalStatuses.Intersect(WorkOrder.UnfinishedStatuses, StringComparer.Ordinal));
        Assert.Equal(
            declared,
            WorkOrder.TerminalStatuses
                .Concat(WorkOrder.UnfinishedStatuses)
                .OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["created", "hold", "released", "started"], WorkOrder.UnfinishedStatuses.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("created")]
    [InlineData("released")]
    [InlineData("started")]
    [InlineData("hold")]
    public void Unfinished_work_order_accepts_every_non_terminal_work_order_status(string status)
    {
        var handover = CreateHandoverWith(status);

        Assert.Equal(status, Assert.Single(handover.UnfinishedWorkOrders).WorkOrderStatus);
    }

    /// <summary>
    /// 六个终态逐个拒。终态出现在「**未完**工单」清单里本身就是矛盾的数据。
    /// </summary>
    [Theory]
    [InlineData("completed")]
    [InlineData("closed")]
    [InlineData("cancelled")]
    [InlineData("scrapped")]
    [InlineData("split")]
    [InlineData("merged")]
    public void Unfinished_work_order_rejects_every_terminal_work_order_status(string status)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateHandoverWith(status));

        Assert.Contains(status, exception.Message, StringComparison.Ordinal);
        Assert.Contains("不是工单的未完状态", exception.Message, StringComparison.Ordinal);
        Assert.Contains("created、released、started、hold", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>inProgress</c> 是票面点名的真实越界码（MES OpenApi 处理器、MES 测试、console e2e 里都出现过），
    /// 它在 <see cref="WorkOrder"/> 的状态常量里根本不存在：PDA 会显示「未知状态」、PC 会解成「执行中」。
    /// </summary>
    [Theory]
    [InlineData("inProgress")]
    [InlineData("in-progress")]
    [InlineData("")]
    [InlineData("   ")]
    public void Unfinished_work_order_rejects_codes_outside_the_work_order_status_domain(string status)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateHandoverWith(status));
    }

    /// <summary>
    /// 大小写按 <c>WorkOrder</c> 自己的归一口径先降小写：落库的始终是小写码，读面的 label 表才对得上。
    /// </summary>
    [Fact]
    public void Unfinished_work_order_status_is_stored_lower_cased()
    {
        var handover = CreateHandoverWith("  Started  ");

        Assert.Equal("started", Assert.Single(handover.UnfinishedWorkOrders).WorkOrderStatus);
    }

    /// <summary>
    /// 拒绝消息必须**能活着到达操作工的屏幕**。
    ///
    /// <para>上面那些断言读的是异常消息本身，它们全绿也证不到屏上对：这条消息要先被
    /// <c>MesDomainRuleGuard.Enforce</c> 包成 <c>KnownException</c>，再经 BusinessGateway 转发，
    /// 而网关的 <c>BusinessServiceProxyException.IsSafeDownstreamBusinessMessage</c> 会把
    /// 含 <c>&lt; &gt; { } / \</c> 或控制字符、或超 500 字符、或首字符是空白的消息**整条**换成
    /// <c>downstream-request-failed</c>。真栈实测：候选值原先用「 / 」分隔，后端确实返回 400，
    /// 但客户端拿到的是 <c>downstream-request-failed</c>，拒绝理由在屏上完全消失。</para>
    ///
    /// <para><b>口径来源声明</b>：下面这份字符集与长度上界是**照抄**网关那个私有判据的（跨程序集，
    /// 这里引用不到它）。它会随网关改动而漂移，不是推导得来的——这一点必须明写，
    /// 不要把它当成「与网关同源」。</para>
    /// </summary>
    [Fact]
    public void Rejection_message_survives_the_gateway_business_message_filter()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateHandoverWith("completed"));
        var message = exception.Message;

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.False(char.IsWhiteSpace(message[0]));
        Assert.True(message.Length <= 500, $"消息长度 {message.Length} 超过网关上界 500。");
        var unsafeCharacters = message
            .Where(character => char.IsControl(character) || character is '<' or '>' or '{' or '}' or '/' or '\\')
            .Distinct()
            .ToArray();
        Assert.Equal([], unsafeCharacters);
    }

    private static ShiftHandover CreateHandoverWith(string workOrderStatus) =>
        ShiftHandover.Create(
            "org-001",
            "env-dev",
            "SH-000001",
            "EARLY",
            "TEAM-WB-AS-A",
            0,
            HandoverAtUtc,
            "装配车间早班组",
            "user-out",
            "张三",
            unfinishedWorkOrders: [new ShiftHandoverUnfinishedWorkOrderSnapshot("WO-0001", 100m, 40m, workOrderStatus)]);
}
