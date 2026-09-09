using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using NetCorePal.Extensions.Primitives;
using static Nerv.IIP.Business.Mes.Web.Tests.MesSqliteTestDatabase;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3098：领料申请按 RequestId 定位的谓词必须在真实关系 provider 上可翻译。
/// 原实现在 RequestId 是 Guid 形态时把 <c>x.Id.Id == requestGuid</c> 写进谓词，整条查询翻译不了，
/// 于是线边收料 / 退料（含命令锁）/ 领料申请详情四处只要传 Guid 就 500。
/// 既有用例跑在 InMemory 上（一律客户端求值）所以照绿，因此这里用 SQLite 实跑；
/// 已实测：把任一处谓词改回 <c>x.Id.Id == requestGuid</c>，对应的 Guid 用例转红并报 could not be translated。
/// </summary>
public sealed class MesMaterialIssueRequestLookupPersistenceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private static readonly DateTimeOffset RequestedAtUtc = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
    private static readonly DateTimeOffset ReceivedAtUtc = DateTimeOffset.Parse("2026-09-01T09:00:00Z");
    private static readonly DateTimeOffset ReturnedAtUtc = DateTimeOffset.Parse("2026-09-01T10:00:00Z");

    [Fact]
    public async Task Confirm_receipt_by_guid_id_translates_and_starts_posting_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var materialRequest = SeedRequestedMaterialRequest(dbContext, "MIR-3098-01");
        await dbContext.SaveChangesAsync();
        var guidId = materialRequest.Id.Id.ToString();
        dbContext.ChangeTracker.Clear();

        var response = await new ConfirmLineSideMaterialReceiptCommandHandler(dbContext, MaterialSupplyTestFixtures.Resolver).Handle(
            new ConfirmLineSideMaterialReceiptCommand(Org, Env, guidId, ReceivedAtUtc, 5m, "LOT-3098"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Equal("MIR-3098-01", response.ReferenceId);
        var persisted = await dbContext.MaterialIssueRequests.SingleAsync();
        Assert.Equal(MaterialIssueRequest.ReceiptPostingStatus, persisted.Status);
        Assert.Equal(5m, persisted.PendingReceiptQuantity);
    }

    [Fact]
    public async Task Confirm_receipt_by_request_no_translates_and_starts_posting_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedRequestedMaterialRequest(dbContext, "MIR-3098-02");
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await new ConfirmLineSideMaterialReceiptCommandHandler(dbContext, MaterialSupplyTestFixtures.Resolver).Handle(
            new ConfirmLineSideMaterialReceiptCommand(Org, Env, "MIR-3098-02", ReceivedAtUtc, 5m, "LOT-3098"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.MaterialIssueRequests.SingleAsync();
        Assert.Equal(MaterialIssueRequest.ReceiptPostingStatus, persisted.Status);
    }

    /// <summary>未命中一律走业务异常出口：Guid.TryParse 成功但库里没有的输入也不能落成 500。</summary>
    [Fact]
    public async Task Confirm_receipt_unknown_request_throws_known_exception()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var handler = new ConfirmLineSideMaterialReceiptCommandHandler(dbContext, MaterialSupplyTestFixtures.Resolver);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ConfirmLineSideMaterialReceiptCommand(Org, Env, "MIR-MISSING", ReceivedAtUtc),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ConfirmLineSideMaterialReceiptCommand(Org, Env, "0199a3f0-3098-7000-8000-000000003098", ReceivedAtUtc),
            CancellationToken.None));
    }

    [Fact]
    public async Task Return_lock_by_guid_id_translates_and_keys_on_request_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var materialRequest = SeedReceivedMaterialRequest(dbContext, "MIR-3098-03");
        await dbContext.SaveChangesAsync();
        var guidId = materialRequest.Id.Id.ToString();
        dbContext.ChangeTracker.Clear();

        var settings = await new ReturnLineSideMaterialCommandLock(dbContext).GetLockKeysAsync(
            new ReturnLineSideMaterialCommand(Org, Env, guidId, ReturnedAtUtc, 1m, "return-3098-03"),
            CancellationToken.None);

        Assert.Equal($"business-mes:material-issue-return:{Org}:{Env}:{guidId}", settings.LockKey);
    }

    [Fact]
    public async Task Return_by_guid_id_translates_and_reduces_received_quantity_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var materialRequest = SeedReceivedMaterialRequest(dbContext, "MIR-3098-04");
        await dbContext.SaveChangesAsync();
        var guidId = materialRequest.Id.Id.ToString();
        dbContext.ChangeTracker.Clear();

        var response = await new ReturnLineSideMaterialCommandHandler(dbContext).Handle(
            new ReturnLineSideMaterialCommand(Org, Env, guidId, ReturnedAtUtc, 2m, "return-3098-04"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Equal("MIR-3098-04", response.ReferenceId);
        var persisted = await dbContext.MaterialIssueRequests.SingleAsync();
        Assert.Equal(3m, persisted.ReceivedQuantity);
        Assert.Equal(MaterialIssueRequest.PartiallyReceivedStatus, persisted.Status);
    }

    [Fact]
    public async Task Get_by_guid_id_translates_and_returns_row_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var materialRequest = SeedRequestedMaterialRequest(dbContext, "MIR-3098-05");
        await dbContext.SaveChangesAsync();
        var guidId = materialRequest.Id.Id.ToString();
        dbContext.ChangeTracker.Clear();

        var handler = new GetMaterialIssueRequestQueryHandler(dbContext);
        var row = await handler.Handle(new GetMaterialIssueRequestQuery(Org, Env, guidId), CancellationToken.None);

        Assert.Equal("MIR-3098-05", row.RequestId);
        Assert.Equal(5m, row.RequestedQuantity);
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new GetMaterialIssueRequestQuery(Org, Env, "0199a3f0-3098-7000-8000-000000003098"),
            CancellationToken.None));
    }

    private static MaterialIssueRequest SeedRequestedMaterialRequest(ApplicationDbContext dbContext, string requestNo)
    {
        dbContext.WorkOrders.Add(WorkOrder.Create(
            Org, Env, "WO-3098", "SKU-3098", "PV-3098", 10m, 1, DateTimeOffset.Parse("2026-09-30T00:00:00Z"), "PCS"));
        var materialRequest = MaterialIssueRequest.Create(
            Org, Env, requestNo, "WO-3098", "OP-10", "MAT-3098", "PCS", 5m, RequestedAtUtc);
        materialRequest.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(materialRequest);
        return materialRequest;
    }

    private static MaterialIssueRequest SeedReceivedMaterialRequest(ApplicationDbContext dbContext, string requestNo)
    {
        var materialRequest = SeedRequestedMaterialRequest(dbContext, requestNo);
        materialRequest.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, ReceivedAtUtc, 5m, "LOT-3098");
        materialRequest.ClearDomainEvents();
        return materialRequest;
    }
}
