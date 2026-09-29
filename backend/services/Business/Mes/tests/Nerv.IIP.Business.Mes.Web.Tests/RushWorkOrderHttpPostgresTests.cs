using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class RushWorkOrderHttpPostgresTests
{
    [MesRealPostgresFact]
    public async Task PostgreSQL_http_creation_still_dispatches_work_order_created_after_sku_gate_save()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var recorder = new WorkOrderCreatedRecorder();
        await using var factory = CreateFactory(MesPostgresLaneDatabase.ConnectionString, recorder);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
            await dbContext.Database.MigrateAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-token");
        var response = await client.PostAsJsonAsync("/api/business/v1/mes/work-orders/rush", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-HTTP-PG-001",
            skuId = "SKU-ACTIVE",
            productionVersionId = "PV-001",
            quantity = 5m,
            dueUtc = "2026-07-20T08:00:00Z",
            workCenterId = "WC-001",
            operationTaskId = "OP-001",
            operationSequence = 10,
            durationMinutes = 30,
            idempotencyKey = "rush-http-pg-001",
        });

        response.EnsureSuccessStatusCode();
        var domainEvent = await recorder.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("WO-HTTP-PG-001", domainEvent.WorkOrder.WorkOrderIdValue);
        Assert.Equal(1, recorder.Count);

        using var assertionScope = factory.Services.CreateScope();
        var assertionContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await assertionContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.WorkOrderIdValue == "WO-HTTP-PG-001",
            CancellationToken.None));
        Assert.True((await assertionContext.WorkOrders.SingleAsync()).IsRush);
        Assert.Null((await assertionContext.OperationTasks.SingleAsync(CancellationToken.None)).RequiredSkillCode);
    }

    // #4033：存量高 Priority 不推断急单；HTTP 命令通过 UoW 落库，服务查询在新 scope 读回。
    [MesRealPostgresFact]
    public async Task Legacy_priority_and_explicit_rush_adjustment_survive_migration_and_new_scope_queries()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory(MesPostgresLaneDatabase.ConnectionString, new WorkOrderCreatedRecorder());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            db.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-LEGACY", "SKU-001", "PV-001", 5m, 1000,
                DateTimeOffset.Parse("2026-09-30T08:00:00Z")));
            await db.SaveChangesAsync();
            // 回到上线前 schema 再前滚：表内保留工单，旧结构没有 is_rush 列。
            await db.GetService<IMigrator>().MigrateAsync("20260929031402_AddMesWorkOrderDemandChanges");
            db.ChangeTracker.Clear();
            await db.Database.MigrateAsync();
            var legacy = await db.WorkOrders.AsNoTracking().SingleAsync();
            Assert.Equal(1000, legacy.Priority);
            Assert.False(legacy.IsRush);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-token");
        foreach (var (isRush, priority) in new[] { (true, 7), (false, 23) })
        {
            var response = await client.PostAsJsonAsync("/api/business/v1/mes/work-orders/WO-LEGACY/priority", new
            {
                organizationId = "org-001", environmentId = "env-dev", isRush, priority,
            });
            response.EnsureSuccessStatusCode();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await db.WorkOrders.AsNoTracking().SingleAsync();
            Assert.Equal(isRush, persisted.IsRush);
            Assert.Equal(priority, persisted.Priority);
            var detail = await new GetMesWorkOrderDetailQueryHandler(db).Handle(
                new GetMesWorkOrderDetailQuery("org-001", "env-dev", "WO-LEGACY"), CancellationToken.None);
            Assert.Equal(isRush, detail.IsRush);
            Assert.Equal(priority, detail.Priority);
            var list = await new ListMesWorkOrdersQueryHandler(db).Handle(
                new ListMesWorkOrdersQuery("org-001", "env-dev", null), CancellationToken.None);
            Assert.Equal(isRush, Assert.Single(list.Items).IsRush);
            Assert.Equal(priority, Assert.Single(list.Items).Priority);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        WorkOrderCreatedRecorder recorder)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = connectionString,
                    ["Messaging:Provider"] = "InMemory",
                    ["Cap:Version"] = $"test-rush-http-{Guid.CreateVersion7():N}",
                    ["InternalService:BearerToken"] = "test-internal-token",
                };

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(recorder);
                    services.AddSingleton<INotificationHandler<WorkOrderCreatedDomainEvent>>(
                        serviceProvider => serviceProvider.GetRequiredService<WorkOrderCreatedRecorder>());
                    // 急单建单时冻结齐套需求（#3858）；本用例不起 ProductEngineering，改用无需求快照。
                    services.RemoveAll<IMesMaterialRequirementSnapshotProvider>();
                    services.AddSingleton<IMesMaterialRequirementSnapshotProvider>(NoRequirementSnapshotProvider.Instance);
                });
            });
    }

    private sealed class WorkOrderCreatedRecorder : INotificationHandler<WorkOrderCreatedDomainEvent>
    {
        private readonly TaskCompletionSource<WorkOrderCreatedDomainEvent> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        public int Count => Volatile.Read(ref count);

        public Task Handle(WorkOrderCreatedDomainEvent notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            completion.TrySetResult(notification);
            return Task.CompletedTask;
        }

        public Task<WorkOrderCreatedDomainEvent> WaitAsync(TimeSpan timeout) =>
            completion.Task.WaitAsync(timeout);
    }
}
