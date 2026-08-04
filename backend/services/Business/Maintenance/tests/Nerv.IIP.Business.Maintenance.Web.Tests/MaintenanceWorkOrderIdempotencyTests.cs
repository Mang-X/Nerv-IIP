using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Nerv.IIP.Testing;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using Nerv.IIP.Coding;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceWorkOrderIdempotencyTests
{
    [Fact]
    public async Task Manual_work_order_replay_returns_the_same_authoritative_receipt()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderCommandHandler(db);
        var command = CreateCommand("repair-intent-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        var receipt = await db.CodeIdempotencyKeys.AsNoTracking().SingleAsync();
        Assert.InRange(receipt.Code.Length, 1, 128);
        Assert.False(Guid.TryParse(receipt.Code, out _));
        var persisted = await db.MaintenanceWorkOrders.SingleAsync();
        persisted.Complete("fixed", "equipment-failure", 10, []);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replayed = await handler.Handle(command, CancellationToken.None);
        var completed = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync();

        Assert.Equal(first.WorkOrderId, replayed.WorkOrderId);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, first.Status);
        Assert.Equal(first.Status, replayed.Status);
        Assert.Equal(first.ChangedAtUtc, replayed.ChangedAtUtc);
        Assert.Equal(completed.OpenedAtUtc, first.ChangedAtUtc);
        Assert.Equal(MaintenanceWorkOrderStatus.Completed, completed.Status);
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
        Assert.Equal(MaintenanceWorkOrderSourceTypes.Manual, completed.SourceType);
        Assert.Null(completed.SourceReferenceId);
        Assert.DoesNotContain(command.IdempotencyKey!, completed.SourceReferenceId ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_work_order_replay_accepts_the_legacy_raw_id_receipt_without_status_drift()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderCommandHandler(db);
        var command = CreateCommand("repair-intent-legacy-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        var structuredReceipt = await db.CodeIdempotencyKeys.SingleAsync();
        db.CodeIdempotencyKeys.Remove(structuredReceipt);
        await db.SaveChangesAsync();
        db.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            structuredReceipt.OrganizationId,
            structuredReceipt.EnvironmentId,
            structuredReceipt.RuleKey,
            structuredReceipt.IdempotencyKey,
            first.WorkOrderId.ToString(),
            structuredReceipt.PayloadFingerprint,
            structuredReceipt.CreatedAtUtc));
        var persisted = await db.MaintenanceWorkOrders.SingleAsync();
        persisted.Complete("fixed", "equipment-failure", 10, []);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replayed = await handler.Handle(command, CancellationToken.None);
        var completed = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync();

        Assert.Equal(first.WorkOrderId, replayed.WorkOrderId);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, replayed.Status);
        Assert.Equal(first.ChangedAtUtc, replayed.ChangedAtUtc);
        Assert.Equal(MaintenanceWorkOrderStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Manual_work_order_replay_rejects_a_receipt_pointing_to_another_scope()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderCommandHandler(db);
        var command = CreateCommand("repair-intent-cross-scope-001");

        await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        var originalReceipt = await db.CodeIdempotencyKeys.SingleAsync();
        db.CodeIdempotencyKeys.Remove(originalReceipt);
        await db.SaveChangesAsync();
        var otherScopeWorkOrder = MaintenanceWorkOrder.OpenManual(
            "org-other",
            "env-other",
            "DEV-CNC-OTHER",
            "high",
            "emp-other");
        db.MaintenanceWorkOrders.Add(otherScopeWorkOrder);
        db.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            originalReceipt.OrganizationId,
            originalReceipt.EnvironmentId,
            originalReceipt.RuleKey,
            originalReceipt.IdempotencyKey,
            $"v1|{otherScopeWorkOrder.Id}|Open|{otherScopeWorkOrder.OpenedAtUtc:O}",
            originalReceipt.PayloadFingerprint,
            originalReceipt.CreatedAtUtc));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("stored-maintenance-work-order-receipt-is-invalid", exception.Message);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_payload_fails_closed()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderCommandHandler(db);

        await handler.Handle(CreateCommand("repair-intent-002"), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(
                CreateCommand("repair-intent-002") with { DeviceAssetId = "DEV-LATHE-02" },
                CancellationToken.None));

        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task Alarm_sourced_work_order_replays_only_the_same_key_and_full_payload()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderCommandHandler(db);
        var command = CreateCommand("alarm-create-001") with { SourceAlarmId = "alarm-001" };

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(first, await handler.Handle(command, CancellationToken.None));
        var differentKey = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(command with { IdempotencyKey = "alarm-create-002" }, CancellationToken.None));
        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(command with { Priority = "critical" }, CancellationToken.None));

        Assert.Equal("source-alarm-already-bound-to-a-different-create-intent", differentKey.Message);
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task Manual_work_order_key_has_a_dedicated_distributed_lock()
    {
        var settings = await new CreateMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            CreateCommand("repair-intent-003"),
            CancellationToken.None);

        Assert.Equal(
            "business-maintenance:work-order-create:org-001:env-dev:repair-intent-003",
            settings.LockKey);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.AcquireTimeout);
    }

    [Fact]
    public async Task Complete_work_order_replays_the_same_authoritative_result_after_context_reload()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            "emp010");
        db.MaintenanceWorkOrders.Add(workOrder);
        db.DowntimeReasons.Add(DowntimeReason.Create(
            "org-001",
            "env-dev",
            "equipment-failure",
            "Equipment failure",
            "breakdown",
            "equipment-failure"));
        await db.SaveChangesAsync();
        var command = new CompleteMaintenanceWorkOrderCommand(
            workOrder.Id,
            "fixed",
            "equipment-failure",
            10,
            [],
            IdempotencyKey: "maintenance-complete-001");
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(db);

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replay = await new CompleteMaintenanceWorkOrderCommandHandler(db)
            .Handle(command, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(MaintenanceWorkOrderStatus.Completed, replay.Status);
        Assert.Equal(1, replay.Version);
    }

    [Fact]
    public async Task Complete_work_order_replays_the_explicit_two_part_legacy_receipt()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var (command, receipt, changedAtUtc) = await PersistCompletedWorkOrderAsync(db, "maintenance-complete-legacy-two-part");
        await ReplaceCompletionReceiptCodeAsync(db, receipt, $"Completed|{changedAtUtc:O}");

        var replay = await new CompleteMaintenanceWorkOrderCommandHandler(db).Handle(command, CancellationToken.None);

        Assert.Equal(MaintenanceWorkOrderStatus.Completed, replay.Status);
        Assert.Equal(changedAtUtc, replay.ChangedAtUtc);
        Assert.Equal(1, replay.Version);
    }

    [Theory]
    [InlineData("999|2026-08-01T01:02:03.0000000+00:00|1")]
    [InlineData("Completed|not-a-time|1")]
    [InlineData("Completed|2026-08-01T01:02:03.0000000+00:00|-1")]
    [InlineData("Completed|2026-08-01T01:02:03.0000000+00:00|not-a-version")]
    public async Task Complete_work_order_rejects_a_corrupt_three_part_persisted_receipt(string corruptCode)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var (command, receipt, _) = await PersistCompletedWorkOrderAsync(db, $"corrupt-{Guid.CreateVersion7():N}");
        await ReplaceCompletionReceiptCodeAsync(db, receipt, corruptCode);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CompleteMaintenanceWorkOrderCommandHandler(db).Handle(command, CancellationToken.None));

        Assert.Equal("stored-maintenance-completion-receipt-is-invalid", exception.Message);
    }

    [Fact]
    public async Task Complete_work_order_has_a_per_aggregate_distributed_lock()
    {
        var workOrderId = new MaintenanceWorkOrderId(Guid.CreateVersion7());
        var settings = await new CompleteMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new CompleteMaintenanceWorkOrderCommand(
                workOrderId,
                "fixed",
                "equipment-failure",
                10,
                [],
                IdempotencyKey: "maintenance-complete-lock"),
            CancellationToken.None);

        Assert.Equal(
            $"business-maintenance:work-order:{workOrderId}",
            settings.LockKey);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.AcquireTimeout);
    }

    [Fact]
    public async Task Complete_work_order_rejects_same_key_with_different_payload()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            "emp010");
        db.MaintenanceWorkOrders.Add(workOrder);
        db.DowntimeReasons.Add(DowntimeReason.Create(
            "org-001",
            "env-dev",
            "equipment-failure",
            "Equipment failure",
            "breakdown",
            "equipment-failure"));
        await db.SaveChangesAsync();
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(db);

        await handler.Handle(
            new CompleteMaintenanceWorkOrderCommand(
                workOrder.Id,
                "fixed",
                "equipment-failure",
                10,
                [],
                IdempotencyKey: "maintenance-complete-conflict"),
            CancellationToken.None);

        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() => handler.Handle(
            new CompleteMaintenanceWorkOrderCommand(
                workOrder.Id,
                "different result",
                "equipment-failure",
                10,
                [],
                IdempotencyKey: "maintenance-complete-conflict"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Completion_fingerprint_is_culture_invariant_unicode_safe_and_spare_part_order_independent()
    {
        // The scope serialises every culture mutator in the assembly and restores the exact prior
        // values (including "was never set") on dispose, so this test cannot leak fr-FR onwards.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            "emp010");
        db.MaintenanceWorkOrders.Add(workOrder);
        db.DowntimeReasons.Add(DowntimeReason.Create(
            "org-001",
            "env-dev",
            "equipment-failure",
            "Equipment failure",
            "breakdown",
            "equipment-failure"));
        await db.SaveChangesAsync();
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(db);
        var first = new CompleteMaintenanceWorkOrderCommand(
            workOrder.Id,
            " 已修复|Δ ",
            "equipment-failure",
            10,
            [
                new MaintenanceSparePartInput("sku|二", 1.25m, "kg"),
                new MaintenanceSparePartInput("sku|一", 2.5m, "pcs"),
            ],
            IdempotencyKey: "maintenance-canonical-001");

        var result = await handler.Handle(first, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var replay = await new CompleteMaintenanceWorkOrderCommandHandler(db).Handle(
            first with
            {
                Result = "已修复|Δ",
                DowntimeReasonCode = " EQUIPMENT-FAILURE ",
                SpareParts =
                [
                    new MaintenanceSparePartInput(" SKU|一 ", 2.50m, " PCS "),
                    new MaintenanceSparePartInput(" SKU|二 ", 1.250m, " KG "),
                ],
            },
            CancellationToken.None);

        Assert.Equal(result, replay);
    }

    [Fact]
    public async Task Completion_fingerprint_does_not_collapse_delimiters_between_fields()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            "emp010");
        db.MaintenanceWorkOrders.Add(workOrder);
        db.DowntimeReasons.Add(DowntimeReason.Create(
            "org-001",
            "env-dev",
            "equipment-failure",
            "Equipment failure",
            "breakdown",
            "equipment-failure"));
        await db.SaveChangesAsync();
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(db);

        await handler.Handle(
            new CompleteMaintenanceWorkOrderCommand(
                workOrder.Id,
                "a|b",
                "equipment-failure",
                10,
                [],
                ActualTechnicianUserId: "c",
                IdempotencyKey: "maintenance-separator-001"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(
                new CompleteMaintenanceWorkOrderCommand(
                    workOrder.Id,
                    "a",
                    "equipment-failure",
                    10,
                    [],
                    ActualTechnicianUserId: "b|c",
                    IdempotencyKey: "maintenance-separator-001"),
                CancellationToken.None));
    }

    private static CreateMaintenanceWorkOrderCommand CreateCommand(string idempotencyKey) =>
        new(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            null,
            "emp010",
            "主轴异响",
            AssignedTechnicianUserId: "emp042",
            EstimatedLaborMinutes: 45,
            IdempotencyKey: idempotencyKey);

    private static async Task<(CompleteMaintenanceWorkOrderCommand Command, CodeIdempotencyKey Receipt, DateTimeOffset ChangedAtUtc)>
        PersistCompletedWorkOrderAsync(ApplicationDbContext db, string idempotencyKey)
    {
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", $"DEV-{Guid.CreateVersion7():N}", "high", "emp010");
        db.MaintenanceWorkOrders.Add(workOrder);
        if (!await db.DowntimeReasons.AnyAsync(x => x.ReasonCode == "equipment-failure"))
        {
            db.DowntimeReasons.Add(DowntimeReason.Create(
                "org-001", "env-dev", "equipment-failure", "Equipment failure", "breakdown", "equipment-failure"));
        }
        await db.SaveChangesAsync();
        var command = new CompleteMaintenanceWorkOrderCommand(
            workOrder.Id, "fixed", "equipment-failure", 10, [], IdempotencyKey: idempotencyKey);
        var result = await new CompleteMaintenanceWorkOrderCommandHandler(db).Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        var receipt = await db.CodeIdempotencyKeys.SingleAsync(x => x.IdempotencyKey == idempotencyKey);
        return (command, receipt, result.ChangedAtUtc);
    }

    private static async Task ReplaceCompletionReceiptCodeAsync(
        ApplicationDbContext db,
        CodeIdempotencyKey receipt,
        string code)
    {
        db.CodeIdempotencyKeys.Remove(receipt);
        await db.SaveChangesAsync();
        db.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            receipt.OrganizationId,
            receipt.EnvironmentId,
            receipt.RuleKey,
            receipt.IdempotencyKey,
            code,
            receipt.PayloadFingerprint,
            receipt.CreatedAtUtc));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
