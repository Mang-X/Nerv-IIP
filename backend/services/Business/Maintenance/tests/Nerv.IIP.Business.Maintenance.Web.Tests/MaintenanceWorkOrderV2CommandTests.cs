using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Contracts.Maintenance;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #2968：v2 工单入口的写边界与双发 publisher。provider-light（InMemory）只证明应用层顺序与形状；
/// 目录精确命中的数据库谓词、同事务原子性在 <see cref="MaintenanceAssetUnavailableV2PostgresTests"/> 的真实 PostgreSQL lane 证明。
/// </summary>
public sealed class MaintenanceWorkOrderV2CommandTests
{
    private const string ExactCode = "Planned-Maintenance_01";

    [Fact]
    public async Task V2_exact_catalog_code_marks_asset_unavailable_with_the_raw_request_value()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await SeedCatalogAsync(db);
        var handler = new CreateMaintenanceWorkOrderV2CommandHandler(db);

        var result = await handler.Handle(CreateCommand("v2-intent-001", ExactCode), CancellationToken.None);

        var workOrder = db.MaintenanceWorkOrders.Local.Single();
        Assert.Equal(workOrder.Id, result.WorkOrderId);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, result.Status);
        Assert.True(workOrder.AssetUnavailable);
        Assert.Equal(ExactCode, workOrder.AssetUnavailableReason);
        Assert.NotNull(workOrder.AssetUnavailableFromUtc);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>());
        Assert.Equal(ExactCode, domainEvent.ReasonCode);
        Assert.Equal(workOrder.AssetUnavailableFromUtc, domainEvent.FromUtc);
        // v2 事实绝不复用 v1 自由文本领域事件：否则 netcorepal 的 v1 converter 会再发一条 v1，与 companion 重复。
        Assert.Empty(workOrder.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
        Assert.Equal(MaintenanceWorkOrderSourceTypes.Manual, workOrder.SourceType);
    }

    [Fact]
    public async Task V2_null_reason_code_creates_a_plain_work_order_without_unavailable_fact()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderV2CommandHandler(db);

        await handler.Handle(CreateCommand("v2-intent-null", null), CancellationToken.None);

        var workOrder = db.MaintenanceWorkOrders.Local.Single();
        Assert.False(workOrder.AssetUnavailable);
        Assert.Null(workOrder.AssetUnavailableReason);
        Assert.Null(workOrder.AssetUnavailableFromUtc);
        Assert.Empty(workOrder.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>());
        Assert.Empty(workOrder.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(" " + ExactCode)]
    [InlineData(ExactCode + " ")]
    [InlineData("planned-maintenance_01")]
    [InlineData("PLANNED-MAINTENANCE_01")]
    [InlineData("other-organization-code")]
    [InlineData("other-environment-code")]
    [InlineData("over temperature")]
    public async Task V2_near_miss_cross_scope_or_free_text_fails_before_any_write(string reasonCode)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await SeedCatalogAsync(db);
        var handler = new CreateMaintenanceWorkOrderV2CommandHandler(db);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(CreateCommand("v2-intent-reject", reasonCode), CancellationToken.None));

        Assert.Equal(CreateMaintenanceWorkOrderV2CommandHandler.ReasonCodeNotFoundErrorCode, exception.Message);
        Assert.DoesNotContain(reasonCode, exception.Message, StringComparison.Ordinal);
        Assert.Empty(db.MaintenanceWorkOrders.Local);
        Assert.Empty(db.CodeIdempotencyKeys.Local);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public void V2_command_validator_rejects_empty_and_over_long_reason_codes_but_accepts_null_and_bounds()
    {
        var validator = new CreateMaintenanceWorkOrderV2CommandValidator();

        Assert.True(validator.Validate(CreateCommand("k", null)).IsValid);
        Assert.True(validator.Validate(CreateCommand("k", "x")).IsValid);
        Assert.True(validator.Validate(CreateCommand("k", new string('x', 100))).IsValid);
        Assert.False(validator.Validate(CreateCommand("k", string.Empty)).IsValid);
        Assert.False(validator.Validate(CreateCommand("k", new string('x', 101))).IsValid);
    }

    [Fact]
    public void V2_request_validator_mirrors_v1_key_rules_and_bounds_the_reason_code()
    {
        var validator = new CreateMaintenanceWorkOrderV2RequestValidator();

        Assert.True(validator.Validate(CreateRequest("k", null)).IsValid);
        Assert.True(validator.Validate(CreateRequest("k", "x")).IsValid);
        Assert.False(validator.Validate(CreateRequest("k", string.Empty)).IsValid);
        Assert.False(validator.Validate(CreateRequest("k", new string('x', 101))).IsValid);
        Assert.False(validator.Validate(CreateRequest(string.Empty, "x")).IsValid);
        Assert.False(validator.Validate(CreateRequest("   ", "x")).IsValid);
        Assert.False(validator.Validate(CreateRequest(new string('k', 151), "x")).IsValid);
    }

    [Fact]
    public async Task V2_replay_with_the_same_key_and_payload_returns_the_same_receipt_without_a_second_work_order()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await SeedCatalogAsync(db);
        var handler = new CreateMaintenanceWorkOrderV2CommandHandler(db);
        var command = CreateCommand("v2-intent-replay", ExactCode);

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replayed = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.WorkOrderId, replayed.WorkOrderId);
        Assert.Equal(first.Status, replayed.Status);
        Assert.Equal(first.ChangedAtUtc, replayed.ChangedAtUtc);
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("planned-maintenance_01")]
    [InlineData(null)]
    public async Task V2_reusing_a_key_with_a_different_reason_code_fails_closed_instead_of_merging_intents(string? otherReasonCode)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await SeedCatalogAsync(db);
        db.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "planned-maintenance_01", "lower-case sibling"));
        await db.SaveChangesAsync();
        var handler = new CreateMaintenanceWorkOrderV2CommandHandler(db);

        await handler.Handle(CreateCommand("v2-intent-conflict", ExactCode), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(CreateCommand("v2-intent-conflict", otherReasonCode), CancellationToken.None));
        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(CreateCommand("v2-intent-conflict", ExactCode) with { DeviceAssetId = "DEV-OTHER" }, CancellationToken.None));

        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task V2_and_v1_share_one_create_intent_namespace_so_a_reused_key_conflicts_across_versions()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await SeedCatalogAsync(db);
        await new CreateMaintenanceWorkOrderV2CommandHandler(db).Handle(CreateCommand("shared-intent", ExactCode), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            new CreateMaintenanceWorkOrderCommandHandler(db).Handle(
                new CreateMaintenanceWorkOrderCommand(
                    "org-001", "env-dev", "DEV-CNC-01", "high", null, "operator-001", ExactCode, IdempotencyKey: "shared-intent"),
                CancellationToken.None));

        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task V2_command_lock_uses_the_same_key_as_v1_for_the_same_create_intent()
    {
        var v1 = await new CreateMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new CreateMaintenanceWorkOrderCommand("org-001", "env-dev", "DEV-CNC-01", "high", null, "operator-001", null, IdempotencyKey: " shared-lock "),
            CancellationToken.None);
        var v2 = await new CreateMaintenanceWorkOrderV2CommandLock().GetLockKeysAsync(
            CreateCommand(" shared-lock ", ExactCode),
            CancellationToken.None);

        Assert.Equal("business-maintenance:work-order-create:org-001:env-dev:shared-lock", v2.LockKey);
        Assert.Equal(v1.LockKey, v2.LockKey);
        Assert.Equal(v1.AcquireTimeout, v2.AcquireTimeout);
    }

    [Fact]
    public async Task V2_dual_publisher_emits_v1_companion_then_v2_canonical_with_shared_business_key_and_distinct_event_ids()
    {
        var recorder = new RecordingOutboxPublisher();
        var publisher = new AssetUnavailableV2IntegrationEventPublisher(
            recorder,
            new MaintenanceAssetUnavailableTopicOptions(" Production "));
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical", "operator-001");
        var fromUtc = new DateTimeOffset(2026, 9, 4, 1, 2, 3, 456, TimeSpan.Zero);
        workOrder.MarkAssetUnavailableByReasonCode(fromUtc, ExactCode);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>());

        await publisher.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, recorder.Published.Count);
        var (v1Topic, v1Object) = recorder.Published[0];
        var (v2Topic, v2Object) = recorder.Published[1];
        Assert.Equal("AssetUnavailableIntegrationEvent", v1Topic);
        Assert.Equal("nerv-iip.production.business-maintenance.maintenance.asset-unavailable.v2", v2Topic);
        var v1 = Assert.IsType<AssetUnavailableIntegrationEvent>(v1Object);
        var v2 = Assert.IsType<AssetUnavailableV2IntegrationEvent>(v2Object);

        // #2964 双发字段表
        Assert.NotEqual(v1.EventId, v2.EventId);
        Assert.Equal(MaintenanceIntegrationEventTypes.AssetUnavailable, v1.EventType);
        Assert.Equal(MaintenanceIntegrationEventTypes.AssetUnavailable, v2.EventType);
        Assert.Equal(MaintenanceIntegrationEventVersions.V1, v1.EventVersion);
        Assert.Equal(MaintenanceIntegrationEventVersions.V2, v2.EventVersion);
        Assert.Equal(MaintenanceIntegrationEventSources.Maintenance, v1.SourceService);
        Assert.Equal(MaintenanceIntegrationEventSources.BusinessMaintenance, v2.SourceService);
        Assert.Equal(fromUtc, v1.OccurredAtUtc);
        Assert.Equal(fromUtc, v2.OccurredAtUtc);
        Assert.Equal(fromUtc, v1.Payload.FromUtc);
        Assert.Equal(fromUtc, v2.Payload.FromUtc);
        Assert.Equal(workOrder.Id.ToString(), v1.CorrelationId);
        Assert.Equal(v1.CorrelationId, v2.CorrelationId);
        Assert.Equal("alarm-001", v1.CausationId);
        Assert.Equal(v1.CausationId, v2.CausationId);
        Assert.Equal(("org-001", "env-dev", "operator-001"), (v2.OrganizationId, v2.EnvironmentId, v2.Actor));
        Assert.Equal((v1.OrganizationId, v1.EnvironmentId, v1.Actor), (v2.OrganizationId, v2.EnvironmentId, v2.Actor));
        Assert.Equal($"asset-unavailable:{workOrder.Id}:{fromUtc:O}", v1.IdempotencyKey);
        Assert.Equal(v1.IdempotencyKey, v2.IdempotencyKey);
        Assert.Equal(ExactCode, v1.Payload.Reason);
        Assert.Equal(ExactCode, v2.Payload.ReasonCode);
        Assert.Equal("DEV-CNC-01", v2.Payload.DeviceAssetId);
        Assert.Equal(v1.Payload.DeviceAssetId, v2.Payload.DeviceAssetId);

        // companion 与既有 v1 converter 逐字段一致（含 idempotencyKey 表达式），v1-only 消费者看不出差别。
        var legacy = new AssetUnavailableIntegrationEventConverter().Convert(new AssetUnavailableDomainEvent(workOrder, ExactCode, fromUtc));
        Assert.Equal(legacy with { EventId = v1.EventId }, v1);

        // v2 wire 契约在 CAP 序列化时也会再次校验；这里证明它能按共享契约写出并回读。
        var json = JsonSerializer.Serialize(v2, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(v2, roundTripped);
        Assert.Contains("\"reasonCode\":\"" + ExactCode + "\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reason\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V2_dual_publisher_fails_before_writing_any_outbox_when_the_v2_envelope_violates_the_shared_contract()
    {
        var recorder = new RecordingOutboxPublisher();
        var publisher = new AssetUnavailableV2IntegrationEventPublisher(
            recorder,
            new MaintenanceAssetUnavailableTopicOptions("Production"));
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "high", "operator-001");
        // 非 UTC 偏移的 fromUtc 违反 v2 wire 契约；publisher 必须在写 v1 companion 之前就失败，不能留下只有 v1 的半次双发。
        var nonUtc = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.FromHours(8));

        await Assert.ThrowsAsync<JsonException>(() =>
            publisher.Handle(new AssetUnavailableByReasonCodeDomainEvent(workOrder, ExactCode, nonUtc), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AssetUnavailableV2IntegrationEventPublisher(recorder, new MaintenanceAssetUnavailableTopicOptions("   "))
                .Handle(new AssetUnavailableByReasonCodeDomainEvent(workOrder, ExactCode, DateTimeOffset.UtcNow), CancellationToken.None));

        Assert.Empty(recorder.Published);
    }

    [Fact]
    public void Aggregate_v2_marker_keeps_the_raw_code_and_refuses_blank_or_over_long_codes()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "high", "operator-001");

        Assert.Throws<ArgumentException>(() => workOrder.MarkAssetUnavailableByReasonCode(DateTimeOffset.UtcNow, "   "));
        Assert.Throws<ArgumentOutOfRangeException>(() => workOrder.MarkAssetUnavailableByReasonCode(DateTimeOffset.UtcNow, new string('x', 101)));
        Assert.False(workOrder.AssetUnavailable);

        var fromUtc = DateTimeOffset.UtcNow;
        workOrder.MarkAssetUnavailableByReasonCode(fromUtc, " Mixed-Case ");
        workOrder.MarkAssetUnavailableByReasonCode(fromUtc.AddMinutes(1), "second-call-ignored");

        Assert.Equal(" Mixed-Case ", workOrder.AssetUnavailableReason);
        Assert.Equal(fromUtc, workOrder.AssetUnavailableFromUtc);
        Assert.Single(workOrder.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>());
    }

    private static async Task SeedCatalogAsync(ApplicationDbContext db)
    {
        db.DowntimeReasons.AddRange(
            DowntimeReason.Create("org-001", "env-dev", ExactCode, "Planned maintenance"),
            DowntimeReason.Create("org-002", "env-dev", "other-organization-code", "Other organization"),
            DowntimeReason.Create("org-001", "env-prod", "other-environment-code", "Other environment"));
        await db.SaveChangesAsync();
    }

    private static CreateMaintenanceWorkOrderV2Command CreateCommand(string idempotencyKey, string? reasonCode) =>
        new(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            null,
            "operator-001",
            reasonCode,
            IdempotencyKey: idempotencyKey);

    private static CreateMaintenanceWorkOrderV2Request CreateRequest(string idempotencyKey, string? reasonCode) =>
        new(
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            DeviceAssetId: "DEV-CNC-01",
            Priority: "high",
            SourceAlarmId: null,
            OpenedBy: "operator-001",
            AssetUnavailableReasonCode: reasonCode,
            IdempotencyKey: idempotencyKey);

    private sealed class RecordingOutboxPublisher : IMaintenanceIntegrationEventOutboxPublisher
    {
        public List<(string Topic, object Event)> Published { get; } = [];

        public Task PublishAsync<T>(string topic, T integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add((topic, integrationEvent!));
            return Task.CompletedTask;
        }
    }
}
