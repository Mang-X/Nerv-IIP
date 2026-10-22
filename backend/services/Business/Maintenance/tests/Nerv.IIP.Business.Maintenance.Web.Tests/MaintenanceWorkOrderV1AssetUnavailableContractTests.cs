using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #2964 冻结的 v1 `assetUnavailableReason` 合同在迁移窗口内零漂移：nullable、maxlen 500、null/空/纯空白不标记不可用也不产生事件、
/// 非空白按既有 trim 语义标记并进入幂等指纹。审核（PR #3123）实测这些分支此前零覆盖（改 maxlen、改空白语义、指纹删字段都存活），
/// 本类把它们钉成契约。
/// </summary>
public sealed class MaintenanceWorkOrderV1AssetUnavailableContractTests
{
    [Fact]
    public void V1_command_validator_keeps_the_500_character_reason_limit()
    {
        var validator = new CreateMaintenanceWorkOrderCommandValidator();

        Assert.True(validator.Validate(CreateCommand("k", null)).IsValid);
        Assert.True(validator.Validate(CreateCommand("k", string.Empty)).IsValid);
        Assert.True(validator.Validate(CreateCommand("k", new string('x', 500))).IsValid);
        Assert.False(validator.Validate(CreateCommand("k", new string('x', 501))).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task V1_null_empty_or_whitespace_reason_creates_a_plain_work_order_without_unavailable_fact(string? reason)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        await new CreateMaintenanceWorkOrderCommandHandler(db).Handle(CreateCommand("v1-plain", reason), CancellationToken.None);

        var workOrder = db.MaintenanceWorkOrders.Local.Single();
        Assert.False(workOrder.AssetUnavailable);
        Assert.Null(workOrder.AssetUnavailableReason);
        Assert.Null(workOrder.AssetUnavailableFromUtc);
        Assert.Empty(workOrder.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
        Assert.Empty(workOrder.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>());
    }

    [Fact]
    public async Task V1_non_blank_reason_is_trimmed_free_text_and_never_consults_the_catalog()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        Assert.Equal(0, await db.DowntimeReasons.CountAsync());

        await new CreateMaintenanceWorkOrderCommandHandler(db).Handle(CreateCommand("v1-text", "  over temperature  "), CancellationToken.None);

        var workOrder = db.MaintenanceWorkOrders.Local.Single();
        Assert.True(workOrder.AssetUnavailable);
        Assert.Equal("over temperature", workOrder.AssetUnavailableReason);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
        Assert.Equal("over temperature", domainEvent.Reason);
        Assert.Empty(workOrder.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>());
    }

    [Fact]
    public async Task V1_fingerprint_keeps_the_trimmed_reason_so_a_reused_key_with_another_reason_conflicts()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CreateMaintenanceWorkOrderCommandHandler(db);

        var first = await handler.Handle(CreateCommand("v1-fingerprint", "over temperature"), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // 既有 trim 语义：只差前后空白的同一原因是同一意图。
        var replayed = await handler.Handle(CreateCommand("v1-fingerprint", "  over temperature  "), CancellationToken.None);
        Assert.Equal(first.WorkOrderId, replayed.WorkOrderId);
        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(CreateCommand("v1-fingerprint", "bearing failure"), CancellationToken.None));
        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() =>
            handler.Handle(CreateCommand("v1-fingerprint", null), CancellationToken.None));
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
    }

    private static CreateMaintenanceWorkOrderCommand CreateCommand(string idempotencyKey, string? reason) =>
        new("org-001", "env-dev", "DEV-CNC-01", "high", null, "operator-001", reason, IdempotencyKey: idempotencyKey);
}
