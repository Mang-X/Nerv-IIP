using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

/// <summary>
/// L1 背景历史（计划域侧）的常规门禁测试：形状、确定性、幂等、隔离、与 MES 工单号配对、fail-closed。
/// </summary>
public sealed class WorldHistoryPlanningSeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库写入类用例的规模：足够覆盖全链，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_fact_stream_matches_the_world_bible_shape()
    {
        var facts = WorldHistoryPlanningSpec.BuildPlanningFacts(AsOfDate, 1.0d);

        var accepted = facts.MrpRuns.SelectMany(x => x.Suggestions).Count(x => x.IsAccepted);
        var open = facts.MrpRuns.SelectMany(x => x.Suggestions).Count(x => !x.IsAccepted);
        var historicalAccepted = facts.HistoricalMrpRuns.SelectMany(x => x.Suggestions).Count(x => x.IsAccepted);
        output.WriteLine($"planning-world-history-demands={facts.Demands.Count}");
        output.WriteLine($"planning-world-history-forecasts={facts.Forecasts.Count}");
        output.WriteLine($"planning-world-history-mps-buckets={facts.MpsBuckets.Count}");
        output.WriteLine($"planning-world-history-mrp-runs={facts.MrpRuns.Count}");
        output.WriteLine($"planning-world-history-historical-mrp-runs={facts.HistoricalMrpRuns.Count}");
        output.WriteLine($"planning-world-history-suggestions-accepted={accepted}");
        output.WriteLine($"planning-world-history-suggestions-open={open}");
        output.WriteLine($"planning-world-history-historical-suggestions-accepted={historicalAccepted}");

        // 设定集 §7：周均约 105 单 × 10 周窗口。
        Assert.InRange(facts.Demands.Count, 700, 1500);
        Assert.Equal(WorldHistoryPlanningSpec.DemandWindowWeeks, facts.MrpRuns.Count);
        Assert.InRange(open, 1, WorldHistoryPlanningSpec.ForecastSkus.Count);
        Assert.Equal(facts.Demands.Count(x => !x.IsCancelled), accepted);
        Assert.Equal(
            WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d)
                .Count(plan => plan.OrderDate < facts.WindowStart && plan.Stage != WorldHistoryOrderStage.Cancelled),
            historicalAccepted);
        Assert.NotEmpty(facts.HistoricalMrpRuns);
        Assert.NotEmpty(facts.MpsBuckets);
        Assert.NotEmpty(facts.Forecasts);
    }

    [Fact]
    public void Fact_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistoryPlanningSpec.BuildPlanningFacts(AsOfDate, 0.2d);
        var second = WorldHistoryPlanningSpec.BuildPlanningFacts(AsOfDate, 0.2d);

        Assert.Equal(first.Demands, second.Demands);
        Assert.Equal(first.Forecasts, second.Forecasts);
        Assert.Equal(first.MpsBuckets, second.MpsBuckets);
        Assert.Equal(first.MrpRuns.Count, second.MrpRuns.Count);
        Assert.Equal(first.HistoricalMrpRuns.Count, second.HistoricalMrpRuns.Count);
        for (var index = 0; index < first.MrpRuns.Count; index++)
        {
            var left = first.MrpRuns[index];
            var right = second.MrpRuns[index];
            Assert.Equal(left.HorizonStart, right.HorizonStart);
            Assert.Equal(left.HorizonEnd, right.HorizonEnd);
            Assert.Equal(left.CreatedAtUtc, right.CreatedAtUtc);
            Assert.Equal(left.CompletedAtUtc, right.CompletedAtUtc);
            Assert.Equal(left.InputSources, right.InputSources);
            Assert.Equal(left.Suggestions, right.Suggestions);
        }

        for (var index = 0; index < first.HistoricalMrpRuns.Count; index++)
        {
            var left = first.HistoricalMrpRuns[index];
            var right = second.HistoricalMrpRuns[index];
            Assert.Equal(left.HorizonStart, right.HorizonStart);
            Assert.Equal(left.HorizonEnd, right.HorizonEnd);
            Assert.Equal(left.CreatedAtUtc, right.CreatedAtUtc);
            Assert.Equal(left.CompletedAtUtc, right.CompletedAtUtc);
            Assert.Equal(left.InputSources, right.InputSources);
            Assert.Equal(left.Suggestions, right.Suggestions);
        }
    }

    [Fact]
    public void Accepted_suggestions_pair_with_the_shared_mes_work_order_formula()
    {
        var facts = WorldHistoryPlanningSpec.BuildPlanningFacts(AsOfDate, 0.2d);

        foreach (var fact in facts.MrpRuns
                     .Concat(facts.HistoricalMrpRuns)
                     .SelectMany(x => x.Suggestions)
                     .Where(x => x.IsAccepted))
        {
            // SO-2026-x 的需求 → MES 侧同一公式的 WO-2026-x。
            var index = int.Parse(fact.DemandSourceReference["SO-2026-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(WorldHistorySpec.WorkOrderNo(index), fact.DownstreamDocumentId);
            Assert.Equal(WorldHistorySpec.SalesOrderNo(index), fact.DemandSourceReference);
        }
    }

    [Fact]
    public async Task Seeded_sales_suggestions_use_stable_ids_and_keep_their_demand_pegging()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var fact = WorldHistoryPlanningSpec.BuildPlanningFacts(AsOfDate, SmallScale)
            .MrpRuns
            .SelectMany(run => run.Suggestions)
            .First(suggestion => suggestion.IsAccepted);
        var suggestion = await db.PlanningSuggestions
            .Include(candidate => candidate.PeggingLinks)
            .SingleAsync(candidate => candidate.AcceptedDownstreamDocumentId == fact.DownstreamDocumentId);

        Assert.Equal(
            "d1e230af-fe8c-a75b-aea2-02ed501caab6",
            WorldHistoryPlanningSpec.PlanningSuggestionIdForSalesOrder("SO-2026-00001").ToString());
        Assert.Equal(
            WorldHistoryPlanningSpec.PlanningSuggestionIdForSalesOrder(fact.DemandSourceReference).ToString(),
            suggestion.Id.ToString());
        Assert.Contains(
            suggestion.PeggingLinks,
            link => link.PeggingType == "demand" &&
                link.DemandSourceReference == fact.DemandSourceReference &&
                link.SourceType == "sales");
    }

    [Fact]
    public async Task Seed_backfills_historical_order_suggestions_without_expanding_the_active_demand_window()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var windowStart = WorldHistoryPlanningSpec.ResolveWindowStart(AsOfDate);
        var historicalPlan = WorldHistorySpec.BuildOrderPlans(AsOfDate, SmallScale)
            .First(plan => plan.HasWorkOrder &&
                plan.Stage != WorldHistoryOrderStage.Cancelled &&
                plan.OrderDate < windowStart);
        var suggestion = await db.PlanningSuggestions
            .Include(candidate => candidate.PeggingLinks)
            .SingleOrDefaultAsync(candidate => candidate.AcceptedDownstreamDocumentId == historicalPlan.WorkOrderNo);

        Assert.NotNull(suggestion);
        Assert.Equal(
            WorldHistoryPlanningSpec.PlanningSuggestionIdForSalesOrder(historicalPlan.SalesOrderNo).ToString(),
            suggestion!.Id.ToString());
        Assert.Contains(
            suggestion.PeggingLinks,
            link => link.DemandSourceReference == historicalPlan.SalesOrderNo && link.SourceType == "sales");
        Assert.Equal(
            WorldHistoryPlanningSpec.BuildPlanningFacts(AsOfDate, SmallScale).Demands.Count,
            await db.DemandSources.CountAsync());
        Assert.True(await db.MrpRuns.CountAsync() > WorldHistoryPlanningSpec.DemandWindowWeeks);
    }

    [Fact]
    public void Planning_department_roster_matches_the_master_data_world_bible_formula()
    {
        // MasterData WorldBibleSpec.BuildEmployees()：生产部 28 人在前，计划部 ordinal 28–31。
        Assert.Equal(4, WorldHistoryPlanningSpec.Planners.Count);
        Assert.Equal("user-emp-029", WorldHistoryPlanningSpec.PlanningSupervisor.UserId);
        Assert.Equal("计划主管", WorldHistoryPlanningSpec.PlanningSupervisor.RoleName);
        Assert.Equal("周凤霞", WorldHistoryPlanningSpec.PlanningSupervisor.Name);
        Assert.Equal(new[] { "user-emp-030", "user-emp-031", "user-emp-032" }, WorldHistoryPlanningSpec.Planners.Skip(1).Select(x => x.UserId));
        Assert.Equal(new[] { "吴德华", "徐建国", "孙春梅" }, WorldHistoryPlanningSpec.Planners.Skip(1).Select(x => x.Name));
    }

    /// <summary>
    /// 演示走查缺口：需求与计划页无数据。全链写入 + 幂等重跑零写入，
    /// 且对任意 asOfDate（含周日、春节段、未来周一）成立；量以 spec 事实流为准，不空断。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 8, 2)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 7, 31)]
    public async Task Seed_writes_the_full_chain_and_reruns_without_writing_anything(int year, int month, int day)
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);
        var asOfDate = new DateOnly(year, month, day);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var facts = WorldHistoryPlanningSpec.BuildPlanningFacts(asOfDate, SmallScale);
        var allMrpRuns = facts.MrpRuns.Concat(facts.HistoricalMrpRuns).ToArray();
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-demands={first.DemandSourcesWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-suggestions={first.PlanningSuggestionsWritten}");

        Assert.Equal(facts.Demands.Count, first.DemandSourcesWritten);
        Assert.Equal(facts.Forecasts.Count, first.ForecastInputsWritten);
        Assert.Equal(facts.MpsBuckets.Count, first.MpsBucketsWritten);
        Assert.Equal(allMrpRuns.Length, first.MrpRunsWritten);
        Assert.Equal(allMrpRuns.Sum(x => x.Suggestions.Count), first.PlanningSuggestionsWritten);

        Assert.Equal(0, second.DemandSourcesWritten);
        Assert.Equal(0, second.ForecastInputsWritten);
        Assert.Equal(0, second.MpsBucketsWritten);
        Assert.Equal(0, second.MrpRunsWritten);
        Assert.Equal(0, second.PlanningSuggestionsWritten);
        Assert.Equal(facts.Demands.Count, await db.DemandSources.CountAsync());
        Assert.Equal(allMrpRuns.Sum(x => x.Suggestions.Count), await db.PlanningSuggestions.CountAsync());
    }

    [Fact]
    public async Task Seeded_documents_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var references = await db.DemandSources.Select(x => x.SourceReference).ToArrayAsync();
        var downstream = await db.PlanningSuggestions
            .Where(x => x.AcceptedDownstreamDocumentId != null)
            .Select(x => x.AcceptedDownstreamDocumentId!)
            .ToArrayAsync();
        var forecasts = await db.ForecastInputs.Select(x => x.ForecastReference).ToArrayAsync();

        Assert.NotEmpty(references);
        Assert.NotEmpty(downstream);
        foreach (var value in references.Concat(downstream).Concat(forecasts))
        {
            Assert.DoesNotContain("-DEMO-", value, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", value, StringComparison.Ordinal);
        }

        Assert.All(references, value => Assert.StartsWith("SO-2026-", value, StringComparison.Ordinal));
        Assert.All(downstream, value => Assert.StartsWith("WO-2026-", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seeded_timestamps_stay_inside_the_history_window_and_monotonic()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var demand in await db.DemandSources.ToArrayAsync())
        {
            Assert.InRange(demand.CreatedAtUtc, lowerBound, upperBound);
            Assert.True(demand.UpdatedAtUtc >= demand.CreatedAtUtc);
        }

        foreach (var run in await db.MrpRuns.ToArrayAsync())
        {
            Assert.InRange(run.CreatedAtUtc, lowerBound, upperBound);
            Assert.True(run.CreatedAtUtc <= run.StartedAtUtc && run.StartedAtUtc <= run.CompletedAtUtc);
            Assert.InRange(run.CompletedAtUtc!.Value, lowerBound, upperBound);
        }

        foreach (var suggestion in await db.PlanningSuggestions.Where(x => x.Status == PlanningSuggestionStatus.Accepted).ToArrayAsync())
        {
            Assert.InRange(suggestion.CreatedAtUtc, lowerBound, upperBound);
            Assert.True(suggestion.AcceptedAtUtc >= suggestion.CreatedAtUtc);
            Assert.InRange(suggestion.AcceptedAtUtc!.Value, lowerBound, upperBound);
        }
    }

    [Fact]
    public async Task Released_mps_buckets_carry_planner_names_from_the_roster()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var released = await db.MasterProductionSchedules
            .Where(x => x.Status == MasterProductionScheduleStatus.Released)
            .ToArrayAsync();
        Assert.NotEmpty(released);
        var plannerNames = WorldHistoryPlanningSpec.Planners.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(released, bucket =>
        {
            Assert.Contains(bucket.ReviewedBy!, plannerNames);
            Assert.Equal(WorldHistoryPlanningSpec.PlanningSupervisor.Name, bucket.ReleasedBy);
        });
    }

    [Fact]
    public async Task Validator_fails_closed_when_an_accepted_suggestion_loses_its_pairing()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var demand = await db.DemandSources.FirstAsync(x => x.SourceStatus == "active");
        db.DemandSources.Remove(demand);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.NotEmpty(exception.Failures);
        Assert.Contains(exception.Failures, failure => failure.Contains("缺失", StringComparison.Ordinal));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"planning-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryTestMediator());
    }

    private sealed class WorldHistoryTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
