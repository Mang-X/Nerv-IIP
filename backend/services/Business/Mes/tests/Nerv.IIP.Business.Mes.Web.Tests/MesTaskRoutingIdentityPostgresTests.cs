using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesTaskRoutingIdentityPostgresTests
{
    private static readonly DateTimeOffset RequestedAt = DateTimeOffset.Parse("2026-10-17T08:00:00Z");

    // #4484: both creation paths must freeze the explicit HTTP snapshot, even after the same PV is rebound.
    [MesRealPostgresFact(Timeout = 120_000)]
    public async Task Plan_and_rush_tasks_keep_creation_route_after_production_version_rebinding_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        using var source = new EngineeringRoutingSource();
        var routingProvider = new HttpMesProductEngineeringRoutingSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(source) { BaseAddress = new Uri("http://engineering") }));
        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            await CreatePlanAsync(db, routingProvider, "WO-PLAN-A");
            await CreateRushAsync(db, routingProvider, "WO-RUSH-A");
            await db.SaveChangesAsync();
        }

        source.Revision = "B";
        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            await CreatePlanAsync(db, routingProvider, "WO-PLAN-B");
            await CreateRushAsync(db, routingProvider, "WO-RUSH-B");
            await db.SaveChangesAsync();
        }

        await using var readback = new ApplicationDbContext(options, new NoopMediator());
        var tasks = await readback.OperationTasks.AsNoTracking().ToListAsync();
        Assert.Equal(8, tasks.Count);
        Assert.All(tasks, task =>
        {
            Assert.Equal("PV-PINNED", task.RoutingSourceProductionVersionId);
            Assert.Equal("ROUTE-EXPLICIT", task.RoutingCode);
            var revision = task.WorkOrderId.EndsWith("-A", StringComparison.Ordinal) ? "A" : "B";
            Assert.Equal(revision, task.RoutingRevision);
            // Opaque id deliberately does not encode the route code/revision.
            Assert.Equal(revision == "A" ? "opaque-first" : "opaque-second", task.RoutingVersionId);
        });
    }

    // #4484: a compatibility upgrade must preserve legacy rows and leave absent provenance unknown.
    [MesRealPostgresFact(Timeout = 120_000)]
    public async Task Migration_and_manual_creation_leave_unproven_task_routes_unknown_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            await CreatePlanAsync(db, new NullMesRoutingSnapshotProvider(), "WO-LEGACY", "WC-MANUAL");
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260929175845_AddMesWorkOrderRushFlag");
            await db.Database.MigrateAsync();
            await CreatePlanAsync(db, new NullMesRoutingSnapshotProvider(), "WO-MANUAL", "WC-MANUAL");
            await db.SaveChangesAsync();
        }

        await using var readback = new ApplicationDbContext(options, new NoopMediator());
        var tasks = await readback.OperationTasks.AsNoTracking().OrderBy(x => x.WorkOrderId).ToListAsync();
        Assert.Equal(new[] { "WO-LEGACY", "WO-MANUAL" }, tasks.Select(x => x.WorkOrderId));
        Assert.All(tasks, task =>
        {
            Assert.Equal("WC-MANUAL", task.WorkCenterId);
            Assert.Equal("SKU-001", task.SkuCode);
            Assert.Equal(5m, task.PlannedQuantity);
            Assert.Null(task.RoutingSourceProductionVersionId);
            Assert.Null(task.RoutingVersionId);
            Assert.Null(task.RoutingCode);
            Assert.Null(task.RoutingRevision);
        });
        Assert.All(await readback.WorkOrders.AsNoTracking().ToListAsync(), order => Assert.Equal("PV-PINNED", order.ProductionVersionId));
    }

    private static Task<MesAcceptedResponse> CreatePlanAsync(
        ApplicationDbContext db, IMesRoutingSnapshotProvider routingProvider, string workOrderId, string? workCenterId = null) =>
        new ConvertPlanToWorkOrderCommandHandler(db, null, null,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(db), routingProvider).Handle(
            new ConvertPlanToWorkOrderCommand("org-001", "env-dev", workOrderId, workOrderId, RequestedAt,
                "SKU-001", "PV-PINNED", 5m, "PCS", RequestedAt.AddDays(1), workCenterId), CancellationToken.None);

    private static Task<CreateRushWorkOrderResponse> CreateRushAsync(
        ApplicationDbContext db, IMesRoutingSnapshotProvider routingProvider, string workOrderId) =>
        new CreateRushWorkOrderCommandHandler(new PersistentMesPlanningStore(db), new MesCodingService(), db,
            routingSnapshotProvider: routingProvider).Handle(
            new CreateRushWorkOrderCommand("org-001", "env-dev", workOrderId, "SKU-001", "PV-PINNED", 5m,
                RequestedAt.AddDays(1), "WC-OVERRIDE", $"{workOrderId}-OP-10", 10, TimeSpan.FromMinutes(30), RequestedAt),
            CancellationToken.None);

    private sealed class EngineeringRoutingSource : HttpMessageHandler
    {
        public string Revision { get; set; } = "A";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/business/v1/engineering/production-versions/PV-PINNED/routing-snapshot", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    data = new
                    {
                        productionVersionId = "PV-PINNED", organizationId = "org-001", environmentId = "env-dev",
                        skuCode = "SKU-001", productionVersionStatus = "Active",
                        routingVersionId = Revision == "A" ? "opaque-first" : "opaque-second",
                        routingCode = "ROUTE-EXPLICIT", routingRevision = Revision, routingStatus = "Published",
                        operations = new[]
                        {
                            new { sequence = 10, workCenterCode = "WC-CUT", operationCode = "CUT", standardMinutes = 20 },
                            new { sequence = 20, workCenterCode = "WC-PACK", operationCode = "PACK", standardMinutes = 10 },
                        },
                    },
                }),
            });
        }
    }
}
