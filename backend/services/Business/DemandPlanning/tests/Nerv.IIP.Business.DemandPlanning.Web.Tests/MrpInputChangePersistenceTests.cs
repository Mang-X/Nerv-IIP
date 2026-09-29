using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpInputChangeAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class MrpInputChangePersistenceTests
{
    [DemandPlanningRealPostgresFact]
    public async Task Migration_preserves_legacy_fact_with_unknown_demand_type_on_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_dp_input_change_upgrade");
        try
        {
            var services = new ServiceCollection();
            services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                database.ConnectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync("20260929065700_AddMrpInputChanges");
            var factId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO demand_planning.mrp_input_changes
                    (id, organization_id, environment_id, input_type, source_reference, source_line_reference,
                     occurred_at_utc, operation, previously_eligible, currently_eligible)
                VALUES ({factId}, 'org-a', 'env-a', 'demand', 'DELETED-1', '', {DateTimeOffset.UtcNow},
                        'Deleted', TRUE, FALSE)
                """);

            await db.Database.MigrateAsync();
            var legacy = await db.MrpInputChanges.AsNoTracking().SingleAsync(x => x.Id == new MrpInputChangeId(factId));
            Assert.Null(legacy.DemandType);
            Assert.Equal("DELETED-1", legacy.SourceReference);
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [DemandPlanningRealPostgresFact]
    public async Task Created_moved_and_deleted_inputs_remain_queryable_by_scope_time_and_either_interval_on_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_dp_input_changes");
        try
        {
            var services = new ServiceCollection();
            services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                database.ConnectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();

            var oldDate = new DateOnly(2026, 10, 10);
            var newDate = new DateOnly(2026, 12, 10);
            var time = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
            db.MrpInputChanges.AddRange(
                MrpInputChange.Record("org-a", "env-a", "demand", "sales-order", "SO-1", "10", time,
                    MrpInputChangeOperation.Created, null, null, false, oldDate, oldDate, true),
                MrpInputChange.Record("org-a", "env-a", "demand", "sales-order", "SO-1", "10", time.AddMinutes(1),
                    MrpInputChangeOperation.Updated, oldDate, oldDate, true, newDate, newDate, true),
                MrpInputChange.Record("org-a", "env-a", "demand", "sales-order", "SO-1", "10", time.AddMinutes(2),
                    MrpInputChangeOperation.Deleted, newDate, newDate, true, null, null, false),
                MrpInputChange.Record("org-b", "env-a", "demand", "sales-order", "SO-1", "10", time.AddMinutes(1),
                    MrpInputChangeOperation.Updated, oldDate, oldDate, true, newDate, newDate, true),
                MrpInputChange.Record("org-a", "env-b", "demand", "sales-order", "SO-1", "10", time.AddMinutes(1),
                    MrpInputChangeOperation.Updated, oldDate, oldDate, true, newDate, newDate, true),
                MrpInputChange.Record("org-a", "env-a", "demand", "sales-order", "SO-1", "10", time.AddMinutes(-1),
                    MrpInputChangeOperation.Created, null, null, false, oldDate, oldDate, true),
                MrpInputChange.Record("org-a", "env-a", "demand", "sales-order", "SO-1", "10", time.AddMinutes(3),
                    MrpInputChangeOperation.Deleted, newDate, newDate, true, null, null, false));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var scoped = db.MrpInputChanges.AsNoTracking().Where(x =>
                x.OrganizationId == "org-a" && x.EnvironmentId == "env-a" &&
                x.OccurredAtUtc >= time && x.OccurredAtUtc < time.AddMinutes(3));
            Assert.Equal(3, await scoped.CountAsync());
            Assert.Equal(3, await scoped.Where(x => x.InputType == "demand" &&
                x.DemandType == "sales-order" && x.SourceReference == "SO-1" && x.SourceLineReference == "10").CountAsync());

            var oldHorizon = await scoped.Where(x =>
                (x.PreviouslyEligible && x.PreviousStartDate <= oldDate && x.PreviousEndDate >= oldDate) ||
                (x.CurrentlyEligible && x.CurrentStartDate <= oldDate && x.CurrentEndDate >= oldDate))
                .OrderBy(x => x.OccurredAtUtc).ToListAsync();
            Assert.Equal([MrpInputChangeOperation.Created, MrpInputChangeOperation.Updated],
                oldHorizon.Select(x => x.Operation).ToArray());

            var newHorizon = await scoped.Where(x =>
                (x.PreviouslyEligible && x.PreviousStartDate <= newDate && x.PreviousEndDate >= newDate) ||
                (x.CurrentlyEligible && x.CurrentStartDate <= newDate && x.CurrentEndDate >= newDate))
                .OrderBy(x => x.OccurredAtUtc).ToListAsync();
            Assert.Equal([MrpInputChangeOperation.Updated, MrpInputChangeOperation.Deleted],
                newHorizon.Select(x => x.Operation).ToArray());
            Assert.False(newHorizon[^1].CurrentlyEligible);
            Assert.Null(newHorizon[^1].CurrentStartDate);

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await new CreateOrUpdateDemandSourceCommandHandler(db).Handle(
                    new("org-a", "env-a", "manual", "ROLLBACK-1", "SKU", "pcs", "SITE", 10, oldDate), default);
                await db.SaveChangesAsync();
                Assert.Single(await db.MrpInputChanges.Where(x => x.SourceReference == "ROLLBACK-1").ToListAsync());
                await transaction.RollbackAsync();
            }

            db.ChangeTracker.Clear();
            Assert.Empty(await db.DemandSources.Where(x => x.SourceReference == "ROLLBACK-1").ToListAsync());
            Assert.Empty(await db.MrpInputChanges.Where(x => x.SourceReference == "ROLLBACK-1").ToListAsync());

            var create = new CreateOrUpdateDemandSourceCommandHandler(db);
            var manualId = await create.Handle(
                new("org-a", "env-a", "manual", "SAME-1", "SKU", "pcs", "SITE", 10, oldDate), default);
            await db.SaveChangesAsync();
            await create.Handle(
                new("org-a", "env-a", "safety-stock", "SAME-1", "SKU", "pcs", "SITE", 10, oldDate), default);
            await db.SaveChangesAsync();
            await create.Handle(
                new("org-a", "env-a", "manual", "SAME-1", "SKU", "pcs", "SITE", 10, newDate), default);
            await db.SaveChangesAsync();
            await new CancelDemandSourceCommandHandler(db).Handle(new("org-a", "env-a", manualId), default);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var sameReference = await db.MrpInputChanges.AsNoTracking()
                .Where(x => x.OrganizationId == "org-a" && x.EnvironmentId == "env-a"
                    && x.InputType == "demand" && x.SourceReference == "SAME-1")
                .ToListAsync();
            Assert.Equal(3, sameReference.Count(x => x.DemandType == "manual"));
            Assert.Single(sameReference, x => x.DemandType == "safety-stock");
            Assert.Contains(sameReference, x => x.DemandType == "manual"
                && x.Operation == MrpInputChangeOperation.Deleted && x.PreviousStartDate == newDate);
            Assert.Empty(await db.DemandSources.Where(x => x.DemandType == "manual" && x.SourceReference == "SAME-1").ToListAsync());
        }
        finally
        {
            await database.DropAsync();
        }
    }
}
