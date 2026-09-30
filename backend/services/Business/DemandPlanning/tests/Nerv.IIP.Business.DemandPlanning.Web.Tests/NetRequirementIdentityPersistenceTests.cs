using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

// ProviderBehavior / DomainInvariant: #4108, real service migrations and netting lifecycle.
public sealed class NetRequirementIdentityPersistenceTests
{
    [DemandPlanningRealPostgresFact]
    public async Task Migration_keeps_legacy_identity_unknown_and_preserves_explanation_and_sources_on_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_dp_net_identity_upgrade");
        try
        {
            await using var provider = Services(database.ConnectionString);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync("20260929094456_AddMrpInputChangeDemandType");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO demand_planning.planning_suggestions
                  (id, organization_id, environment_id, mrp_run_id, suggestion_type, sku_code, uom_code, site_code,
                   quantity, required_date, release_date, reason_code, status, created_at_utc,
                   gross_demand_quantity, on_hand_quantity, reserved_quantity, available_to_net_quantity,
                   scheduled_receipt_quantity, safety_stock_quantity, net_requirement_quantity, planned_quantity,
                   scrap_rate, yield_rate, primary_source_type, formula, uom_conversion_summary)
                VALUES
                  ('01900000-0000-7000-8000-000000000011', 'org-a', 'env-a', '01900000-0000-7000-8000-000000000012',
                   'planned-purchase', 'SKU', 'pcs', 'SITE', 12, DATE '2026-10-10', DATE '2026-10-10',
                   'component-net-requirement', 'Open', NOW(), 30, 0, 0, 0, 0, 0, 30, 12, 0, 1, 'sales-order', '30 - 0 = 30', '');
                INSERT INTO demand_planning.mrp_pegging_links
                  (id, planning_suggestion_id, pegging_type, demand_source_reference, parent_sku_code,
                   quantity, source_type, gross_demand_quantity, source_line_reference)
                VALUES
                  ('01900000-0000-7000-8000-000000000013', '01900000-0000-7000-8000-000000000011',
                   'demand', 'SO-30', 'SKU', 30, 'sales-order', 30, '10');
                """);
            await db.Database.MigrateAsync();
            var legacy = await db.PlanningSuggestions.AsNoTracking().Include(x => x.PeggingLinks).SingleAsync();
            Assert.Null(legacy.NetRequirementReference);
            Assert.Equal(30m, legacy.NetRequirementQuantity);
            Assert.Equal(12m, legacy.Quantity);
            Assert.Equal("30 - 0 = 30", legacy.Formula);
            var link = Assert.Single(legacy.PeggingLinks);
            Assert.Equal("SO-30", link.DemandSourceReference);
            Assert.Equal("10", link.SourceLineReference);

            // The nullable addition also rolls back without rewriting historical business facts.
            await db.Database.MigrateAsync("20260929094456_AddMrpInputChangeDemandType");
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            Assert.Null((await db.PlanningSuggestions.SingleAsync()).NetRequirementReference);
        }
        finally { await database.DropAsync(); }
    }

    [DemandPlanningRealPostgresFact]
    public async Task Netting_identity_survives_split_independent_stages_accept_reject_and_supersession_on_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_dp_net_identity");
        try
        {
            await using var provider = Services(database.ConnectionString);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            var split = await Execute(db, NetRequirementIdentityTests.SplitInput());
            Assert.Equal([12m, 12m, 6m], split.Select(x => x.Quantity).ToArray());
            Assert.All(split, x => Assert.Equal(30m, x.NetRequirementQuantity));
            var shared = Assert.Single(split.Select(x => x.NetRequirementReference).Distinct());
            Assert.NotNull(shared);
            Assert.NotEqual(Guid.Empty, shared);
            var splitIds = split.Select(x => x.Id).ToArray();
            var runId = split[0].MrpRunId;
            await new AcceptPlanningSuggestionCommandHandler(db).Handle(
                new(split[0].Id, "test", "document", "DOC-1"), default);
            await new RejectPlanningSuggestionCommandHandler(db).Handle(new(split[1].Id, "user", "same-reason"), default);
            await db.SaveChangesAsync();
            await Execute(db, NetRequirementIdentityTests.SplitInput());
            db.ChangeTracker.Clear();
            var savedSplit = await db.PlanningSuggestions.Where(x => splitIds.Contains(x.Id)).ToListAsync();
            Assert.All(savedSplit, x => { Assert.Equal(shared, x.NetRequirementReference); Assert.Equal(runId, x.MrpRunId); });
            Assert.Equal(PlanningSuggestionStatus.Accepted, savedSplit.Single(x => x.Id == splitIds[0]).Status);
            Assert.Equal(PlanningSuggestionStatus.Rejected, savedSplit.Single(x => x.Id == splitIds[1]).Status);
            Assert.Equal(PlanningSuggestionStatus.Superseded, savedSplit.Single(x => x.Id == splitIds[2]).Status);

            var components = (await Execute(db, NetRequirementIdentityTests.ComponentInput()))
                .Where(x => x.SkuCode == "COMPONENT").ToArray();
            Assert.Equal([5m, 3m], components.Select(x => x.Quantity).ToArray());
            Assert.Equal(2, components.Select(x => x.NetRequirementReference).Distinct().Count());
            Assert.All(components, x => Assert.NotNull(x.NetRequirementReference));

            var collision = await Execute(db, NetRequirementIdentityTests.CollisionInput());
            Assert.Equal(2, collision.Length);
            Assert.All(collision, x => { Assert.Equal(3m, x.Quantity); Assert.Equal(3m, x.NetRequirementQuantity); Assert.NotNull(x.NetRequirementReference); });
            Assert.Equal(2, collision.Select(x => x.NetRequirementReference).Distinct().Count());
            var references = collision.ToDictionary(x => x.Id, x => x.NetRequirementReference);
            foreach (var suggestion in collision)
                await new RejectPlanningSuggestionCommandHandler(db).Handle(new(suggestion.Id, "user", "same-reason"), default);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var rejected = await db.PlanningSuggestions.Where(x => x.MrpRunId == collision[0].MrpRunId).ToListAsync();
            Assert.All(rejected, x => { Assert.Equal(PlanningSuggestionStatus.Rejected, x.Status); Assert.Equal("same-reason", x.ReasonCode); Assert.Equal(references[x.Id], x.NetRequirementReference); });
        }
        finally { await database.DropAsync(); }
    }

    private static ServiceProvider Services(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
        return services.BuildServiceProvider();
    }

    private static async Task<PlanningSuggestion[]> Execute(ApplicationDbContext db, MrpCalculationInput input)
    {
        var run = MrpRun.Create(input.OrganizationId, input.EnvironmentId, input.HorizonStart, input.HorizonEnd);
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();
        await new ExecuteMrpRunCommandHandler(db, new SnapshotProvider(input)).Handle(new(run.Id), default);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return await db.PlanningSuggestions.Include(x => x.PeggingLinks).Where(x => x.MrpRunId == run.Id)
            .OrderBy(x => x.Id).ToArrayAsync();
    }

    private sealed class SnapshotProvider(MrpCalculationInput input) : IPlanningInputSnapshotProvider
    {
        public Task<PlanningInputSnapshotResult> GetSnapshotAsync(string organizationId, string environmentId,
            DateOnly horizonStart, DateOnly horizonEnd, CancellationToken cancellationToken) =>
            Task.FromResult(new PlanningInputSnapshotResult("fixture", "fixture", input.Demands, input.Availability,
                input.ProductionVersions, input.BomComponents, input.ScheduledReceipts, input.PlanningParameters, input.UomConversions));
    }
}
