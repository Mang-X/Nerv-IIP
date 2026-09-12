using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;

using AppHubDbContext = Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext;
using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using IndustrialTelemetryDbContext = Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.ApplicationDbContext;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using InventoryDomainEvents = Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using MaintenanceDbContext = Nerv.IIP.Business.Maintenance.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using NotificationDbContext = Nerv.IIP.Notification.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;
using ApprovalDbContext = Nerv.IIP.Business.Approval.Infrastructure.ApplicationDbContext;
using ApprovalDomainEvents = Nerv.IIP.Business.Approval.Domain.DomainEvents;
using MesDomainEvents = Nerv.IIP.Business.Mes.Domain.DomainEvents;
using ProductEngineeringDbContext = Nerv.IIP.Business.ProductEngineering.Infrastructure.ApplicationDbContext;
using ProductEngineeringDomainEvents = Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using QualityDomainEvents = Nerv.IIP.Business.Quality.Domain.DomainEvents;
using SchedulingDomainEvents = Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using WmsDomainEvents = Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 集成事件**信封键**与其**跨服务**承载列之间的机器可验关系（#3339）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：已接入 <see cref="IntegrationEventIdempotencyKey"/> 的 producer，
/// 在**各自 EF 列宽定义的最坏输入**下产出的信封键，都装得进平台 inbox 那一族里最窄的承载列。
/// 两侧数值全部在运行时从各自的 EF 模型读出，**本类不手抄任何长度数字**。</para>
///
/// <para><b>为什么这条断言住在 Acceptance</b>：键在各 producer 服务构造、在 9 个消费服务落库，
/// 这些服务互不引用，只有测试侧能把多个 EF 模型放在一起对撞。
/// 同族先例 <c>MesFinishedGoodsReceiptInventoryPostingKeyBoundContractTests</c> 也住这里。
/// ⚠️ 本 PR 为此给本测试项目**新增了 AppHub.Web 的 ProjectReference**——
/// 9 张平台表里 AppHub 那张此前在本程序集不可见，不加就只能登记 8 张然后声称「全部」。
/// 另一个能同时看到 AppHub 与业务服务的程序集是 <c>Nerv.IIP.FacadeCoverage.Tests</c>，
/// 那是门面覆盖架构测试，不该承载跨服务列宽契约。**理由是职责，不是「唯一」。**</para>
///
/// <para><b>承载列登记表怎么派生的</b>：登记的是 <c>(DbContext, 物理表名, 属性名)</c>，
/// 列宽运行时从各服务真 EF 模型读；解析不到就红
/// （<see cref="Every_declared_platform_carrier_column_resolves"/>），改表名或删列不会静默通过。
/// 登记依据是逐跳实读写入端：<c>ProcessedIntegrationEventInbox.TryRecordAsync</c> 把
/// <c>IIntegrationEventEnvelope.IdempotencyKey</c> **原样**写进各服务的
/// <c>processed_integration_events</c>；Notification 的
/// <c>NotificationIntent</c> 把同一把键原样写进 <c>notification_intents.dedupe_key</c>，
/// 而 <c>TryRecordAsync</c> **只 <c>Add</c> 不 SaveChanges</c>**，两行在同一个 UoW 落
/// ⇒ 先炸哪一列都一样、整事务回滚 ⇒ 有效上界取两者最小（#3281 判据）。</para>
///
/// <para><b>本类不证明什么（值域边界，别读成完备）</b>：</para>
/// <list type="number">
/// <item><b>不证明登记表穷举了所有承载该键的列。</b>新增一个消费侧写入点不会让本类报红。</item>
/// <item><b>故意不登记</b> <c>integration_event_dead_letters.idempotency_key</c>(500)：
/// 写入端走 <c>TruncateOptional</c> **截断**，截断列不构成上界，登记它等于登记一个假权威。</item>
/// <item><b>故意不登记</b> <c>inspection_tasks.trigger_idempotency_key</c>(474)，
/// 它是一条**更窄但事件专属**的列，由 <c>InspectionTaskTriggerKey</c> 与
/// <c>InspectionTaskTriggerKeyCrossServiceWidthContractTests</c> 自己看守（#2977 / #3318）。
/// <para><b>不登记的依据不是「它只承载别人的键」，而是下面这条逐位点实读</b>
/// （<c>InspectionTaskTriggerIntegrationEventHandlers.cs</c> 里写这一列的**全部 4 个位点**）：</para>
/// <list type="table">
/// <item><term><c>:73</c></term><description>消费 <c>WmsIntegrationEvent</c> 的
/// <c>WmsIntegrationEventTypes.InboundOrderCompleted</c>，写 <c>{信封键}:{line.LineReference}</c>
/// —— <b>射程内唯一到达该列的位点</b>。</description></item>
/// <item><term><c>:129</c></term><description>消费 Erp <c>PurchaseReceiptRecordedIntegrationEvent</c>，
/// 写 <c>{信封键}:{line.LineReference}</c>。Erp 本 PR **未接入**。</description></item>
/// <item><term><c>:183</c></term><description>消费 Mes <c>MesOperationTaskCompletedIntegrationEvent</c>，
/// **逐字**写信封键。Mes 本 PR **未接入**。</description></item>
/// <item><term><c>:230</c></term><description>消费 Mes <c>FinishedGoodsReceiptRequestedIntegrationEvent</c>，
/// **逐字**写信封键。Mes 本 PR **未接入**。</description></item>
/// </list>
/// <para>⇒ <b>真正的安全依据</b>：本 PR 接入的四个 producer 里，只有 <c>wms:inbound-completed</c>
/// 到得了这一列，而它的最坏长度是
/// <c>4（"wms:"）+ 17（"inbound-completed"）+ 1（":"）+ 100（org）+ 1 + 100（env）+ 1 + 100（InboundOrderNo）= <b>324</b></c>
/// （四段列宽全部实读自 Wms EF 模型），<b>324 &lt; 512 ⇒ 构造上到不了回落分支</b>
/// ⇒ 这一列上的值与改动前**逐字相同**，本 PR 没有、也不可能削弱那条 474 的守卫。
/// 另外三条的 producer 本 PR 根本没碰，形态零变化。</para>
/// <para>而**不把 474 并进** <see cref="IntegrationEventIdempotencyKey.Budget"/> 的理由是另一件事：
/// 并进来会把全平台预算压到 474，让今天长度落在 475..512、在自己链路上完全合法的键无谓改形，
/// 反而破坏存量键逐字保持。**所以本类的「所有承载列」量词限定在平台 inbox 那一族。**</para></item>
/// <item><b>不证明所有 producer 都已接入。</b>本类登记的是
/// <see cref="ConvertedProducerKeys"/> 那九个服务（#3368 四个 + #3370 五个）；
/// #3370 第 1 问判定为 🟢 的六个 producer（DemandPlanning / Erp / MasterData /
/// BarcodeLabel / Maintenance / Ops）**仍是纯拼接**，不接入的不会让本类报红。
/// **不新建源码文本扫描护栏**去看守这件事（#3176 / PR #3214 实证不收敛）；
/// 让 producer 枚举退化成编译期强制那条路已实证可行、按规模另票承接（#3382）。</item>
/// <item><b>⚠️ 已登记边界：<c>ComposeServiceScoped</c> 在少于 3 段时抛，旧实现会产出短键。</b>
/// <c>MinimumTailParts = 2</c> 要求「1 个 kind 段 + 至少 2 个尾段」，
/// 而改动前各服务那份 <c>$"{prefix}{string.Join(':', parts)}"</c> 对任意段数都照产出。
/// ⇒ 若将来有人用 &lt;3 段调用某个已委派服务的 <c>EventIds.Idempotency</c>，
/// 拿到的是 <c>ArgumentException</c> 而不是一把短键。
/// 这是 <b>#3368 引入的入口性质，不是 #3370 新增的</b>：今天全部调用点都 ≥4 段
/// （#3370 已逐点核过，五个服务整程序集全绿即是未触发的读数），因此**本票不修**、只登记。
/// 本类**不看守**这条——它是构造前置条件，不是长度性质。</item>
/// </list>
/// </remarks>
public sealed class IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests
{
    private const string ProcessedIntegrationEventsTable = "processed_integration_events";
    private const string NotificationIntentsTable = "notification_intents";
    private const string IdempotencyKeyPropertyName = "IdempotencyKey";
    private const string DedupeKeyPropertyName = "DedupeKey";

    /// <summary>
    /// 平台 inbox 那一族承载列。每条都已实读写入点确认**落库的就是信封键本身**
    /// （不是 #3290 那种哈希派生的幽灵权威）。
    /// </summary>
    private static CarrierColumn[] PlatformCarrierColumns() =>
    [
        new("AppHub", ModelOnly<AppHubDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Notification", ModelOnly<NotificationDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Wms", ModelOnly<WmsDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Quality", ModelOnly<QualityDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Scheduling", ModelOnly<SchedulingDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Mes", ModelOnly<MesDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("DemandPlanning", ModelOnly<DemandPlanningDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Maintenance", ModelOnly<MaintenanceDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Erp", ModelOnly<ErpDbContext>(), ProcessedIntegrationEventsTable, IdempotencyKeyPropertyName),
        new("Notification", ModelOnly<NotificationDbContext>(), NotificationIntentsTable, DedupeKeyPropertyName),
    ];

    [Fact]
    public void Every_declared_platform_carrier_column_resolves()
    {
        var columns = PlatformCarrierColumns();
        try
        {
            var unresolved = columns
                .Where(column => column.Resolve() is null)
                .Select(column => column.ToString())
                .ToArray();

            Assert.True(
                columns.Length > 0 && unresolved.Length == 0,
                $"承载列登记表解析失败（{unresolved.Length}）：{string.Join(", ", unresolved)}");
        }
        finally
        {
            Dispose(columns);
        }
    }

    [Fact]
    public void Budget_equals_the_narrowest_platform_carrier_column()
    {
        var (narrowest, readings) = NarrowestCarrierWidth();

        Assert.True(
            narrowest == IntegrationEventIdempotencyKey.Budget,
            $"平台预算 Budget = {IntegrationEventIdempotencyKey.Budget}，而最窄承载列 = {narrowest}，两侧已漂移。逐列读数：{readings}");
    }

    /// <summary>
    /// 最坏情况：每一段都取到 producer 自己 EF 模型里那一列的上限，**跑真 converter**，
    /// 产出的信封键必须装得进最窄承载列。
    /// </summary>
    [Theory]
    [MemberData(nameof(ConvertedProducerNames))]
    public void Worst_case_converted_producer_keys_fit_the_narrowest_platform_carrier_column(string producer)
    {
        var (narrowest, _) = NarrowestCarrierWidth();
        var reading = ConvertedProducerKeys()[producer]();

        Assert.True(
            reading.ComposedKey.Length <= narrowest,
            $"{producer} 最坏信封键长度 {reading.ComposedKey.Length} > 最窄承载列 {narrowest}：{reading.ComposedKey}");
    }

    /// <summary>
    /// 改动前的形状留一条回归读数：**纯拼接**在同一组最坏输入下超过最窄承载列。
    /// 这条不是断言实现，是把「为什么必须回落」钉在可执行的读数上——
    /// 哪天上游把段收窄到纯拼接也装得下了，这条会红，届时回落分支是否还需要要重新评估。
    /// </summary>
    [Theory]
    [MemberData(nameof(ConvertedProducerNames))]
    public void Plain_concatenation_of_the_same_worst_case_input_would_not_fit(string producer)
    {
        var (narrowest, _) = NarrowestCarrierWidth();
        var reading = ConvertedProducerKeys()[producer]();

        Assert.True(
            reading.PlainConcatenationLength > narrowest,
            $"{producer} 纯拼接最坏长度 {reading.PlainConcatenationLength} 已不超过最窄承载列 {narrowest}，本票对该 producer 的前提需要重新评估。");
    }

    /// <summary>
    /// 回落形态的长度只由前缀与摘要决定，与输入多长无关；这条把那个上界也钉在承载列上。
    /// </summary>
    [Fact]
    public void Fallback_shape_fits_the_narrowest_platform_carrier_column_for_every_converted_producer()
    {
        var (narrowest, _) = NarrowestCarrierWidth();

        foreach (var (producer, factory) in ConvertedProducerKeys())
        {
            var reading = factory();
            Assert.True(
                IntegrationEventIdempotencyKey.IsDigested(reading.Prefix, reading.ComposedKey),
                $"{producer} 在最坏输入下没有走回落分支，本条读数会退化成空转：{reading.ComposedKey}");
            Assert.True(
                reading.Prefix.Length + IntegrationEventIdempotencyKey.DigestLength <= narrowest,
                $"{producer} 回落形态需要 {reading.Prefix.Length + IntegrationEventIdempotencyKey.DigestLength} 字符，最窄承载列只放得下 {narrowest}。");
        }
    }

    public static TheoryData<string> ConvertedProducerNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ConvertedProducerKeys().Keys)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// 本 PR 已接入平台预算的四个 producer，各取自己**最长的那条**信封键。
    /// 每个入口都跑真 converter，因此「接线了」与「走通了」是同一条读数
    /// （grep 到调用方只证明有代码，证不到这条路径真的产出这把键）。
    /// </summary>
    private static Dictionary<string, Func<ProducerKeyReading>> ConvertedProducerKeys() => new(StringComparer.Ordinal)
    {
        ["IndustrialTelemetry.production-count"] = IndustrialTelemetryProductionCountKey,
        ["Wms.wcs-retry-exhausted"] = WmsRetryExhaustedKey,
        ["AppHub.connector-host-unreachable"] = AppHubConnectorHostUnreachableKey,
        ["Inventory.stock-movement-posted"] = InventoryStockMovementPostedKey,
        ["Approval.step-resolved"] = ApprovalStepResolvedKey,
        ["Mes.production-consumption"] = MesProductionConsumptionKey,
        ["ProductEngineering.production-version-created"] = ProductEngineeringProductionVersionCreatedKey,
        ["Quality.inspection-conditional-release"] = QualityInspectionConditionalReleaseKey,
        ["Scheduling.schedule-plan-invalidated"] = SchedulingSchedulePlanInvalidatedKey,
    };

    private static ProducerKeyReading IndustrialTelemetryProductionCountKey()
    {
        using var model = ModelOnly<IndustrialTelemetryDbContext>();
        var widest = Saturate(
            model,
            typeof(TelemetrySummary),
            nameof(TelemetrySummary.OrganizationId),
            nameof(TelemetrySummary.EnvironmentId),
            nameof(TelemetrySummary.DeviceAssetId),
            nameof(TelemetrySummary.TagKey),
            nameof(TelemetrySummary.SourceSystem),
            nameof(TelemetrySummary.SourceConnector),
            nameof(TelemetrySummary.SourceSequence));

        var summary = TelemetrySummary.Record(
            widest[0],
            widest[1],
            widest[2],
            widest[3],
            DateTimeOffset.Parse("2026-09-11T08:00:00Z"),
            DateTimeOffset.Parse("2026-09-11T08:01:00Z"),
            1,
            1m,
            1m,
            1m,
            widest[6],
            widest[4],
            widest[5]);

        var integrationEvent = new TelemetryProductionCountDeltaIntegrationEventConverter()
            .Convert(new TelemetryProductionCountDeltaDomainEvent(summary, 1m, "posted", HasActiveAlarm: false));

        const string prefix = "industrialTelemetry:production-count:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widest[2], widest[3], widest[4], widest[5], widest[6]));
    }

    private static ProducerKeyReading WmsRetryExhaustedKey()
    {
        using var model = ModelOnly<WmsDbContext>();
        var widest = Saturate(
            model,
            typeof(WcsTask),
            "OrganizationId",
            "EnvironmentId",
            nameof(WcsTask.AdapterType),
            nameof(WcsTask.DeviceId),
            nameof(WcsTask.ExternalTaskId));

        var task = WcsTask.Dispatch(
            widest[0],
            widest[1],
            new WarehouseTaskId(Guid.CreateVersion7()),
            widest[2],
            widest[4],
            "{}",
            widest[3]);

        var integrationEvent = new WcsTaskRetryExhaustedIntegrationEventConverter()
            .Convert(new WmsDomainEvents.WcsTaskRetryExhaustedDomainEvent(task));

        const string prefix = "wms:wcs-retry-exhausted:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widest[2], widest[3], widest[4]));
    }

    private static ProducerKeyReading AppHubConnectorHostUnreachableKey()
    {
        using var model = ModelOnly<AppHubDbContext>();
        var widest = Saturate(
            model,
            typeof(ApplicationInstance),
            "OrganizationId",
            "EnvironmentId",
            "ConnectorHostId",
            "InstanceKey");

        var detectedAtUtc = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var integrationEvent = new ConnectorHostUnreachableIntegrationEventConverter().Convert(
            new ConnectorHostUnreachableDomainEvent(
                widest[0],
                widest[1],
                widest[2],
                widest[3],
                detectedAtUtc.AddMinutes(-5),
                detectedAtUtc,
                TimeSpan.FromMinutes(5)));

        const string prefix = "apphub:connector-host-unreachable:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widest[2], widest[3], detectedAtUtc.ToString("O")));
    }

    private static ProducerKeyReading InventoryStockMovementPostedKey()
    {
        using var model = ModelOnly<InventoryDbContext>();
        var widest = Saturate(
            model,
            typeof(StockMovement),
            nameof(StockMovement.OrganizationId),
            nameof(StockMovement.EnvironmentId),
            nameof(StockMovement.SourceService),
            nameof(StockMovement.SourceDocumentId),
            nameof(StockMovement.IdempotencyKey));

        var movement = StockMovement.Post(
            widest[0],
            widest[1],
            "inbound",
            widest[2],
            widest[3],
            null,
            widest[4],
            "SKU-001",
            "ea",
            "SITE-001",
            "WH-01",
            null,
            null,
            "unrestricted",
            "company",
            null,
            1m);
        AssignStockMovementId(movement);

        var integrationEvent = new StockMovementPostedIntegrationEventConverter(new StubInventoryContextAccessor())
            .Convert(new InventoryDomainEvents.StockMovementPostedDomainEvent(movement));

        const string prefix = "inventory:stock-movement-posted:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widest[2], widest[3], widest[4]));
    }

    /// <summary>
    /// 改动前那条纯拼接的长度：<c>{前缀}{各段以 ':' 相连}</c>。
    /// **不手抄数字**——各段长度由调用方从 EF 模型饱和后传进来。
    /// </summary>
    private static int PlainLength(string prefix, params string[] parts) =>
        prefix.Length + string.Join(IntegrationEventIdempotencyKey.ReadableSeparator, parts).Length;

    private static string[] Saturate(DbContext context, Type entityType, params string[] propertyNames)
    {
        var entity = context.Model.FindEntityType(entityType);
        Assert.True(entity is not null, $"解析不到实体类型 {entityType.Name}。");

        return propertyNames
            .Select(name =>
            {
                var width = entity!.FindProperty(name)?.GetMaxLength();
                Assert.True(width is > 0, $"{entityType.Name}.{name} 解析不到列宽，最坏情况会退化成空转。");
                return new string('w', width!.Value);
            })
            .ToArray();
    }

    private static (int Narrowest, string Readings) NarrowestCarrierWidth()
    {
        var columns = PlatformCarrierColumns();
        try
        {
            var widths = columns
                .Select(column => (column.ToString(), Width: column.Resolve()))
                .ToArray();

            Assert.True(
                widths.All(x => x.Width is > 0),
                $"承载列解析失败：{string.Join(", ", widths.Where(x => x.Width is not > 0).Select(x => x.Item1))}");

            return (
                widths.Min(x => x.Width!.Value),
                string.Join(", ", widths.Select(x => $"{x.Item1}={x.Width}")));
        }
        finally
        {
            Dispose(columns);
        }
    }

    private static void Dispose(IEnumerable<CarrierColumn> columns)
    {
        foreach (var column in columns)
        {
            column.Context.Dispose();
        }
    }

    private static void AssignStockMovementId(StockMovement movement)
    {
        var setter = typeof(StockMovement)
            .GetProperty(nameof(StockMovement.Id))?
            .GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("StockMovement.Id setter was not found.");
        setter.Invoke(movement, [new StockMovementId(Guid.CreateVersion7())]);
    }

    private sealed record ProducerKeyReading(string Prefix, string ComposedKey, int PlainConcatenationLength);

    /// <summary>
    /// 按**物理表名**而不是实体 CLR 类型名解析，因为九个服务各自有一份同名实体类型；
    /// 表名是这一列在数据库里的身份。同名表出现两次也红（闭集枚举）。
    /// </summary>
    private sealed record CarrierColumn(string Service, DbContext Context, string TableName, string PropertyName)
    {
        public int? Resolve()
        {
            var entities = Context.Model.GetEntityTypes()
                .Where(entity => string.Equals(entity.GetTableName(), TableName, StringComparison.Ordinal))
                .ToArray();

            return entities.Length == 1
                ? entities[0].FindProperty(PropertyName)?.GetMaxLength()
                : null;
        }

        public override string ToString() => $"{Service}.{TableName}.{PropertyName}";
    }

    private static DbContext ModelOnly<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=127.0.0.1;Database=nerv_iip_envelope_idempotency_budget_contract;Username=nerv;Password=nerv")
            .Options;
        return (DbContext)Activator.CreateInstance(typeof(TContext), options, NullMediator.Instance)!;
    }


    // ---- #3370：第 1 问判定为 🔴 的五个 producer，各取自己最长的那条信封键 ----
    // 判定口径与上面四个完全相同：段长从**各自 EF 模型**饱和读出、跑**真 converter**、
    // 与最窄承载列对撞。判定为 🟢 的 producer（DemandPlanning / Erp / MasterData /
    // BarcodeLabel / Maintenance / Ops）**故意不登记**——它们没有接入，
    // 登记进来会让 Plain_concatenation_of_the_same_worst_case_input_would_not_fit 这条读数失去意义。

    private static ProducerKeyReading ApprovalStepResolvedKey()
    {
        using var model = ModelOnly<ApprovalDbContext>();
        var chainWidest = Saturate(model, typeof(ApprovalChain), "OrganizationId", "EnvironmentId");
        var stepWidest = Saturate(model, typeof(ApprovalStep), "ApproverType", "ApproverRef");
        var decisionWidest = Saturate(
            model,
            typeof(ApprovalDecision),
            "ActorType",
            "ActorRef",
            "OnBehalfOfActorType",
            "OnBehalfOfActorRef",
            "Decision");

        var template = ApprovalTemplate.Create(
            chainWidest[0],
            chainWidest[1],
            "tpl",
            "doc",
            1,
            true,
            [new ApprovalTemplateStepDefinition(1, "step", null, stepWidest[0], stepWidest[1], null)]);
        var chain = ApprovalChain.Start(
            template,
            new ApprovalDocumentReference("svc", "doc", "doc-001", null),
            "system:test");
        var decision = chain.ResolveStep(
            1,
            decisionWidest[0],
            decisionWidest[1],
            ApprovalDecisions.Approve,
            null,
            decisionWidest[2],
            decisionWidest[3]);
        var step = chain.Steps.Single(x => x.StepNo == 1);

        var integrationEvent = new ApprovalStepResolvedIntegrationEventConverter()
            .Convert(new ApprovalDomainEvents.ApprovalStepResolvedDomainEvent(chain, step, decision));

        const string prefix = "business-approval:step-resolved:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(
                prefix,
                chainWidest[0],
                chainWidest[1],
                chain.Id.ToString(),
                decision.RoundNo.ToString(CultureInfo.InvariantCulture),
                decision.StepNo.ToString(CultureInfo.InvariantCulture),
                decisionWidest[0],
                decisionWidest[1],
                decisionWidest[2],
                decisionWidest[3]));
    }

    private static ProducerKeyReading MesProductionConsumptionKey()
    {
        using var model = ModelOnly<MesDbContext>();
        var widest = Saturate(
            model,
            typeof(ProductionReportMaterialConsumption),
            nameof(ProductionReportMaterialConsumption.OrganizationId),
            nameof(ProductionReportMaterialConsumption.EnvironmentId),
            nameof(ProductionReportMaterialConsumption.ReportNo),
            nameof(ProductionReportMaterialConsumption.MaterialIssueRequestNo),
            nameof(ProductionReportMaterialConsumption.MaterialId),
            nameof(ProductionReportMaterialConsumption.MaterialLotId));

        var consumption = ProductionReportMaterialConsumption.Record(
            widest[0],
            widest[1],
            widest[2],
            "WO-001",
            "OP-001",
            widest[4],
            widest[5],
            "ea",
            1m,
            widest[3],
            "SITE-001",
            "WH-01");

        var integrationEvent = new ProductionMaterialConsumedIntegrationEventConverter()
            .Convert(new MesDomainEvents.ProductionMaterialConsumedDomainEvent(consumption));

        // Mes 那条 EventIds.Idempotency 会先滤掉空白段再交给 Compose，段数是动态的，
        // 因此它用的是 Compose("mes:", ...) 而不是 ComposeServiceScoped——回落前缀只有服务段。
        const string prefix = "mes:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, "production-consumption", widest[0], widest[1], widest[2], widest[3], widest[4], widest[5]));
    }

    private static ProducerKeyReading ProductEngineeringProductionVersionCreatedKey()
    {
        using var model = ModelOnly<ProductEngineeringDbContext>();
        var widest = Saturate(
            model,
            typeof(ProductionVersion),
            nameof(ProductionVersion.OrganizationId),
            nameof(ProductionVersion.EnvironmentId),
            nameof(ProductionVersion.SkuCode),
            nameof(ProductionVersion.MbomVersionId),
            nameof(ProductionVersion.RoutingVersionId));

        var version = ProductionVersion.Create(
            widest[0],
            widest[1],
            widest[2],
            widest[3],
            widest[4],
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            1,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);

        var integrationEvent = new ProductionVersionCreatedIntegrationEventConverter(new StubProductEngineeringContextAccessor())
            .Convert(new ProductEngineeringDomainEvents.ProductionVersionCreatedDomainEvent(version));

        const string prefix = "product-engineering:production-version-created:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widest[2], widest[3], widest[4]));
    }

    private static ProducerKeyReading QualityInspectionConditionalReleaseKey()
    {
        using var model = ModelOnly<QualityDbContext>();
        var widest = Saturate(
            model,
            typeof(InspectionRecord),
            nameof(InspectionRecord.OrganizationId),
            nameof(InspectionRecord.EnvironmentId),
            nameof(InspectionRecord.SourceDocumentId));

        // ⚠️ SourceService **不能**按 EF 列宽（100）饱和：域构造器按闭集 QualityInspectionSourceServices.All
        // 校验，列宽在这条轴上不是有效上界（#3281 判据的反向一例——真上界取「列宽 ∧ 值域」的更小者）。
        // 这一段从词表实时取最长成员，不手抄。
        var widestSourceService = QualityInspectionSourceServices.All
            .MaxBy(value => value.Length)!;

        var record = InspectionRecord.Create(
            widest[0],
            widest[1],
            null,
            QualityInspectionSourceTypes.Receiving,
            widestSourceService,
            widest[2],
            null,
            "SKU-001",
            1m,
            null,
            null,
            [InspectionResultLineInput.Pass("CH-001", "ok", null, [])],
            null,
            []);

        var integrationEvent = new InspectionConditionalReleasedIntegrationEventConverter(new StubQualityContextAccessor())
            .Convert(new QualityDomainEvents.InspectionConditionalReleasedDomainEvent(record));

        const string prefix = "quality:inspection-conditional-release:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widestSourceService, widest[2], record.Id.ToString()));
    }

    private static ProducerKeyReading SchedulingSchedulePlanInvalidatedKey()
    {
        using var model = ModelOnly<SchedulingDbContext>();
        var widest = Saturate(
            model,
            typeof(SchedulePlanInvalidation),
            nameof(SchedulePlanInvalidation.OrganizationId),
            nameof(SchedulePlanInvalidation.EnvironmentId),
            nameof(SchedulePlanInvalidation.PlanId),
            nameof(SchedulePlanInvalidation.SourceEventId));

        var occurredAtUtc = DateTimeOffset.Parse("2026-09-12T08:00:00Z", CultureInfo.InvariantCulture);
        var invalidation = SchedulePlanInvalidation.Create(
            widest[0],
            widest[1],
            widest[2],
            widest[3],
            "mes.WorkOrderReleased",
            "business-mes",
            "upstream-change",
            null,
            null,
            null,
            null,
            occurredAtUtc,
            occurredAtUtc);
        var snapshot = new SchedulePlanInvalidatedSnapshot(
            widest[2],
            "problem-001",
            1,
            "v1",
            "fingerprint",
            SchedulePlanLifecycleStatus.Released,
            []);

        var integrationEvent = new SchedulePlanInvalidatedIntegrationEventConverter(
                TimeProvider.System,
                new StubSchedulingContextAccessor())
            .Convert(new SchedulingDomainEvents.SchedulePlanInvalidatedDomainEvent(invalidation, snapshot));

        const string prefix = "scheduling:schedule-plan-invalidated:";
        return new ProducerKeyReading(
            prefix,
            integrationEvent.IdempotencyKey,
            PlainLength(prefix, widest[0], widest[1], widest[2], widest[3]));
    }

    private sealed class StubProductEngineeringContextAccessor : IProductEngineeringIntegrationEventContextAccessor
    {
        public ProductEngineeringIntegrationEventContext GetContext() => new("corr-001", "cause-001", "system:test");
    }

    private sealed class StubQualityContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() => new("corr-001", "cause-001", "system:test");
    }

    private sealed class StubSchedulingContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext() => new("corr-001", "cause-001", "system:test");
    }

    private sealed class StubInventoryContextAccessor : IInventoryIntegrationEventContextAccessor
    {
        public InventoryIntegrationEventContext GetContext() => new("corr-001", "cause-001", "system:test");
    }

    private sealed class NullMediator : IMediator
    {
        public static readonly NullMediator Instance = new();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new NotSupportedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
