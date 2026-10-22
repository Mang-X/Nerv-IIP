using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.Notifications;

namespace Nerv.IIP.Notification.Web.Tests;

/// <summary>
/// 告警摘要渲染上界的契约（#3305）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：WCS 重试耗尽消费者拼出来的 <c>Summary</c>，
/// 在上游自由文本任意长时仍然装得进本服务的 <c>summary</c> 列；
/// 上界从 EF 模型现读、取全部承载列的最小值，代码里零手抄。</para>
///
/// <para><b>为什么需要这条</b>：改前该消费者把 WMS 转发的外部 WCS 诊断报文原样插值进摘要，
/// 而 <c>notification_intents.summary</c> / <c>notification_messages.summary</c> 都是 <c>varchar(2000)</c>。
/// 这条路径上没有任何长度闸——消费者走 <c>sender.Send</c> 不过 HTTP 端点校验器，
/// 聚合构造器只 <c>Required</c> 不截断，<c>IntegrationEventConsumerGuard</c> 只对信封校验失败写死信。
/// ⇒ 超长时 22001 逃逸成 poison，告警永远送不到 <c>role:wms-operator</c>，
/// 而 WMS 侧写库是成功的，症状不在上游那一侧显现。</para>
///
/// <para><b>合同分类</b>（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是 <c>NotificationIntentEntityTypeConfiguration</c> /
/// <c>NotificationMessageEntityTypeConfiguration</c> 的列宽声明；
/// <c>Regression</c> 的权威来源是 GitHub #3305。</para>
///
/// <para><b>值域边界（声明放弃了什么）</b>：</para>
/// <list type="bullet">
/// <item>只覆盖 <c>WcsRetryExhaustedIntegrationEventHandlerForNotification</c> 这一个消费者。
/// 本服务另有十余个消费者同样从上游 payload 拼 <c>Summary</c>，
/// 它们的入参上界是否够窄**未由本类看守**，已在 PR #3305 正文按入口来源逐条登记，本票不扩面修。</item>
/// <item>宿主用 InMemory provider：它不执行 <c>varchar(n)</c>，
/// 所以本类证的是「拼出来的长度 ≤ 模型声明的上界」，不是「真库不抛 22001」。
/// 两者之间只差 provider 是否兑现列宽，那一面由 <c>NotificationPostgresProfileTests</c> 的 lane 承担。</item>
/// </list>
/// </remarks>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class NotificationSummaryTextTests
{
    /// <summary>
    /// 承载列名单必须与 EF 模型**双向相等**。
    /// </summary>
    /// <remarks>
    /// 手写名单会静默漏掉后来者：将来谁再加一张带 <c>Summary</c> 列的表，
    /// 若它不进 <see cref="NotificationSummaryText.DeclaredLandingColumns"/>，
    /// <see cref="NotificationSummaryText.ResolveSummaryMaxLength"/> 就会算出一个比真实有效上界宽的数，
    /// 而所有既有断言照绿。所以值域从**模型**枚举，名单只负责被比对。
    /// </remarks>
    [Fact]
    public void Every_summary_column_in_the_model_is_a_declared_landing_column()
    {
        using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;

        var fromModel = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Where(property => property.ClrType == typeof(string)
                    && string.Equals(property.Name, nameof(NotificationIntent.Summary), StringComparison.Ordinal))
                .Select(property => $"{entityType.ClrType.Name}.{property.Name}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(fromModel);
        Assert.Equal(
            NotificationSummaryText.DeclaredLandingColumns
                .Select(x => $"{x.EntityType.Name}.{x.PropertyName}")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            fromModel);
    }

    /// <summary>
    /// 解析出来的上界必须**恰好**是各承载列宽的最小值（#3281 判据）。
    /// </summary>
    /// <remarks>
    /// 反同义反复：同时断言两列都真的声明了正上界。
    /// 若哪天两列都变成无界，本条会红并逼当轮显式处理，而不是让
    /// <see cref="NotificationSummaryText.ResolveSummaryMaxLength"/> 悄悄返回 <see cref="int.MaxValue"/>。
    /// </remarks>
    [Fact]
    public void Resolved_bound_is_the_minimum_of_the_landing_column_widths()
    {
        using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;

        var widths = NotificationSummaryText.DeclaredLandingColumns
            .Select(column => Property(model, column.EntityType, column.PropertyName).GetMaxLength())
            .ToArray();

        Assert.All(widths, width => Assert.True(width is > 0, "承载列没有声明列宽，最小值口径已失效。"));
        Assert.Equal(widths.Min()!.Value, NotificationSummaryText.ResolveSummaryMaxLength(model));
    }

    /// <summary>
    /// <see cref="NotificationSummaryText.Fit"/> 的边界值：恰好装得下 / 超一个字符 / 代理对切点。
    /// </summary>
    /// <remarks>
    /// 「恰好装得下」那格不可省：只有「超一个字符会被截」时，
    /// 把 <c>&lt;=</c> 写成 <c>&lt;</c>（或把上界减一）照绿。
    /// 代理对那格钉的是：切点落在代理对中间时要再退一位，否则产出孤立高代理项、
    /// 编码成 UTF-8 时会失败——那是把「装得下」换成另一种崩法。
    /// </remarks>
    [Fact]
    public void Fit_keeps_the_exact_bound_and_marks_anything_longer()
    {
        var exact = new string('a', 20);
        Assert.Same(exact, NotificationSummaryText.Fit(exact, 20));

        var oneOver = new string('a', 21);
        var trimmed = NotificationSummaryText.Fit(oneOver, 20);
        Assert.Equal(20, trimmed.Length);
        Assert.EndsWith(NotificationSummaryText.TruncationMarker, trimmed, StringComparison.Ordinal);
        Assert.Equal(new string('a', 19) + NotificationSummaryText.TruncationMarker, trimmed);

        // 切点恰好落在代理对中间："ab" + 😀(2 个 UTF-16 单元)，上界 4 ⇒ keep=3 落在高代理项上。
        var withSurrogatePair = "ab\U0001F600cd";
        var surrogateSafe = NotificationSummaryText.Fit(withSurrogatePair, 4);
        Assert.Equal("ab" + NotificationSummaryText.TruncationMarker, surrogateSafe);
        Assert.DoesNotContain(surrogateSafe, char.IsSurrogate);

        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationSummaryText.Fit("abc", 1));
    }

    /// <summary>
    /// 真实消费者端到端：上游诊断报文任意长时，落库的两处摘要都装得进各自的列。
    /// </summary>
    /// <remarks>
    /// 断言落在**落库后的实体**上而不是中间字符串：中间字符串对了、
    /// 聚合把别的东西写进 <c>Summary</c> 也照绿。
    /// 同时读 <c>Messages</c>：摘要是被复制进每条消息的，只看 intent 会漏掉那一份。
    /// </remarks>
    [Fact]
    public async Task Consumer_fits_an_unbounded_upstream_diagnostic_message_into_the_summary_column()
    {
        using var factory = CreateHost();
        int bound;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            bound = NotificationSummaryText.ResolveSummaryMaxLength(dbContext.Model);
            var handler = ActivatorUtilities.CreateInstance<WcsRetryExhaustedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
            await ((IIntegrationEventHandler<WmsIntegrationEvent>)handler)
                .HandleAsync(RetryExhaustedEvent(new string('m', 100_000)), CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var intent = await dbContext.NotificationIntents.Include(x => x.Messages).SingleAsync();
            Assert.Equal(bound, intent.Summary.Length);
            Assert.EndsWith(NotificationSummaryText.TruncationMarker, intent.Summary, StringComparison.Ordinal);
            Assert.StartsWith("WCS task EXT-3305-001 exhausted retry attempts: PLC_TIMEOUT ", intent.Summary, StringComparison.Ordinal);
            var message = Assert.Single(intent.Messages);
            Assert.True(message.Summary.Length <= bound, "消息副本的摘要超过了 summary 列宽。");
            Assert.Equal(intent.Summary, message.Summary);
        }
    }

    /// <summary>
    /// 对照格：装得下的诊断报文**一个字符都不许改**。
    /// </summary>
    /// <remarks>
    /// 没有这一格，「一律截断到上界」也能让上面那条绿——
    /// 那会把每条正常告警都无谓地打上截断标记。
    /// </remarks>
    [Fact]
    public async Task Consumer_leaves_a_short_diagnostic_message_untouched()
    {
        using var factory = CreateHost();
        using (var scope = factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<WcsRetryExhaustedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
            await ((IIntegrationEventHandler<WmsIntegrationEvent>)handler)
                .HandleAsync(RetryExhaustedEvent("blocked aisle"), CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var intent = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().NotificationIntents.SingleAsync();
            Assert.Equal(
                "WCS task EXT-3305-001 exhausted retry attempts: PLC_TIMEOUT blocked aisle",
                intent.Summary);
            Assert.DoesNotContain(NotificationSummaryText.TruncationMarker, intent.Summary, StringComparison.Ordinal);
        }
    }

    private static WmsIntegrationEvent RetryExhaustedEvent(string diagnosticMessage) =>
        new(
            $"evt-{Guid.CreateVersion7():N}",
            WmsIntegrationEventTypes.WcsTaskRetryExhausted,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            WmsIntegrationEventSources.BusinessWms,
            "wms:wcs-retry-exhausted:org-001:env-dev:agv:AGV-01:EXT-3305-001",
            "wms:wcs-retry-exhausted:org-001:env-dev:agv:AGV-01:EXT-3305-001",
            "org-001",
            "env-dev",
            "system:wms",
            "wms:wcs-retry-exhausted:org-001:env-dev:agv:AGV-01:EXT-3305-001",
            new WmsIntegrationPayload(
                "EXT-3305-001",
                null,
                null,
                null,
                null,
                null,
                null,
                "Failed",
                "PLC_TIMEOUT",
                diagnosticMessage,
                AdapterType: "agv"));

    private static IProperty Property(IModel model, Type entityType, string propertyName) =>
        model.FindEntityType(entityType)?.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Notification 模型里找不到 {entityType.Name}.{propertyName}。");

    private static WebApplicationFactory<Program> CreateHost() => new NotificationSummaryWebApplicationFactory();

    private sealed class NotificationSummaryWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                }));
        }
    }
}
