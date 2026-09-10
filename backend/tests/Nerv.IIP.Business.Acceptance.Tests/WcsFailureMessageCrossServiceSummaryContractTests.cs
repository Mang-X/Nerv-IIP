using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NotificationDbContext = Nerv.IIP.Notification.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #3305：WCS 失败回调的诊断报文跨服务流出面的宽度契约。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：WMS 侧 <c>wcs_tasks.failure_message</c> 是**无界** <c>text</c>
/// （外部 WCS 回传的原始报文不该有人为上界），而它经
/// <c>wms.WcsTaskRetryExhausted</c> 流到 Notification 后落进有界的 <c>summary</c> 列；
/// 因此**最坏取值下 Notification 拼出来的摘要必须装得进它自己的列**，
/// 且这个上界从两侧 EF 模型现读，两边都不手抄。</para>
///
/// <para>这个类是本仓唯一能把这句话写成断言的地方：本测试项目同时引用 Wms.Web 与 Notification.Web，
/// 因而能在同一个进程里读到**两个服务各自的 EF 模型**。
/// 单服务的契约用例只能证明「我这一侧没被改」。</para>
///
/// <para><b>最坏取值怎么来的</b>：摘要模板里除诊断报文外还有两段变量——
/// <c>PublicReference</c>（取值是 <c>wcs_tasks.external_task_id</c>）与
/// <c>DiagnosticCode</c>（取值是 <c>wcs_tasks.failure_code</c>）。
/// 它们各自的结构上界就是那两列的列宽，从 WMS 模型现读、不写死。
/// 诊断报文那一段没有结构上界（列已无界），所以用一个远超任何列宽的取值代表「任意长」。</para>
///
/// <para><b>为什么调 <c>BuildSummary</c> 而不在这里重拼模板</b>：两边各写一份模板就是
/// 「照抄 canonical」——模板改一处、本类还对着旧的那份绿。
/// 非同义反复由 <see cref="Worst_case_summary_fits_the_notification_column"/> 里
/// 「不截断时确实超界」那一格保证。</para>
///
/// <para><b>合同分类</b>（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是两侧的 EntityConfiguration / migration 列宽约束；
/// <c>Regression</c> 的权威来源是 GitHub #3305。</para>
///
/// <para><b>值域边界（声明放弃了什么，别读成完备）</b>：</para>
/// <list type="bullet">
/// <item>列宽读的是 <b>EF 模型</b>而不是迁移脚本。模型与迁移单边漂移不由本类抓——
/// 它由 EF 自己在 <c>MigrateAsync</c> 处抛 pending-model-changes 暴露，因而只在跑真库迁移的用例上显形
/// （本 PR 的 M3 变异格实测：把 <c>HasMaxLength(1000)</c> 加回去后 10 条 Postgres 用例因此转红）。
/// <b>本仓没有独立的 pending-model-changes 门禁脚本</b>——<c>HasPendingModelChanges</c> 全仓零调用点，
/// 别照抄别处注释里那句「由 pending-model-changes 门禁承担」。</item>
/// <item>只覆盖 <c>wms.WcsTaskRetryExhausted</c> → Notification 这一条字段对。
/// <c>wms.WcsTaskFailed</c> 同样携带 <c>DiagnosticMessage</c> 但**当前 src 内零消费者**；
/// <c>wms.WcsTaskCancelled</c> 由 WMS 自己消费、只转发给适配器不落库。两者都不由本类看守。</item>
/// <item>本类是纯模型读取 + 纯函数调用，**不能**证明真库落库行为；
/// WMS 那一侧的真库读数由 <c>WcsTaskCallbackValidatorTests</c> 的 <c>postgres:18</c> 用例承担。</item>
/// </list>
/// </remarks>
public sealed class WcsFailureMessageCrossServiceSummaryContractTests
{
    /// <summary>
    /// 产出侧无界、承载侧有界——这条把本票的处置形状本身钉住。
    /// </summary>
    /// <remarks>
    /// 谁把 <c>failure_message</c> 的 <c>HasMaxLength</c> 加回去，或者把 Notification 的
    /// <c>summary</c> 改成无界，本条都会红：前者推翻本票主结论，
    /// 后者会让下面那条「装得进」退化成恒真。
    /// </remarks>
    [Fact]
    public void Producer_message_column_is_unbounded_while_the_notification_summary_column_is_bounded()
    {
        using var wms = CreateWmsModel();
        using var notification = CreateNotificationModel();

        Assert.Null(WmsProperty(wms.Model, nameof(WcsTask.FailureMessage)).GetMaxLength());
        Assert.True(
            NotificationSummaryText.ResolveSummaryMaxLength(notification.Model) < int.MaxValue,
            "Notification 的 summary 列变成无界了，跨服务宽度契约已失去被证对象，"
            + "要显式重述这条契约而不是留着一条恒真断言。");
    }

    /// <summary>
    /// 最坏取值下拼出来的摘要必须装得进 Notification 的 <c>summary</c> 列。
    /// </summary>
    [Fact]
    public void Worst_case_summary_fits_the_notification_column()
    {
        using var wms = CreateWmsModel();
        using var notification = CreateNotificationModel();
        var summaryBound = NotificationSummaryText.ResolveSummaryMaxLength(notification.Model);
        var externalTaskIdWidth = WmsWidth(wms.Model, nameof(WcsTask.ExternalTaskId));
        var failureCodeWidth = WmsWidth(wms.Model, nameof(WcsTask.FailureCode));

        var worstCase = RetryExhaustedEvent(
            externalTaskId: new string('x', externalTaskIdWidth),
            failureCode: new string('c', failureCodeWidth),
            failureMessage: new string('m', summaryBound * 10));

        var rendered = WcsRetryExhaustedIntegrationEventHandlerForNotification.BuildSummary(worstCase, summaryBound);
        Assert.True(
            rendered.Length <= summaryBound,
            $"最坏取值下摘要长 {rendered.Length}，超过 summary 列宽 {summaryBound}："
            + "该消费者不接 catch，超界会以 22001 逃逸成 poison message，告警永远送不到收件人。");

        // 非同义反复：不截断时**确实**会超界。没有这一格，即使模板换成一个常量，上面那条也绿。
        Assert.True(
            WcsRetryExhaustedIntegrationEventHandlerForNotification.BuildSummary(worstCase, int.MaxValue).Length > summaryBound,
            "未截断的渲染没有超界，这一格没有在检验截断。");

        // 两段结构上界确实被算进去了：把它们各自顶满、诊断报文只留一个字符时，
        // 整条摘要仍然装得下，且**不该**被截。
        var boundedSegmentsOnly = RetryExhaustedEvent(
            externalTaskId: new string('x', externalTaskIdWidth),
            failureCode: new string('c', failureCodeWidth),
            failureMessage: "m");
        var short_ = WcsRetryExhaustedIntegrationEventHandlerForNotification.BuildSummary(boundedSegmentsOnly, summaryBound);
        Assert.True(
            short_.Length <= summaryBound,
            $"两段变量各自顶满列宽、诊断报文只有 1 个字符时摘要就已经长 {short_.Length}，"
            + $"超过 summary 列宽 {summaryBound}——这时截断诊断报文已经救不回来，需要重新裁定。");
        Assert.DoesNotContain(NotificationSummaryText.TruncationMarker, short_, StringComparison.Ordinal);
    }

    /// <summary>
    /// 摘要里的两段变量确实**取自**那两列，而不是本类挑了两个无关的列宽。
    /// </summary>
    /// <remarks>
    /// 没有这条，上面那条的「最坏取值」可能压根不是最坏：
    /// 比如 <c>PublicReference</c> 若改成由别的更宽的列填充，本类照绿。
    /// 这里让**真实转换器**从一个真实聚合产出事件，再比对两段变量的取值来源。
    /// </remarks>
    [Fact]
    public void Summary_variable_segments_come_from_the_two_bounded_wcs_columns()
    {
        using var wms = CreateWmsModel();
        var externalTaskId = new string('x', WmsWidth(wms.Model, nameof(WcsTask.ExternalTaskId)));
        var failureCode = new string('c', WmsWidth(wms.Model, nameof(WcsTask.FailureCode)));

        var task = WcsTask.Dispatch(
            "org-001",
            "env-dev",
            new WarehouseTaskId(Guid.CreateVersion7()),
            "agv",
            externalTaskId,
            """{"op":"move"}""");
        task.Fail(failureCode, "diagnostic", maxRetryAttempts: 1);
        Assert.Contains(task.GetDomainEvents(), x => x is WcsTaskRetryExhaustedDomainEvent);

        var produced = new WcsTaskRetryExhaustedIntegrationEventConverter()
            .Convert(new WcsTaskRetryExhaustedDomainEvent(task));

        Assert.Equal(externalTaskId, produced.Payload.PublicReference);
        Assert.Equal(failureCode, produced.Payload.DiagnosticCode);
        Assert.Equal("diagnostic", produced.Payload.DiagnosticMessage);
    }

    private static Nerv.IIP.Contracts.Wms.WmsIntegrationEvent RetryExhaustedEvent(
        string externalTaskId,
        string failureCode,
        string failureMessage)
    {
        var task = WcsTask.Dispatch(
            "org-001",
            "env-dev",
            new WarehouseTaskId(Guid.CreateVersion7()),
            "agv",
            externalTaskId,
            """{"op":"move"}""");
        task.Fail(failureCode, failureMessage, maxRetryAttempts: 1);
        return new WcsTaskRetryExhaustedIntegrationEventConverter()
            .Convert(new WcsTaskRetryExhaustedDomainEvent(task));
    }

    private static IProperty WmsProperty(IModel model, string propertyName) =>
        (model.FindEntityType(typeof(WcsTask)) ?? throw new InvalidOperationException("WMS 模型里找不到 WcsTask 实体。"))
            .FindProperty(propertyName)
            ?? throw new InvalidOperationException($"WcsTask 上没有 {propertyName} 属性。");

    private static int WmsWidth(IModel model, string propertyName) =>
        WmsProperty(model, propertyName).GetMaxLength()
            ?? throw new InvalidOperationException($"WcsTask.{propertyName} 没有声明列宽。");

    private static ModelFixture<WmsDbContext> CreateWmsModel() =>
        new(new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=nerv_iip_wcs_summary_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WmsFacts.Schema))
                .Options,
            WcsSummaryContractNoopMediator.Instance));

    private static ModelFixture<NotificationDbContext> CreateNotificationModel() =>
        new(new NotificationDbContext(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseNpgsql("Host=localhost;Database=nerv_iip_wcs_summary_contract;Username=nerv;Password=nerv")
                .Options,
            WcsSummaryContractNoopMediator.Instance));

    private sealed class ModelFixture<TContext>(TContext context) : IDisposable
        where TContext : DbContext
    {
        public IModel Model { get; } = context.GetService<IDesignTimeModel>().Model;

        public void Dispose() => context.Dispose();
    }

    private sealed class WcsSummaryContractNoopMediator : IMediator
    {
        internal static readonly WcsSummaryContractNoopMediator Instance = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This contract test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");
    }
}
