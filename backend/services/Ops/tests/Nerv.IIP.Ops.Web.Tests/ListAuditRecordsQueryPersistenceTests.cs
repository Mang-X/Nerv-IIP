using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.Ops.Web.Application.Queries;

namespace Nerv.IIP.Ops.Web.Tests;

/// <summary>
/// #3098：审计记录按 OperationTaskId 过滤的谓词必须在真实关系 provider 上可翻译。
/// 原实现把 <c>x.Id.Id == request.OperationTaskId</c> 写进谓词（强类型 string Id 再取内部成员），
/// SQLite 与 PostgreSQL 18 均报 could not be translated，于是带 OperationTaskId 的审计查询必 500。
/// 既有端点用例跑在 InMemory 上（一律客户端求值）所以照绿，因此这里用 SQLite 实跑；
/// 已实测：把谓词改回原样，本文件用例转红并报 could not be translated。
/// </summary>
public sealed class ListAuditRecordsQueryPersistenceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    [Fact]
    public async Task Filter_by_operation_task_id_translates_and_returns_only_that_task_on_relational_provider()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>()
            .Options;
        await using var dbContext = new ApplicationDbContext(options, mediator: null!);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new EfOperationTaskApplicationService(
            new OperationTaskRepository(dbContext),
            new OperationTemplateRepository(dbContext),
            dbContext);
        var first = await service.CreateAsync(CreateRestartRequest("idem-3098-a"), DateTimeOffset.Parse("2026-09-01T00:00:00Z"), CancellationToken.None);
        var second = await service.CreateAsync(CreateRestartRequest("idem-3098-b"), DateTimeOffset.Parse("2026-09-01T00:00:01Z"), CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var handler = new ListAuditRecordsQueryHandler(new ServiceCollection().AddSingleton(dbContext).BuildServiceProvider());

        var filtered = await handler.Handle(new ListAuditRecordsQuery(Org, Env, first.OperationTaskId), CancellationToken.None);
        var unfiltered = await handler.Handle(new ListAuditRecordsQuery(Org, Env, null), CancellationToken.None);

        Assert.NotEmpty(filtered.Items);
        Assert.All(filtered.Items, item => Assert.Equal(first.OperationTaskId, item.OperationTaskId));
        Assert.Contains(unfiltered.Items, item => item.OperationTaskId == second.OperationTaskId);
        Assert.Equal(unfiltered.Items.Count(item => item.OperationTaskId == first.OperationTaskId), filtered.Items.Count);
    }

    private static CreateOperationTaskRequest CreateRestartRequest(string idempotencyKey) =>
        new(Org, Env, "docker-container-local-demo-001", "lifecycle.restart", idempotencyKey,
            "local-admin", "manual smoke restart", $"corr-{idempotencyKey}", new Dictionary<string, string>());

    // SQLite provider 无法翻译 DateTimeOffset 的排序（仓库已知坑：EF 测试 provider 翻译差异），
    // 测试专用 ModelCustomizer 把所有 DateTimeOffset 列统一转成 long（值均为 UTC，ToBinary 排序与时间序一致）。
    private sealed class SqliteDateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        private static readonly DateTimeOffsetToBinaryConverter Converter = new();

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(Converter);
                }
            }
        }
    }
}
