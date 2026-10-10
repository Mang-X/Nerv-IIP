using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

// ProviderBehavior / DomainInvariant: #4289 requires complete adapter provenance, not truncation.
public sealed class MrpInputSourcePersistenceTests
{
    [DemandPlanningRealPostgresFact]
    public async Task Released_mps_completes_and_reads_back_full_http_adapter_sources_on_new_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_dp_mrp_sources");
        try
        {
            var services = new ServiceCollection();
            services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(database.ConnectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            var date = new DateOnly(2026, 10, 10);
            var mps = MasterProductionSchedule.Create("org-a", "env-a", "FG", "pcs", "SITE", date, 2m);
            mps.MarkReviewed("planner");
            mps.Release("planner");
            db.MasterProductionSchedules.Add(mps);
            await db.SaveChangesAsync();

            foreach (var mesDegraded in new[] { false, true })
            {
                using var http = new HttpClient(new UpstreamResponses(mesDegraded)) { BaseAddress = new Uri("http://upstream.test") };
                var snapshotProvider = new DemandPlanningUpstreamInputSnapshotProvider(db,
                    new HttpPlanningProductEngineeringSnapshotClient(http),
                    new HttpPlanningInventorySnapshotClient(http),
                    new CompositePlanningScheduledReceiptSnapshotClient([
                        new HttpPlanningErpScheduledReceiptSnapshotClient(http, db),
                        new HttpPlanningMesScheduledReceiptSnapshotClient(http)]),
                    new HttpPlanningMasterDataPlanningParameterSnapshotClient(http));
                var expectedSource = "inventory-http:1;erp-purchase-orders:0;accepted-purchases:0;mes-work-orders:"
                    + (mesDegraded ? "error" : "0")
                    + ";master-data-planning-parameters:1;master-data-uom-conversions:0";
                var snapshot = await snapshotProvider.GetSnapshotAsync("org-a", "env-a", date, date.AddDays(30), default);
                Assert.True(snapshot.InventorySnapshotSource.Length > 128);
                Assert.Equal(expectedSource, snapshot.InventorySnapshotSource);
                Assert.Equal("mps", Assert.Single(snapshot.Demands).SourceType);

                var runId = await new RunMrpCommandHandler(db).Handle(new("org-a", "env-a", date, date.AddDays(30)), default);
                await db.SaveChangesAsync();
                await new ExecuteMrpRunCommandHandler(db, snapshotProvider).Handle(new(runId), default);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                // The existing public query must read the committed result, not the tracked aggregate.
                var runs = await new ListMrpRunsQueryHandler(db).Handle(new("org-a", "env-a"), default);
                var result = Assert.Single(runs, x => x.RunId == runId);
                Assert.Equal(MrpRunStatus.Completed, result.Status);
                Assert.Null(result.FailureReason);
                Assert.Equal(1, result.SuggestionCount);
                Assert.Equal("product-engineering-http:1", result.ProductionEngineeringSnapshotSource);
                Assert.Equal(expectedSource, result.InventorySnapshotSource);
                Assert.Equal(mesDegraded, result.HasInputDegradation);
                Assert.Equal(mesDegraded ? ["mes-work-orders"] : Array.Empty<string>(), result.InputDegradationSources);
                Assert.Equal(["mps"], result.InputSources);
                var suggestion = await db.PlanningSuggestions.AsNoTracking().SingleAsync(x => x.MrpRunId == runId);
                Assert.Equal("FG", suggestion.SkuCode);
                Assert.Equal(2m, suggestion.Quantity);
            }
        }
        finally { await database.DropAsync(); }
    }

    // HTTP response fixtures exercise the production clients and source composition; upstream services are not hosted here.
    private sealed class UpstreamResponses(bool mesDegraded) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object data = path switch
            {
                "/api/business/v1/engineering/production-versions" => new
                {
                    items = new[] { new { productionVersionId = "PV-FG", skuCode = "FG", mbomVersionId = "BOM-FG", routingVersionId = "ROUTE-FG", isDefault = true } }
                },
                "/api/business/v1/engineering/manufacturing-boms" => new { items = Array.Empty<object>() },
                "/api/inventory/v1/availability" => new { skuCode = "FG", uomCode = "pcs", siteCode = "SITE", onHandQuantity = 0m, reservedQuantity = 0m, availableQuantity = 0m },
                "/api/business/v1/erp/purchase-orders" => new { items = Array.Empty<object>(), total = 0 },
                "/api/business/v1/mes/work-orders" when mesDegraded => throw new HttpRequestException("MES fixture unavailable"),
                "/api/business/v1/mes/work-orders" => new { items = Array.Empty<object>(), total = 0 },
                "/api/business/v1/master-data/resources/sku/FG" => new { code = "FG", active = true, baseUomCode = "pcs", manufacturingUomCode = "pcs", procurementType = "in-house", mrpType = "PD" },
                _ => throw new InvalidOperationException("Unexpected upstream request: " + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, data })
            });
        }
    }
}
