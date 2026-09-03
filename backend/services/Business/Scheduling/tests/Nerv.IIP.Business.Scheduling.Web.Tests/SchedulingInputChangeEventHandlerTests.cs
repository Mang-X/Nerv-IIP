using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingInputChangeEventHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Maintenance_asset_unavailable_event_invalidates_generated_plan_for_affected_resource()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = CreateAssetUnavailableHandler(scope.ServiceProvider);

        await handler.HandleAsync(CreateAssetUnavailableEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalidation.ReasonCode);
            Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
            Assert.Equal("maintenance.AssetUnavailable", invalidation.SourceEventType);
            Assert.Equal(FixedNow, invalidation.RecordedAtUtc);
        });
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Maintenance_asset_unavailable_v1_and_v2_share_business_identity_and_invalidate_once(bool v1First)
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var v1 = CreateAssetUnavailableHandler(scope.ServiceProvider);
        var v2 = CreateAssetUnavailableV2Handler(scope.ServiceProvider);
        const string sharedKey = "asset-unavailable:wo-maint-001:2026-06-01T09:00:00.0000000+00:00";

        var first = v1First
            ? v1.HandleAsync(CreateAssetUnavailableEvent() with { IdempotencyKey = sharedKey }, CancellationToken.None)
            : v2.HandleAsync(CreateAssetUnavailableV2Event(sharedKey), CancellationToken.None);
        await first;
        var second = v1First
            ? v2.HandleAsync(CreateAssetUnavailableV2Event(sharedKey), CancellationToken.None)
            : v1.HandleAsync(CreateAssetUnavailableEvent() with { IdempotencyKey = sharedKey }, CancellationToken.None);
        await second;

        Assert.Equal(2, await db.SchedulePlanInvalidations.CountAsync());
        Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Empty(await ListAssetUnavailableDeadLettersAsync(scope.ServiceProvider));
    }

    /// <summary>
    /// 双身份去重断言的鉴别力自证（反向探针）：同一业务键下，只有版本合法的投递才会进入 claim。
    /// v1 envelope 把 EventVersion 换成 2，消费守卫必须把它送进 DLQ（<c>unsupported-version</c>）而不是
    /// 当作已处理的重复静默吞掉；v2 的 wire 契约在反序列化时就拒绝 eventVersion 不为 2 的 envelope，
    /// 这样的投递在到达 handler 之前已进入 CAP 既有失败路径。最后换了业务键与事件实例的投递必须真的
    /// 产生第二条 inbox 记录，证明前面的 <c>Single</c> 断言不是恒真。
    /// </summary>
    [Fact]
    public async Task Maintenance_asset_unavailable_version_mutation_is_rejected_instead_of_being_deduplicated()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var v1 = CreateAssetUnavailableHandler(scope.ServiceProvider);
        var v2 = CreateAssetUnavailableV2Handler(scope.ServiceProvider);
        const string sharedKey = "asset-unavailable:wo-maint-001:2026-06-01T09:00:00.0000000+00:00";

        await v2.HandleAsync(CreateAssetUnavailableV2Event(sharedKey), CancellationToken.None);
        Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());

        await v1.HandleAsync(CreateAssetUnavailableEvent() with { IdempotencyKey = sharedKey, EventVersion = MaintenanceIntegrationEventVersions.V2 }, CancellationToken.None);
        var deadLetter = Assert.Single(await ListAssetUnavailableDeadLettersAsync(scope.ServiceProvider));
        Assert.Equal(IntegrationEventEnvelopeValidator.UnsupportedVersionFailureCode, deadLetter.FailureCode);

        var v2WireJson = JsonSerializer.Serialize(CreateAssetUnavailableV2Event(sharedKey), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var mutatedWireJson = v2WireJson.Replace("\"eventVersion\":2", "\"eventVersion\":1", StringComparison.Ordinal);
        Assert.NotEqual(v2WireJson, mutatedWireJson);
        var wireRejection = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(mutatedWireJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Contains("eventVersion 2", wireRejection.Message, StringComparison.Ordinal);

        Assert.Single(await db.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Equal(2, await db.SchedulePlanInvalidations.CountAsync());

        await v1.HandleAsync(CreateAssetUnavailableEvent() with { EventId = "evt-maint-002", IdempotencyKey = "asset-unavailable:wo-maint-002" }, CancellationToken.None);
        Assert.Equal(2, (await db.ProcessedIntegrationEvents.ToArrayAsync()).Length);
        Assert.Equal(4, await db.SchedulePlanInvalidations.CountAsync());
    }

    /// <summary>
    /// v1 与 v2 handler 必须共用同一个注册的 <see cref="IAssetUnavailableCanonicalProcessor"/>：
    /// 用 <c>services.Replace</c> 换成计数装饰器后，两个版本的投递都必须经过它。
    /// </summary>
    [Fact]
    public async Task Maintenance_asset_unavailable_v1_and_v2_handlers_dispatch_through_the_registered_processor_seam()
    {
        var counting = new List<CountingAssetUnavailableProcessor>();
        await using var provider = CreateInMemoryProvider(services =>
            services.Replace(ServiceDescriptor.Scoped<IAssetUnavailableCanonicalProcessor>(serviceProvider =>
            {
                var processor = new CountingAssetUnavailableProcessor(serviceProvider.GetRequiredService<AssetUnavailableCanonicalProcessor>());
                counting.Add(processor);
                return processor;
            })));
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var v1 = CreateAssetUnavailableHandler(scope.ServiceProvider);
        var v2 = CreateAssetUnavailableV2Handler(scope.ServiceProvider);
        const string sharedKey = "asset-unavailable:wo-maint-001:2026-06-01T09:00:00.0000000+00:00";

        await v1.HandleAsync(CreateAssetUnavailableEvent() with { IdempotencyKey = sharedKey }, CancellationToken.None);
        await v2.HandleAsync(CreateAssetUnavailableV2Event(sharedKey), CancellationToken.None);

        var seam = Assert.Single(counting);
        Assert.Equal(
            [MaintenanceIntegrationEventVersions.V1, MaintenanceIntegrationEventVersions.V2],
            seam.Inputs.Select(x => x.Envelope.EventVersion));
        Assert.All(seam.Inputs, input => Assert.Equal("breakdown", input.UpstreamReason));
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToArrayAsync());
    }

    /// <summary>
    /// #1320 命中回证：维护世界发出的 deviceAssetId 是**设备业务编码**（DEV-CNC-01），
    /// 计划失效必须靠它命中已生成方案。修复前排程侧的 resourceId 存的是 MasterData 聚合主键 GUID，
    /// 这条链从来命中不了——设备停机不会让在途方案失效，排产员拿着过期方案照发。
    /// 这里两侧都走真实解析：方案资源标识由 <see cref="SchedulingDeviceAssetKey"/> 从
    /// (业务编码, GUID 主键) 解析而来，事件带同一个业务编码。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Maintenance_asset_events_invalidate_plans_matched_by_device_business_code(bool unavailable)
    {
        const string DeviceCode = "DEV-CNC-01";
        const string DeviceAggregateId = "0198f0aa-1111-7000-8000-000000000001";
        // 排程问题构造侧的真实口径：有业务编码就用业务编码。
        var planResourceId = SchedulingDeviceAssetKey.Resolve(DeviceCode, DeviceAggregateId);
        Assert.Equal(DeviceCode, planResourceId);

        await using var provider = CreateInMemoryProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seedDbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedDbContext.SchedulePlans.Add(CreatePlan(
                "plan-device-code",
                SchedulePlanStatusContract.Generated,
                "org-001",
                "env-dev",
                resourceId: planResourceId));
            // 对照：修复前的形状（资源标识存 GUID 主键）。同一事件对它无能为力。
            seedDbContext.SchedulePlans.Add(CreatePlan(
                "plan-legacy-guid",
                SchedulePlanStatusContract.Generated,
                "org-001",
                "env-dev",
                problemId: "problem-legacy",
                resourceId: DeviceAggregateId));
            await seedDbContext.SaveChangesAsync();
        }

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        // 维护世界只持有业务编码，两个事件都用它。
        if (unavailable)
        {
            await CreateAssetUnavailableHandler(scope.ServiceProvider)
                .HandleAsync(
                    CreateAssetUnavailableEvent() with
                    {
                        Payload = new AssetUnavailablePayload(
                            DeviceCode,
                            "breakdown",
                            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero))
                    },
                    CancellationToken.None);
        }
        else
        {
            await new AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans(
                    dbContext,
                    new InMemoryIntegrationEventDeadLetterStore(),
                    sender,
                    new RecordingLogger<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans>())
                .HandleAsync(
                    CreateAssetRestoredEvent() with
                    {
                        Payload = new AssetRestoredPayload(
                            DeviceCode,
                            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero))
                    },
                    CancellationToken.None);
        }

        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-device-code", invalidation.PlanId);
        Assert.Equal(DeviceCode, invalidation.AffectedResourceId);
        Assert.Equal(
            unavailable
                ? SchedulingPlanInvalidationReasons.EquipmentUnavailable
                : SchedulingPlanInvalidationReasons.EquipmentRestored,
            invalidation.ReasonCode);
        Assert.Single(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>());
    }

    [Fact]
    public async Task Maintenance_asset_unavailable_event_rejects_blank_device_asset_id()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = CreateAssetUnavailableHandler(scope.ServiceProvider);
        var integrationEvent = CreateAssetUnavailableEvent() with
        {
            Payload = new AssetUnavailablePayload(" ", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero))
        };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(integrationEvent, CancellationToken.None));

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
    }

    [Fact]
    public async Task Maintenance_asset_unavailable_v2_with_wrong_source_is_dead_lettered_before_claim()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var handler = CreateAssetUnavailableV2Handler(scope.ServiceProvider);

        await handler.HandleAsync(CreateAssetUnavailableV2Event("key-wrong-source") with
        {
            SourceService = MaintenanceIntegrationEventSources.Maintenance
        }, CancellationToken.None);

        var deadLetter = Assert.Single(await ListAssetUnavailableDeadLettersAsync(scope.ServiceProvider));
        Assert.Equal("unexpected-source-service", deadLetter.FailureCode);
        using var eventJson = JsonDocument.Parse(deadLetter.EventJson);
        var root = eventJson.RootElement;
        Assert.Equal("evt-maint-v2-001", root.GetProperty("eventId").GetString());
        Assert.Equal(MaintenanceIntegrationEventVersions.V2, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal("key-wrong-source", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal("org-001", root.GetProperty("organizationId").GetString());
        Assert.Equal("ASSET-CNC-01", root.GetProperty("payload").GetProperty("deviceAssetId").GetString());
        Assert.Equal("breakdown", root.GetProperty("payload").GetProperty("reasonCode").GetString());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToArrayAsync());
    }

    /// <summary>
    /// replay 回环：DLQ 里保存的完整 v2 envelope 必须能被 replay handler 用契约 converter 反序列化回
    /// <see cref="AssetUnavailableV2IntegrationEvent"/>、按当前环境的 v2 canonical topic 重新发布，且重入的
    /// envelope 与原投递逐字段相等；v1 行按 legacy alias 重入。
    /// </summary>
    [Fact]
    public async Task Maintenance_asset_unavailable_dead_letters_replay_the_original_envelope_through_cap()
    {
        var v2Original = CreateAssetUnavailableV2Event("key-replay") with { CausationId = "cause-maint-v2-001" };
        var v1Original = CreateAssetUnavailableEvent();
        var v2DeadLetter = IntegrationEventDeadLetterMessage.Create(
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            v2Original,
            IntegrationEventCapFailureDeadLetterer.HandlerRetryExhaustedFailureCode,
            "probe");
        var v1DeadLetter = IntegrationEventDeadLetterMessage.Create(
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            v1Original,
            IntegrationEventCapFailureDeadLetterer.HandlerRetryExhaustedFailureCode,
            "probe");
        var publisher = new RecordingCapPublisher();
        var replay = new SchedulingAssetUnavailableDeadLetterReplayHandler(
            publisher,
            new HostingEnvironment { EnvironmentName = "ReplayProbe" });

        Assert.True(replay.CanReplay(v2DeadLetter));
        Assert.True(replay.CanReplay(v1DeadLetter));
        await replay.ReplayAsync(v2DeadLetter, CancellationToken.None);
        await replay.ReplayAsync(v1DeadLetter, CancellationToken.None);

        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(AssetUnavailableIntegrationEventTopics.V2("ReplayProbe"), publisher.Published[0].Name);
        Assert.Equal(v2Original, Assert.IsType<AssetUnavailableV2IntegrationEvent>(publisher.Published[0].Content));
        Assert.Equal(AssetUnavailableIntegrationEventTopics.V1LegacyAlias, publisher.Published[1].Name);
        Assert.Equal(v1Original, Assert.IsType<AssetUnavailableIntegrationEvent>(publisher.Published[1].Content));
    }

    /// <summary>
    /// 错误 source 那条 DLQ 的 <c>EventJson</c> 是完整的 v2 envelope，但 v2 wire 契约在反序列化时就拒绝
    /// 非 business-maintenance 的 source service——这样的 envelope 在生产中到不了消费者，重入也必然再次失败。
    /// replay handler 必须以可操作的契约原因拒绝，执行器把该行记为 replay 失败并保留原行，且不向 broker 发布任何消息。
    /// </summary>
    [Fact]
    public async Task Maintenance_asset_unavailable_v2_wrong_source_dead_letter_is_refused_on_replay_with_the_contract_reason()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var handler = CreateAssetUnavailableV2Handler(scope.ServiceProvider);
        await handler.HandleAsync(CreateAssetUnavailableV2Event("key-wrong-source") with
        {
            SourceService = MaintenanceIntegrationEventSources.Maintenance
        }, CancellationToken.None);
        var deadLetter = Assert.Single(await ListAssetUnavailableDeadLettersAsync(scope.ServiceProvider));

        var publisher = new RecordingCapPublisher();
        var replay = new SchedulingAssetUnavailableDeadLetterReplayHandler(
            publisher,
            new HostingEnvironment { EnvironmentName = "ReplayProbe" });
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var executor = new IntegrationEventDeadLetterReplayExecutor(store, [replay], TimeProvider.System);

        var result = await executor.ReplayAsync(deadLetter.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Message);
        Assert.Contains("business-maintenance source service", result.Message, StringComparison.Ordinal);
        Assert.Contains(deadLetter.Id.ToString(), result.Message, StringComparison.Ordinal);
        Assert.Empty(publisher.Published);
        var failed = await store.GetAsync(deadLetter.Id, CancellationToken.None);
        Assert.NotNull(failed);
        Assert.Equal(IntegrationEventDeadLetterStatus.Failed, failed.Status);
        Assert.Equal("replay-handler-failed", failed.FailureCode);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Maintenance_asset_unavailable_event_logs_when_resource_mapping_matches_no_generated_plan()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<RecordingLogger<AssetUnavailableCanonicalProcessor>>();
        var handler = CreateAssetUnavailableHandler(scope.ServiceProvider);
        var integrationEvent = CreateAssetUnavailableEvent() with
        {
            Payload = new AssetUnavailablePayload("ASSET-NOT-MAPPED", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero))
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Contains(logger.Messages, x =>
            x.LogLevel == LogLevel.Information &&
            x.Message.Contains("ASSET-NOT-MAPPED", StringComparison.Ordinal) &&
            x.Message.Contains("matched no schedule plan", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>().Published);
    }

    [Fact]
    public async Task Maintenance_asset_restored_event_invalidates_generated_plan_for_affected_resource()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans>());

        await handler.HandleAsync(CreateAssetRestoredEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentRestored, invalidation.ReasonCode);
            Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
            Assert.Equal("maintenance.AssetRestored", invalidation.SourceEventType);
        });
    }

    [Fact]
    public async Task IndustrialTelemetry_device_state_changed_event_invalidates_generated_or_released_plans_for_affected_device_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateDeviceStateChangedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.DeviceStateChanged, invalidation.ReasonCode);
            Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
            Assert.Equal(IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged, invalidation.SourceEventType);
            Assert.Equal(FixedNow, invalidation.RecordedAtUtc);
        });
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>().Count());

        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Equal(DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName, processed.ConsumerName);
        Assert.Equal("industrialTelemetry:device-state:org-001:env-dev:ASSET-CNC-01:state-seq-009:state-snapshot-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Stock_availability_changed_event_invalidates_generated_plans_in_same_business_scope()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());

        await handler.HandleAsync(CreateStockAvailabilityChangedEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.MaterialReadinessChanged, invalidation.ReasonCode);
            Assert.Equal("SKU-001", invalidation.AffectedSkuCode);
            Assert.Equal("inventory.StockAvailabilityChanged", invalidation.SourceEventType);
        });
    }

    [Theory]
    [InlineData(QualityIntegrationEventTypes.InspectionRejected, SchedulingPlanInvalidationReasons.QualityBlocked)]
    [InlineData(QualityIntegrationEventTypes.InspectionPassed, SchedulingPlanInvalidationReasons.QualityReleased)]
    public async Task Quality_inspection_event_invalidates_generated_plan_for_affected_work_order(string eventType, string expectedReason)
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());

        await handler.HandleAsync(CreateInspectionEvent(eventType), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(expectedReason, invalidation.ReasonCode);
            Assert.Equal("WO-001", invalidation.AffectedWorkOrderId);
            Assert.Equal(eventType, invalidation.SourceEventType);
        });
    }

    [Fact]
    public async Task Quality_inspection_event_for_operation_source_publishes_only_affected_operation()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());

        await handler.HandleAsync(
            CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, sourceDocumentId: "OP-002"),
            CancellationToken.None);

        var invalidations = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .SchedulePlanInvalidations
            .OrderBy(x => x.PlanId)
            .ToArrayAsync();
        Assert.Equal(2, invalidations.Length);
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal("OP-002", invalidation.AffectedOperationId);
            Assert.Null(invalidation.AffectedWorkOrderId);
            Assert.Contains("OP-002", invalidation.SourceEventId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Quality_inspection_event_ignores_non_mes_source_service()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());
        var integrationEvent = CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected) with
        {
            Payload = CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected).Payload with
            {
                SourceService = QualityIntegrationEventSources.BusinessQuality
            }
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
    }

    [Fact]
    public async Task Mes_work_order_released_event_invalidates_generated_plans_in_same_business_scope_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());
        var integrationEvent = CreateWorkOrderReleasedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.WorkOrderReleased, invalidation.ReasonCode);
            Assert.Equal("WO-NEW", invalidation.AffectedWorkOrderId);
            Assert.Equal("mes.WorkOrderReleased", invalidation.SourceEventType);
        });
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task MasterData_work_calendar_changed_event_invalidates_only_generated_plans_using_the_calendar_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateWorkCalendarChangedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-calendar-target", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.WorkCalendarChanged, invalidation.ReasonCode);
        Assert.Equal(MasterDataIntegrationEventTypes.WorkCalendarChanged, invalidation.SourceEventType);

        var plans = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            new ListSchedulePlansQuery("org-001", "env-dev"),
            CancellationToken.None);
        Assert.True(plans.Single(x => x.PlanId == "plan-calendar-target").IsInvalidated);
        Assert.False(plans.Single(x => x.PlanId == "plan-calendar-released").IsInvalidated);
        Assert.False(plans.Single(x => x.PlanId == "plan-calendar-other").IsInvalidated);

        var published = Assert.Single(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>());
        Assert.Equal(["ASSET-CNC-01", "WC-CNC"], published.Payload.AffectedResourceIds);

        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Equal(WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName, processed.ConsumerName);
    }

    [Fact]
    public async Task MasterData_work_calendar_changed_event_ignores_plan_not_assigned_to_a_resource_using_the_calendar()
    {
        await using var provider = CreateInMemoryProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seedDbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedDbContext.SchedulePlans.Add(CreatePlan(
                "plan-unused-calendar",
                SchedulePlanStatusContract.Generated,
                "org-001",
                "env-dev",
                "problem-unused-calendar",
                "ASSET-LATHE-01",
                "WC-LATHE"));
            var horizonStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
            var horizonEnd = horizonStart.AddHours(8);
            var problem = new SchedulingProblemContract(
                1,
                "problem-unused-calendar",
                "org-001",
                "env-dev",
                horizonStart,
                horizonEnd,
                [],
                [
                    new SchedulingResourceContract("ASSET-CNC-01", "WC-CNC", [], 1, "CAL-A", "ASSET-CNC-01"),
                    new SchedulingResourceContract("ASSET-LATHE-01", "WC-LATHE", [], 1, "CAL-B", "ASSET-LATHE-01"),
                ],
                [
                    new SchedulingCalendarContract("CAL-A", [new SchedulingTimeWindowContract(horizonStart, horizonEnd, "regular")]),
                    new SchedulingCalendarContract("CAL-B", [new SchedulingTimeWindowContract(horizonStart, horizonEnd, "regular")]),
                ],
                [],
                [],
                [],
                []);
            seedDbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
                problem.ProblemId,
                problem.ContractVersion,
                problem.OrganizationId,
                problem.EnvironmentId,
                "fingerprint-unused-calendar",
                JsonSerializer.Serialize(problem, SchedulingJson.Options),
                horizonStart,
                horizonEnd,
                FixedNow));
            await seedDbContext.SaveChangesAsync();
        }

        using var scope = provider.CreateScope();
        var handler = new WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>());

        await handler.HandleAsync(CreateWorkCalendarChangedEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>().Published);
    }

    [Fact]
    public async Task MasterData_resource_changed_event_invalidates_only_generated_plans_assigned_to_the_resource_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateResourceChangedEvent("WorkCenter", "WC-CNC");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-calendar-target", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.ResourceChanged, invalidation.ReasonCode);
        Assert.Equal("WC-CNC", invalidation.AffectedResourceId);
        Assert.Equal(MasterDataIntegrationEventTypes.ResourceChanged, invalidation.SourceEventType);
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
    }

    [Fact]
    public async Task MasterData_work_center_change_publishes_only_operations_whose_work_center_matches()
    {
        await using var provider = CreateInMemoryProvider();
        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.SchedulePlans.Add(CreatePlanWithAssignments(
                "plan-work-center-collision",
                "problem-work-center-collision",
                [
                    new ScheduleAssignmentContract(
                        "assign-target",
                        "WO-001",
                        "OP-TARGET",
                        10,
                        "ASSET-CNC-01",
                        "WC-CNC",
                        new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        false,
                        "scheduled"),
                    new ScheduleAssignmentContract(
                        "assign-resource-code-collision",
                        "WO-001",
                        "OP-UNRELATED",
                        20,
                        "WC-CNC",
                        "WC-LATHE",
                        new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                        false,
                        "scheduled"),
                ]));
            await dbContext.SaveChangesAsync();
        }

        using var scope = provider.CreateScope();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>());

        await handler.HandleAsync(CreateResourceChangedEvent("WorkCenter", "WC-CNC"), CancellationToken.None);

        var published = Assert.Single(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>());
        Assert.Equal(["OP-TARGET"], published.Payload.AffectedOperations.Select(x => x.OperationId));
    }

    [Fact]
    public async Task MasterData_resource_changed_event_with_untraceable_hierarchy_is_a_successful_no_match()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var logger = new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            logger);

        await handler.HandleAsync(CreateResourceChangedEvent("Shift", "WC-CNC"), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Contains(logger.Messages, x =>
            x.LogLevel == LogLevel.Information &&
            x.Message.Contains("WC-CNC", StringComparison.Ordinal) &&
            x.Message.Contains("matched no schedule plan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MasterData_work_calendar_changes_with_distinct_event_ids_are_not_collapsed_by_the_stable_source_key()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var first = CreateWorkCalendarChangedEvent();
        var second = first with
        {
            EventId = "evt-masterdata-calendar-002",
            OccurredAtUtc = first.OccurredAtUtc.AddHours(1),
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(2, await dbContext.SchedulePlanInvalidations.CountAsync());
    }

    [Fact]
    public async Task MasterData_resource_changes_with_distinct_event_ids_are_not_collapsed_by_the_stable_source_key()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var first = CreateResourceChangedEvent("WorkCenter", "WC-CNC");
        var second = first with
        {
            EventId = "evt-masterdata-resource-WorkCenter-WC-CNC-002",
            OccurredAtUtc = first.OccurredAtUtc.AddHours(1),
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(2, await dbContext.SchedulePlanInvalidations.CountAsync());
    }

    [Fact]
    public void Scheduling_input_change_handlers_have_cap_subscriptions()
    {
        AssertSubscription<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>(
            "AssetUnavailableIntegrationEvent",
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<AssetUnavailableV2IntegrationEventHandlerForInvalidateSchedulePlans>(
            AssetUnavailableIntegrationEventTopics.V2Template,
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans>(
            "AssetRestoredIntegrationEvent",
            AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "DeviceStateChangedIntegrationEvent",
            DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "StockAvailabilityChangedIntegrationEvent",
            StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans>(
            "InspectionResultIntegrationEvent",
            QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "WorkOrderReleasedIntegrationEvent",
            WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "WorkCalendarChangedIntegrationEvent",
            WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "ResourceChangedIntegrationEvent",
            ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
    }

    private static async Task SeedMasterDataScopedPlansAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.SchedulePlans.Add(CreatePlan(
            "plan-calendar-target",
            SchedulePlanStatusContract.Generated,
            "org-001",
            "env-dev",
            "problem-calendar-target",
            "ASSET-CNC-01",
            "WC-CNC"));
        var released = CreatePlan(
            "plan-calendar-released",
            SchedulePlanStatusContract.Generated,
            "org-001",
            "env-dev",
            "problem-calendar-released",
            "ASSET-CNC-01",
            "WC-CNC");
        released.Release(FixedNow, 1);
        dbContext.SchedulePlans.Add(released);
        dbContext.SchedulePlans.Add(CreatePlan(
            "plan-calendar-other",
            SchedulePlanStatusContract.Generated,
            "org-001",
            "env-dev",
            "problem-calendar-other",
            "ASSET-LATHE-01",
            "WC-LATHE"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-calendar-target", "CAL-A", "ASSET-CNC-01", "WC-CNC"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-calendar-released", "CAL-A", "ASSET-CNC-01", "WC-CNC"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-calendar-other", "CAL-B", "ASSET-LATHE-01", "WC-LATHE"));
        await dbContext.SaveChangesAsync();
    }

    private static ScheduleProblemSnapshot CreateProblemSnapshot(
        string problemId,
        string calendarId,
        string resourceId,
        string workCenterId)
    {
        var horizonStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var horizonEnd = horizonStart.AddHours(8);
        var problem = new SchedulingProblemContract(
            1,
            problemId,
            "org-001",
            "env-dev",
            horizonStart,
            horizonEnd,
            [],
            [new SchedulingResourceContract(resourceId, workCenterId, [], 1, calendarId, resourceId)],
            [new SchedulingCalendarContract(calendarId, [new SchedulingTimeWindowContract(horizonStart, horizonEnd, "regular")])],
            [],
            [],
            [],
            []);
        return new ScheduleProblemSnapshot(
            problemId,
            1,
            "org-001",
            "env-dev",
            $"fingerprint-{problemId}",
            JsonSerializer.Serialize(problem, SchedulingJson.Options),
            horizonStart,
            horizonEnd,
            FixedNow);
    }

    private static async Task SeedPlansAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.SchedulePlans.Add(CreatePlan("plan-generated", SchedulePlanStatusContract.Generated, "org-001", "env-dev"));
        var released = CreatePlan("plan-released", SchedulePlanStatusContract.Generated, "org-001", "env-dev");
        released.Release(FixedNow, 1);
        dbContext.SchedulePlans.Add(released);
        dbContext.SchedulePlans.Add(CreatePlan("plan-other-env", SchedulePlanStatusContract.Generated, "org-001", "env-other"));
        await dbContext.SaveChangesAsync();
    }

    private static SchedulePlan CreatePlan(
        string planId,
        SchedulePlanStatusContract status,
        string organizationId,
        string environmentId,
        string problemId = "problem-001",
        string resourceId = "ASSET-CNC-01",
        string workCenterId = "WC-CNC")
    {
        return CreatePlanWithAssignments(
            planId,
            problemId,
            [
                new ScheduleAssignmentContract(
                    AssignmentId: $"assign-{planId}",
                    OrderId: "WO-001",
                    OperationId: "OP-001",
                    OperationSequence: 10,
                    ResourceId: resourceId,
                    WorkCenterId: workCenterId,
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    IsLocked: false,
                    ExplanationCode: "scheduled"),
                new ScheduleAssignmentContract(
                    AssignmentId: $"assign-{planId}-2",
                    OrderId: "WO-001",
                    OperationId: "OP-002",
                    OperationSequence: 20,
                    ResourceId: resourceId,
                    WorkCenterId: workCenterId,
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    IsLocked: false,
                    ExplanationCode: "scheduled"),
            ],
            status,
            organizationId,
            environmentId);
    }

    private static SchedulePlan CreatePlanWithAssignments(
        string planId,
        string problemId,
        IReadOnlyCollection<ScheduleAssignmentContract> assignments,
        SchedulePlanStatusContract status = SchedulePlanStatusContract.Generated,
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        return SchedulePlan.FromGeneratedPlan(
            organizationId,
            environmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: problemId,
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: status,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(
                    ScheduledOperationCount: 1,
                    UnscheduledOperationCount: 0,
                    AssignedMinutes: 60,
                    MakespanMinutes: 60,
                    TotalTardinessMinutes: 0,
                    LateOperationCount: 0,
                    OnTimeRate: 1m,
                    AverageResourceUtilization: 0m),
                Assignments: assignments,
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));
    }

    private static ServiceProvider CreateInMemoryProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var databaseName = $"scheduling-events-{Guid.NewGuid():N}";
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddScoped<ISchedulingIntegrationEventContextAccessor, StubSchedulingIntegrationEventContextAccessor>();
        services.AddScoped<SchedulePlanInvalidatedIntegrationEventConverter>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        // AssetUnavailable 的 v1/v2 handler 与 canonical processor 走与 Program.cs 相同的 DI seam 注册，
        // 用例从容器解析 handler，而不是手工 new：只有这样才证明两个版本解析到的是同一个注册的 processor。
        services.AddSingleton<IIntegrationEventDeadLetterStore, InMemoryIntegrationEventDeadLetterStore>();
        services.AddSingleton<RecordingLogger<AssetUnavailableCanonicalProcessor>>();
        services.AddSingleton<ILogger<AssetUnavailableCanonicalProcessor>>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingLogger<AssetUnavailableCanonicalProcessor>>());
        services.AddScoped<AssetUnavailableCanonicalProcessor>();
        services.AddScoped<IAssetUnavailableCanonicalProcessor>(serviceProvider =>
            serviceProvider.GetRequiredService<AssetUnavailableCanonicalProcessor>());
        services.AddScoped<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>();
        services.AddScoped<AssetUnavailableV2IntegrationEventHandlerForInvalidateSchedulePlans>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static AssetUnavailableIntegrationEvent CreateAssetUnavailableEvent()
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-maint-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-maint-001",
            "wo-maint-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "maintenance:asset-unavailable:ASSET-CNC-01",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)));
    }

    private static AssetRestoredIntegrationEvent CreateAssetRestoredEvent()
    {
        return new AssetRestoredIntegrationEvent(
            "evt-maint-restored-001",
            MaintenanceIntegrationEventTypes.AssetRestored,
            MaintenanceIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-maint-001",
            "wo-maint-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "maintenance:asset-restored:ASSET-CNC-01",
            new AssetRestoredPayload("ASSET-CNC-01", new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero)));
    }

    private static AssetUnavailableV2IntegrationEvent CreateAssetUnavailableV2Event(string idempotencyKey)
    {
        return new AssetUnavailableV2IntegrationEvent(
            "evt-maint-v2-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2,
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            MaintenanceIntegrationEventSources.BusinessMaintenance,
            "corr-maint-001",
            "wo-maint-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            idempotencyKey,
            new AssetUnavailableV2Payload("ASSET-CNC-01", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)));
    }

    private static DeviceStateChangedIntegrationEvent CreateDeviceStateChangedEvent()
    {
        return new DeviceStateChangedIntegrationEvent(
            "evt-iiot-state-001",
            IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged,
            IndustrialTelemetryIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 2, 0, TimeSpan.Zero),
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            "corr-iiot-001",
            "state-snapshot-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            "industrialTelemetry:device-state:org-001:env-dev:ASSET-CNC-01:state-seq-009:state-snapshot-001",
            new DeviceStateChangedPayload(
                "state-snapshot-001",
                "ASSET-CNC-01",
                "faulted",
                "state-seq-009"));
    }

    private static StockAvailabilityChangedIntegrationEvent CreateStockAvailabilityChangedEvent()
    {
        return new StockAvailabilityChangedIntegrationEvent(
            "evt-stock-001",
            InventoryIntegrationEventTypes.StockAvailabilityChanged,
            InventoryIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 5, 0, TimeSpan.Zero),
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-stock-001",
            "movement-001",
            "org-001",
            "env-dev",
            "system:inventory",
            "inventory:stock-availability:SKU-001",
            new StockAvailabilityChangedPayload(
                "SKU-001",
                "EA",
                "production",
                "line-side",
                null,
                null,
                "Unrestricted",
                "production",
                null,
                10,
                2,
                8,
                42,
                new DateTimeOffset(2026, 6, 1, 9, 5, 0, TimeSpan.Zero),
                12,
                120));
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(string eventType, string sourceDocumentId = "WO-001")
    {
        return new InspectionResultIntegrationEvent(
            $"evt-quality-{eventType}-{sourceDocumentId}",
            eventType,
            QualityIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-quality-001",
            "inspection-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection:{eventType}:{sourceDocumentId}",
            new InspectionResultPayload(
                "INS-001",
                null,
                "mes-work-order",
                QualityIntegrationEventSources.BusinessMes,
                sourceDocumentId,
                "SKU-001",
                1,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "Rejected" : "Passed",
                null,
                [],
                new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero)));
    }

    private static WorkOrderReleasedIntegrationEvent CreateWorkOrderReleasedEvent()
    {
        return new WorkOrderReleasedIntegrationEvent(
            "evt-mes-wo-001",
            MesIntegrationEventTypes.WorkOrderReleased,
            MesIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero),
            MesIntegrationEventSources.BusinessMes,
            "corr-mes-001",
            "WO-NEW",
            "org-001",
            "env-dev",
            "system:mes",
            "mes:work-order-released:WO-NEW",
            new WorkOrderReleasedPayload(
                "WO-NEW",
                "SKU-001",
                10,
                new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero),
                [new ReleasedOperationPayload("OP-NEW-10", 10, "WC-CNC")]));
    }

    private static WorkCalendarChangedIntegrationEvent CreateWorkCalendarChangedEvent()
    {
        return new WorkCalendarChangedIntegrationEvent(
            "evt-masterdata-calendar-001",
            MasterDataIntegrationEventTypes.WorkCalendarChanged,
            MasterDataIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 20, 0, TimeSpan.Zero),
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-masterdata-001",
            "calendar-CAL-A",
            "org-001",
            "env-dev",
            "user:planner",
            "work-calendar-changed:org-001:env-dev:CAL-A",
            new MasterDataChangedPayload("work-calendar", "CAL-A", "active", FixedNow));
    }

    private static ResourceChangedIntegrationEvent CreateResourceChangedEvent(string resourceType, string code)
    {
        return new ResourceChangedIntegrationEvent(
            $"evt-masterdata-resource-{resourceType}-{code}",
            MasterDataIntegrationEventTypes.ResourceChanged,
            MasterDataIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 25, 0, TimeSpan.Zero),
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-masterdata-001",
            $"resource-{code}",
            "org-001",
            "env-dev",
            "user:planner",
            $"resource-changed:org-001:env-dev:{resourceType}:{code}",
            new ResourceChangedPayload(resourceType, code, "active", FixedNow));
    }

    private static void AssertSubscription<THandler>(string expectedTopic, string expectedGroup)
    {
        var attribute = typeof(THandler)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(CapSubscribeAttribute), false).Cast<CapSubscribeAttribute>())
            .Single();

        Assert.Equal(expectedTopic, attribute.Name);
        Assert.Equal(expectedGroup, attribute.Group);
    }

    private static AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans CreateAssetUnavailableHandler(
        IServiceProvider services) =>
        services.GetRequiredService<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>();

    private static AssetUnavailableV2IntegrationEventHandlerForInvalidateSchedulePlans CreateAssetUnavailableV2Handler(
        IServiceProvider services) =>
        services.GetRequiredService<AssetUnavailableV2IntegrationEventHandlerForInvalidateSchedulePlans>();

    private static Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAssetUnavailableDeadLettersAsync(IServiceProvider services) =>
        services.GetRequiredService<IIntegrationEventDeadLetterStore>().ListAsync(
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None);

    private sealed class CountingAssetUnavailableProcessor(IAssetUnavailableCanonicalProcessor inner) : IAssetUnavailableCanonicalProcessor
    {
        public List<AssetUnavailableCanonicalInput> Inputs { get; } = [];

        public Task ProcessAsync(AssetUnavailableCanonicalInput input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return inner.ProcessAsync(input, cancellationToken);
        }
    }

    private sealed class RecordingCapPublisher : ICapPublisher
    {
        public List<(string Name, object? Content)> Published { get; } = [];
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public ICapTransaction? Transaction { get; set; }

        public Task PublishAsync<T>(string name, T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default)
        {
            Published.Add((name, contentObj));
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(string name, T? contentObj, IDictionary<string, string?> headers, CancellationToken cancellationToken = default)
        {
            Published.Add((name, contentObj));
            return Task.CompletedTask;
        }

        public void Publish<T>(string name, T? contentObj, string? callbackName = null) => Published.Add((name, contentObj));
        public void Publish<T>(string name, T? contentObj, IDictionary<string, string?> headers) => Published.Add((name, contentObj));
        public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? contentObj, IDictionary<string, string?> headers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void PublishDelay<T>(TimeSpan delayTime, string name, T? contentObj, IDictionary<string, string?> headers) => throw new NotSupportedException();
        public void PublishDelay<T>(TimeSpan delayTime, string name, T? contentObj, string? callbackName = null) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class StubSchedulingIntegrationEventContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext()
        {
            return new SchedulingIntegrationEventContext(
                "corr-scheduling-test",
                "cause-scheduling-test",
                "system:test");
        }
    }
}
