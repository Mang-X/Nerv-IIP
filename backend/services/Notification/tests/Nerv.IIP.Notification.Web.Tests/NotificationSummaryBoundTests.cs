using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.Notifications;
using Nerv.IIP.Notification.Web.Endpoints.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Tests;

/// <summary>
/// 通知摘要的承载上界。
/// <para>
/// 背景：<c>notification_intents.summary</c> 由 20 个进程内构造点插值拼出，构造上没有任何上界；
/// 溢出后 22001 会以 DbUpdateException 逃出消费者 → CAP 重试 → poison，
/// 结果是<b>告警永远送不到收件人，而上游服务写库是成功的</b>。
/// </para>
/// <para>
/// 收口方式：夹紧由<b>命令层</b>保证（<see cref="SubmitNotificationIntentCommand"/> 不收裸 string 摘要），
/// 不是 20 处各写一遍；HTTP 提交路径由端点校验器在 handler 之前<b>拒绝</b>而不是截断。
/// </para>
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class NotificationSummaryBoundTests
{
    // ---------------------------------------------------------------------
    // 上界来源：从 EF 模型派生，不手抄列宽
    // ---------------------------------------------------------------------

    [Fact]
    public void Summary_bound_is_the_minimum_declared_width_of_every_carrying_column()
    {
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;

        var declaredWidths = NotificationSummaryBudget.CarryingColumns
            .Select(column => model.FindEntityType(column.ClrType)!.FindProperty(column.PropertyName)!.GetMaxLength())
            .ToArray();

        Assert.All(declaredWidths, width => Assert.NotNull(width));
        Assert.Equal(
            declaredWidths.Select(width => width!.Value).Min(),
            NotificationSummaryBudget.FromModel(model).MaxLength);
    }

    /// <summary>
    /// 承载列名单与「从模型反向枚举」双向对撞：新增一列叫 Summary 的承载列而忘了登记，这里会红。
    /// <para>
    /// ⚠️ 边界：反向枚举的身份判据是<b>属性名</b>。若将来出现一个承载摘要但<b>不叫 Summary</b> 的列，
    /// 本断言看不见它 —— 这条不宣称完备，只宣称「按属性名这一面不弱于名单」。
    /// </para>
    /// </summary>
    [Fact]
    public void Carrying_column_ledger_matches_reverse_enumeration_of_the_model()
    {
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;

        var reverseEnumerated = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.Name == nameof(NotificationIntent.Summary))
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var ledger = NotificationSummaryBudget.CarryingColumns
            .Select(column => column.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ledger, reverseEnumerated);
    }

    /// <summary>承载列不在模型里时必须 fail closed，而不是回落成「无上界」。</summary>
    [Fact]
    public void Summary_bound_derivation_fails_closed_when_a_carrying_column_is_absent_from_the_model()
    {
        using var context = new EmptyModelDbContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => NotificationSummaryBudget.FromModel(context.Model));

        Assert.Contains("is not part of the EF model", exception.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // ⭐ 校验器必须真的能从 host 容器里解析出来
    // 规则写了但容器解析不到 = 静默失效，且失效方向是「HTTP 超长值被悄悄截断」。
    // ---------------------------------------------------------------------

    /// <summary>
    /// ⭐ 校验器按 FastEndpoints 的方式从<b>真实 host 容器</b>构造（<see cref="ActivatorUtilities"/> + 根 provider）。
    /// <para>
    /// 这条钉的是「规则写了但容器里解析不到」这一形态：
    /// 若 <see cref="NotificationSummaryBudget"/> 没被注册，这里构造就会抛，而不是让长度规则悄悄消失。
    /// </para>
    /// <para>
    /// 上界不读规则树元数据、直接跑<b>边界对</b>：恰好 bound 通过、bound+1 失败，
    /// 把「上界恰等于模型派生值」钉死，而这个值本身来自模型 —— 全程无列宽字面量。
    /// </para>
    /// </summary>
    [Fact]
    public void Submit_intent_validator_resolves_from_the_host_container_and_carries_the_model_derived_bound()
    {
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var modelDerivedBound = NotificationSummaryBudget
            .FromModel(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model)
            .MaxLength;
        Assert.Equal(
            modelDerivedBound,
            scope.ServiceProvider.GetRequiredService<NotificationSummaryBudget>().MaxLength);

        IValidator<SubmitNotificationIntentRequest> validator =
            ActivatorUtilities.CreateInstance<SubmitNotificationIntentRequestValidator>(factory.Services);

        var atBound = validator.Validate(CreateHttpIntent(new string('a', modelDerivedBound)));
        var overBound = validator.Validate(CreateHttpIntent(new string('a', modelDerivedBound + 1)));

        Assert.True(atBound.IsValid, string.Join("; ", atBound.Errors.Select(x => x.ErrorMessage)));
        Assert.False(overBound.IsValid);
        Assert.Contains(
            overBound.Errors,
            // FastEndpoints 把属性名解析成 camelCase（与 HTTP 响应体里的 errors 键一致）。
            error => string.Equals(
                error.PropertyName,
                nameof(SubmitNotificationIntentRequest.Summary),
                StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------
    // HTTP 提交路径：拒绝，不是截断
    // ---------------------------------------------------------------------

    /// <summary>
    /// HTTP 超界必须被<b>拒绝</b>而不是被截断，且必须是<b>端点校验器</b>拒的。
    /// <para>
    /// 判据用的是响应体里 <c>errors.summary</c> 这个字段级错误：
    /// 摘要非空时只有长度规则会产出它；handler 里 <c>FromSubmitted</c> 抛的 KnownException 同样是 400，
    /// 但它落在 <c>message</c> 上而不是 <c>errors</c> 上 —— 只断言 400 分不出是哪一层拒的。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Http_submitted_summary_over_the_bound_is_rejected_by_the_endpoint_validator_and_nothing_is_persisted()
    {
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var bound = ResolveBound(factory);

        var response = await client.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateHttpIntent(new string('a', bound + 1), "dedupe-http-over-bound"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(
            body.RootElement.TryGetProperty("errors", out var errors)
            && errors.TryGetProperty(
                nameof(SubmitNotificationIntentRequest.Summary).ToLowerInvariant(),
                out _),
            $"超界摘要必须由端点校验器以字段级错误拒绝，实际响应体：{body.RootElement}");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await dbContext.NotificationIntents.CountAsync());
    }

    [Fact]
    public async Task Http_submitted_summary_at_the_bound_is_persisted_verbatim()
    {
        using var factory = new NotificationEndpointTests.NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var bound = ResolveBound(factory);
        var summary = new string('a', bound);

        var response = await client.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateHttpIntent(summary, "dedupe-http-at-bound"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents.Include(x => x.Messages).SingleAsync();
        Assert.Equal(summary, intent.Summary);
        Assert.Equal(summary, Assert.Single(intent.Messages).Summary);
    }

    // ---------------------------------------------------------------------
    // 两个具名工厂：拼装夹紧 / 提交抛
    // ---------------------------------------------------------------------

    [Fact]
    public void Render_clamps_over_bound_text_and_marks_it_as_truncated()
    {
        var budget = CreateBudget();
        var text = new string('a', budget.MaxLength + 500);

        var summary = NotificationSummary.Render(text, budget);

        Assert.Equal(budget.MaxLength, summary.Value.Length);
        Assert.EndsWith(NotificationSummary.TruncationMarker, summary.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_keeps_text_at_or_below_the_bound_verbatim()
    {
        var budget = CreateBudget();
        var text = new string('a', budget.MaxLength);

        Assert.Equal(text, NotificationSummary.Render(text, budget).Value);
    }

    [Fact]
    public void Render_does_not_split_a_surrogate_pair()
    {
        var budget = CreateBudget();
        // 末尾恰好压在代理对上：劈开会得到一个无法渲染的孤立码元。
        var text = new string('a', budget.MaxLength - NotificationSummary.TruncationMarker.Length) + "🙂🙂";

        var value = NotificationSummary.Render(text, budget).Value;

        Assert.True(value.Length <= budget.MaxLength);
        var lastKeptIndex = value.Length - NotificationSummary.TruncationMarker.Length - 1;
        Assert.False(char.IsHighSurrogate(value[lastKeptIndex]));
    }

    [Fact]
    public void FromSubmitted_throws_instead_of_silently_rewriting_the_callers_text()
    {
        var budget = CreateBudget();
        var text = new string('a', budget.MaxLength + 1);

        var exception = Assert.Throws<KnownException>(() => NotificationSummary.FromSubmitted(text, budget));

        Assert.Contains("通知摘要长度", exception.Message, StringComparison.Ordinal);
        Assert.Equal(text, new string('a', budget.MaxLength + 1));
    }

    [Fact]
    public void Command_carries_the_typed_summary_into_the_request_it_holds()
    {
        var budget = CreateBudget();
        var request = CreateHttpIntent(new string('a', budget.MaxLength + 10));

        var command = new SubmitNotificationIntentCommand(
            "org-001",
            "env-001",
            request,
            NotificationSummary.Render(request.Summary, budget),
            DateTimeOffset.Parse("2026-09-11T00:00:00Z"));

        // Request.Summary 与 Summary.Value 由构造成立地相等：读哪一个都是同一份。
        Assert.Equal(command.Summary.Value, command.Request.Summary);
        Assert.Equal(budget.MaxLength, command.Request.Summary.Length);
    }

    // ---------------------------------------------------------------------
    // 进程内拼装路径：真的落到库里且装得下
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Consumer_rendered_summary_is_clamped_before_it_reaches_the_carrying_columns()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Quality:SpcAlert:RecipientRefs:0"] = "role:quality-engineer",
        });
        var bound = ResolveBound(factory);

        await HandleSpcAlertAsync(factory, CreateSpcAlertEvent(
            "event-spc-oversized-summary",
            "quality-spc-alert:org-001:env-dev:oversized",
            payloadSummary: new string('x', bound + 2_000)));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents.Include(x => x.Messages).SingleAsync();

        Assert.Equal(bound, intent.Summary.Length);
        Assert.EndsWith(NotificationSummary.TruncationMarker, intent.Summary, StringComparison.Ordinal);
        Assert.Equal(intent.Summary, Assert.Single(intent.Messages).Summary);
    }

    // ---------------------------------------------------------------------
    // 同质枚举集合（标识符）：截项 + 计数提示，不截字符
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Spc_rule_codes_are_truncated_by_item_so_every_listed_code_is_a_real_code()
    {
        var ruleCodes = Enumerable.Range(1, 17).Select(index => $"RULE-TREND-{index:D3}").ToArray();
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Quality:SpcAlert:RecipientRefs:0"] = "role:quality-engineer",
        });

        await HandleSpcAlertAsync(factory, CreateSpcAlertEvent(
            "event-spc-many-rules",
            "quality-spc-alert:org-001:env-dev:many-rules",
            payloadSummary: string.Empty,
            ruleCodes: ruleCodes));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var summary = (await dbContext.NotificationIntents.SingleAsync()).Summary;

        // 列出的每个码都是真码，没有被劈成 "RULE-TREND-0" 这种查不到的半截标识符。
        var listed = ruleCodes.Take(NotificationSummaryList.MaxListedItems).ToArray();
        Assert.All(listed, code => Assert.Contains(code, summary, StringComparison.Ordinal));
        Assert.All(
            ruleCodes.Skip(NotificationSummaryList.MaxListedItems),
            code => Assert.DoesNotContain(code, summary, StringComparison.Ordinal));
        // 缺失被显式化，而不是让读者以为只有 5 条规则命中。
        Assert.Contains($"{ruleCodes.Length - NotificationSummaryList.MaxListedItems} more", summary, StringComparison.Ordinal);
        Assert.Contains($"({ruleCodes.Length} total)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scheduling_affected_resources_are_truncated_by_item_so_no_fake_resource_id_appears()
    {
        var resourceIds = Enumerable.Range(1, 23).Select(index => $"WC-PRESS-{index:D2}").ToArray();
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Scheduling:InvalidationNotification:RecipientRefs:0"] = "role:production-planner",
        });

        await HandleSchedulePlanInvalidatedAsync(factory, CreateSchedulePlanInvalidatedEvent(
            "event-schedule-many-resources",
            "schedule-plan-invalidated:org-001:env-001:many-resources",
            resourceIds));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var summary = (await dbContext.NotificationIntents.SingleAsync()).Summary;

        var listed = resourceIds.Take(NotificationSummaryList.MaxListedItems).ToArray();
        Assert.All(listed, id => Assert.Contains(id, summary, StringComparison.Ordinal));
        Assert.All(
            resourceIds.Skip(NotificationSummaryList.MaxListedItems),
            id => Assert.DoesNotContain(id, summary, StringComparison.Ordinal));
        Assert.Contains($"{resourceIds.Length - NotificationSummaryList.MaxListedItems} more", summary, StringComparison.Ordinal);
        Assert.Contains($"({resourceIds.Length} total)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_lists_every_item_when_the_collection_fits()
    {
        var items = Enumerable.Range(1, NotificationSummaryList.MaxListedItems)
            .Select(index => $"RULE-{index}")
            .ToArray();

        var described = NotificationSummaryList.Describe(items, "fallback");

        Assert.Equal(string.Join(", ", items), described);
        Assert.DoesNotContain("more", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// 列出条数是<b>产品数</b>：由列宽反算会让它随列宽漂移，并把产品判断伪装成算术。
    /// 这里把它钉成字面量，改它就必须是一次显式的产品决定，而不是顺手跟着别处漂。
    /// </summary>
    [Fact]
    public void Listed_item_count_is_a_product_number_not_a_derived_one()
    {
        Assert.Equal(5, NotificationSummaryList.MaxListedItems);
    }

    [Fact]
    public void Describe_falls_back_when_the_collection_is_empty()
    {
        Assert.Equal("fallback", NotificationSummaryList.Describe([], "fallback"));
        Assert.Equal("fallback", NotificationSummaryList.Describe(null, "fallback"));
    }

    // ---------------------------------------------------------------------
    // 异构语义段：只整体夹紧，不截项
    // 这条防的是「下一轮同族扩面顺手把它也改成截项」。
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Industrial_telemetry_alarm_summary_is_clamped_as_a_whole_and_never_truncated_by_item()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["IndustrialTelemetry:AlarmNotification:RecipientRefs:0"] = "role:maintenance-dispatcher",
        });
        var bound = ResolveBound(factory);

        // 单项无界（TagKey 来自外部 payload），但语义段只有 5 段：
        // 丢任何一段都是丢语义，所以这里只能整体夹紧。
        await HandleAlarmRaisedAsync(factory, CreateAlarmRaisedEvent(
            "event-alarm-oversized-tag",
            "industrial-alarm-raised:oversized-tag",
            tagKey: new string('t', bound + 500)));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var summary = (await dbContext.NotificationIntents.SingleAsync()).Summary;

        Assert.Equal(bound, summary.Length);
        Assert.EndsWith(NotificationSummary.TruncationMarker, summary, StringComparison.Ordinal);
        // 没有被套上截项 + 计数提示：那是同质枚举集合的处置，这里套用会把语义段丢成"还有 N 项"。
        Assert.DoesNotContain(" more (", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(" total)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Industrial_telemetry_alarm_summary_keeps_every_semantic_segment_when_it_fits()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["IndustrialTelemetry:AlarmNotification:RecipientRefs:0"] = "role:maintenance-dispatcher",
        });

        await HandleAlarmRaisedAsync(factory, CreateAlarmRaisedEvent(
            "event-alarm-all-segments",
            "industrial-alarm-raised:all-segments"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var summary = (await dbContext.NotificationIntents.SingleAsync()).Summary;

        Assert.Contains("Alarm HI_TEMP raised on device asset-001", summary, StringComparison.Ordinal);
        // 语义段之间用 "; " 连接。这同时钉住「这一处没有被改走同质枚举集合那条路径」：
        // 那条路径用 ", " 连接，并且在段数超过 MaxListedItems 时会把段当枚举项丢掉。
        // ⚠️ 今天 parts 最多 5 段、恰等于 MaxListedItems，所以「丢段」这一面还够不着；
        // 能被这组断言杀掉的是「改了路由」，不是「已经丢了段」。段数一旦长到 6，丢段才成为现实风险。
        Assert.Contains("; tag temp.bearing", summary, StringComparison.Ordinal);
        Assert.Contains("; observed 97.5 C", summary, StringComparison.Ordinal);
        Assert.Contains("; threshold 80 C", summary, StringComparison.Ordinal);
        Assert.Contains("; raised at 2026-07-03T08:00:00", summary, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // 夹具
    // ---------------------------------------------------------------------

    private static NotificationSummaryBudget CreateBudget()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"summary-bound-{Guid.NewGuid():N}")
            .Options;
        using var context = new ApplicationDbContext(options, mediator: null!);
        return NotificationSummaryBudget.FromModel(context.Model);
    }

    private static int ResolveBound(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<NotificationSummaryBudget>().MaxLength;
    }

    private static SubmitNotificationIntentRequest CreateHttpIntent(string summary, string dedupeKey = "dedupe-summary-bound") =>
        new(
            SourceService: "ops",
            SourceEventType: "ops.OperationTaskFailed",
            SourceEventId: dedupeKey,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", dedupeKey, null),
            Title: "Restart failed",
            Summary: summary,
            SuggestedRecipientRefs: ["user:admin"]);

    private static async Task HandleSpcAlertAsync(
        NotificationConsumerWebApplicationFactory factory,
        SpcAlertRaisedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<SpcAlertRaisedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<SpcAlertRaisedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleSchedulePlanInvalidatedAsync(
        NotificationConsumerWebApplicationFactory factory,
        SchedulePlanInvalidatedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<SchedulePlanInvalidatedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<SchedulePlanInvalidatedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleAlarmRaisedAsync(
        NotificationConsumerWebApplicationFactory factory,
        AlarmRaisedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<AlarmRaisedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<AlarmRaisedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static SpcAlertRaisedIntegrationEvent CreateSpcAlertEvent(
        string eventId,
        string idempotencyKey,
        string payloadSummary = "SPC trend detected for SKU-RM-1000 length at WC-MIX-01.",
        IReadOnlyCollection<string>? ruleCodes = null) =>
        new(
            EventId: eventId,
            EventType: QualityIntegrationEventTypes.SpcAlertRaised,
            EventVersion: QualityIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-07T08:00:00Z"),
            SourceService: QualityIntegrationEventSources.BusinessQuality,
            CorrelationId: $"corr-{eventId}",
            CausationId: "spc-evaluate-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "system:business-quality",
            IdempotencyKey: idempotencyKey,
            Payload: new SpcAlertRaisedPayload(
                AlertKey: "quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01",
                ResourceType: "quality-spc-alert",
                SkuCode: "SKU-RM-1000",
                CharacteristicCode: "length",
                WorkCenterId: "WC-MIX-01",
                RuleCodes: ruleCodes ?? [QualitySpcRuleCodes.TrendIncreasing],
                Severity: NotificationContractConstants.SeverityWarning,
                LatestMeasuredAtUtc: DateTimeOffset.Parse("2026-07-07T07:59:00Z"),
                Summary: payloadSummary));

    private static SchedulePlanInvalidatedIntegrationEvent CreateSchedulePlanInvalidatedEvent(
        string eventId,
        string idempotencyKey,
        IReadOnlyCollection<string> affectedResourceIds) =>
        new(
            EventId: eventId,
            EventType: SchedulingIntegrationEventTypes.SchedulePlanInvalidated,
            EventVersion: SchedulingIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-03T08:00:00Z"),
            SourceService: SchedulingIntegrationEventSources.BusinessScheduling,
            CorrelationId: $"corr-{eventId}",
            CausationId: "schedule-invalidate-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:scheduling",
            IdempotencyKey: idempotencyKey,
            Payload: new SchedulePlanInvalidatedPayload(
                PlanId: "PLAN-001",
                ProblemId: "PROBLEM-001",
                ContractVersion: 1,
                AlgorithmVersion: "v1",
                ProblemFingerprint: "fingerprint-001",
                PlanStatus: "released",
                ReasonCode: "resource-unavailable",
                SourceEventType: "mes.WorkOrderReleased",
                SourceEventId: "source-event-001",
                AffectedResourceIds: affectedResourceIds,
                AffectedOperations: []));

    private static AlarmRaisedIntegrationEvent CreateAlarmRaisedEvent(
        string eventId,
        string idempotencyKey,
        string tagKey = "temp.bearing") =>
        new(
            EventId: eventId,
            EventType: IndustrialTelemetryIntegrationEventTypes.AlarmRaised,
            EventVersion: IndustrialTelemetryIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-03T08:00:00Z"),
            SourceService: IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            CorrelationId: $"corr-{eventId}",
            CausationId: "sample-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:industrial-telemetry",
            IdempotencyKey: idempotencyKey,
            Payload: new AlarmRaisedPayload(
                AlarmEventId: "alarm-event-001",
                DeviceAssetId: "asset-001",
                AlarmCode: "HI_TEMP",
                Severity: "critical",
                RaisedAtUtc: DateTimeOffset.Parse("2026-07-03T08:00:00Z"),
                ExternalAlarmId: "external-alarm-001",
                Priority: null,
                TagKey: tagKey,
                ObservedValue: 97.5m,
                ThresholdValue: 80m,
                UnitCode: "C"));

    private sealed class EmptyModelDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase($"empty-summary-model-{Guid.NewGuid():N}");
    }

    private sealed class NotificationConsumerWebApplicationFactory(IReadOnlyDictionary<string, string?>? settings = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var mergedSettings = new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                };
                if (settings is not null)
                {
                    foreach (var (key, value) in settings)
                    {
                        mergedSettings[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(mergedSettings);
            });
        }
    }
}
