using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// L1 背景历史（质量域侧）的常规门禁测试：形状、确定性、幂等、隔离、分布与 fail-closed。
/// 全量规模下的真实数据库耗时实测在 <see cref="WorldHistoryQualitySeedPostgresTests"/>（env-gated）。
/// </summary>
public sealed class WorldHistoryQualitySeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库写入类用例的规模：足够跑出 NCR 全链，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_fact_stream_matches_the_world_bible_shape()
    {
        var facts = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 1.0d);
        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(AsOfDate, 1.0d);

        var completed = facts.Count(x => x.Status == WorldHistoryInspectionStatus.Completed);
        var nonconforming = facts.Where(x => x.HasNonconformance).ToArray();
        var scrapQuantity = nonconforming
            .Where(x => x.Disposition == WorldHistoryInspectionDisposition.Scrap)
            .Sum(x => x.DefectQuantity);
        var workOrderScrap = workOrderFacts.Sum(x => x.Plan.ScrapQuantity);

        foreach (var group in facts.GroupBy(x => x.SourceType).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"quality-world-history-tasks-{group.Key}={group.Count()}");
        }

        output.WriteLine($"quality-world-history-tasks-total={facts.Count}");
        output.WriteLine($"quality-world-history-completed={completed}");
        output.WriteLine($"quality-world-history-records={completed + nonconforming.Count(x => x.ReinspectedAtUtc is not null)}");
        output.WriteLine($"quality-world-history-reinspections={nonconforming.Count(x => x.ReinspectedAtUtc is not null)}");
        output.WriteLine($"quality-world-history-ncrs={nonconforming.Length}");
        output.WriteLine(FormattableString.Invariant(
            $"quality-world-history-nonconforming-rate={(double)nonconforming.Length / completed:P3}"));
        foreach (var disposition in new[]
                 {
                     WorldHistoryInspectionDisposition.Rework,
                     WorldHistoryInspectionDisposition.ConditionalRelease,
                     WorldHistoryInspectionDisposition.Scrap,
                 })
        {
            var count = nonconforming.Count(x => x.Disposition == disposition);
            output.WriteLine(FormattableString.Invariant(
                $"quality-world-history-disposition-{disposition}={count} ({(double)count / nonconforming.Length:P1})"));
        }

        output.WriteLine(FormattableString.Invariant($"quality-world-history-scrap-quantity={scrapQuantity}"));
        output.WriteLine(FormattableString.Invariant($"quality-world-history-work-order-scrap-quantity={workOrderScrap}"));

        // 设定集 §7：三条检验来源合计约 7000 条。
        Assert.InRange(facts.Count, 6000, 8000);
        Assert.Equal(workOrderFacts.Count(x => x.HasFinalInspection), facts.Count(x => x.SourceType == "operation"));
        Assert.True(WorldHistoryConsistencyValidator.WithinTolerance(
            nonconforming.Length, WorldHistoryQualitySpec.NonconformingRate, completed));
        Assert.True(scrapQuantity > 0m && scrapQuantity <= workOrderScrap);
    }

    [Fact]
    public void Full_scale_disposition_mix_matches_the_world_bible_targets()
    {
        var nonconforming = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 1.0d)
            .Where(x => x.HasNonconformance)
            .ToArray();

        Assert.True(WorldHistoryConsistencyValidator.WithinTolerance(
            nonconforming.Count(x => x.Disposition == WorldHistoryInspectionDisposition.Rework),
            WorldHistoryQualitySpec.ReworkDispositionShare / 100d,
            nonconforming.Length));
        Assert.True(WorldHistoryConsistencyValidator.WithinTolerance(
            nonconforming.Count(x => x.Disposition == WorldHistoryInspectionDisposition.ConditionalRelease),
            WorldHistoryQualitySpec.ConditionalReleaseDispositionShare / 100d,
            nonconforming.Length));
        Assert.True(WorldHistoryConsistencyValidator.WithinTolerance(
            nonconforming.Count(x => x.Disposition == WorldHistoryInspectionDisposition.Scrap),
            WorldHistoryQualitySpec.ScrapDispositionShare / 100d,
            nonconforming.Length));
    }

    [Fact]
    public void Scrap_dispositions_never_exceed_the_work_order_scrap_allowance()
    {
        var scrapAllowance = WorldHistoryPhase2Spec.BuildWorkOrderFacts(AsOfDate, 1.0d)
            .ToDictionary(x => x.Plan.WorkOrderNo, x => x.Plan.ScrapQuantity, StringComparer.Ordinal);

        foreach (var fact in WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 1.0d)
                     .Where(x => x.Disposition == WorldHistoryInspectionDisposition.Scrap))
        {
            Assert.Equal("operation", fact.SourceType);
            Assert.True(fact.DefectQuantity > 0m);
            Assert.True(fact.DefectQuantity <= scrapAllowance[fact.SourceDocumentId]);
        }
    }

    [Fact]
    public void All_fact_timestamps_stay_inside_the_history_window_and_off_sunday()
    {
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var fact in WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 0.2d))
        {
            var chain = new List<DateTimeOffset> { fact.CreatedAtUtc };
            if (fact.StartedAtUtc is { } started) chain.Add(started);
            if (fact.CompletedAtUtc is { } completed) chain.Add(completed);
            if (fact.NcrOpenedAtUtc is { } opened) chain.Add(opened);
            if (fact.NcrDispositionAtUtc is { } decided) chain.Add(decided);
            if (fact.ReinspectedAtUtc is { } reinspected) chain.Add(reinspected);
            if (fact.NcrClosedAtUtc is { } closed) chain.Add(closed);

            for (var index = 0; index < chain.Count; index++)
            {
                Assert.InRange(chain[index], lowerBound, upperBound);
                Assert.True(
                    WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(chain[index].UtcDateTime)),
                    $"{fact.SourceDocumentId} 的第 {index} 个时间戳落在周日。");
                if (index > 0)
                {
                    Assert.True(chain[index] >= chain[index - 1], $"{fact.SourceDocumentId} 时间链非单调。");
                }
            }
        }
    }

    [Fact]
    public void Fact_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 0.2d);
        var second = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 0.2d);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first, second);
    }

    [Fact]
    public void A_documents_content_is_independent_of_the_scale()
    {
        var small = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 0.1d)
            .ToDictionary(x => x.TriggerIdempotencyKey, StringComparer.Ordinal);
        var large = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 1.0d)
            .ToDictionary(x => x.TriggerIdempotencyKey, StringComparer.Ordinal);

        // 补产工单 WO-2026-R#### 是按「已发货订单池」的相对位置挑出来的（一期 WorldHistoryPhase2Spec），
        // 同一个补产号在不同 Scale 下挂的源订单本就不同，因此不参与缩放无关性比对。
        var shared = small.Keys
            .Intersect(large.Keys, StringComparer.Ordinal)
            .Where(key => !key.Contains("WO-2026-R", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(shared);
        var comparedOutcomes = 0;
        foreach (var key in shared)
        {
            // 单据内容（SKU / 数量 / 批次）按单据号取流，与本次生成的总量无关。
            Assert.Equal(small[key].SkuCode, large[key].SkuCode);
            Assert.Equal(small[key].Quantity, large[key].Quantity);
            Assert.Equal(small[key].BatchNo, large[key].BatchNo);

            // 判定与处置同样按单据号取流，但只有在两侧落点状态相同时才可比：
            // 一期的「已结案 / 在制 / 已下达」是按订单在时间轴上的相对位置排布的（设定集 §7），
            // 同一张单在不同 Scale 下位置不同，可能一侧还在制、根本没走到检验完成。
            // 时间轴与 NCR 流水号同理依赖落点，因此也不在比对范围内。
            if (small[key].Status != large[key].Status)
            {
                continue;
            }

            Assert.Equal(small[key].Disposition, large[key].Disposition);
            Assert.Equal(small[key].DefectQuantity, large[key].DefectQuantity);
            Assert.Equal(small[key].DefectReasonCode, large[key].DefectReasonCode);
            comparedOutcomes++;
        }

        Assert.True(comparedOutcomes > shared.Length / 2, "可比对的单据太少，缩放无关性没有被真正验证。");
    }

    [Fact]
    public async Task Seed_writes_the_full_chain_and_reruns_without_writing_anything()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var facts = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, SmallScale);
        output.WriteLine($"small-scale-tasks={first.InspectionTasksWritten}");
        output.WriteLine($"small-scale-records={first.InspectionRecordsWritten}");
        output.WriteLine($"small-scale-reinspections={first.ReinspectionRecordsWritten}");
        output.WriteLine($"small-scale-ncrs={first.NonconformanceReportsWritten}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"small-scale-sample: {line}");
        }

        Assert.Equal(3, first.InspectionPlansWritten);
        Assert.Equal(facts.Count, first.InspectionTasksWritten);
        Assert.Equal(facts.Count(x => x.HasRecord), first.InspectionRecordsWritten);
        Assert.Equal(facts.Count(x => x.HasNonconformance), first.NonconformanceReportsWritten);
        Assert.Equal(
            facts.Count(x => x.Disposition == WorldHistoryInspectionDisposition.Rework),
            first.ReinspectionRecordsWritten);

        Assert.Equal(0, second.InspectionPlansWritten);
        Assert.Equal(0, second.InspectionTasksWritten);
        Assert.Equal(0, second.InspectionRecordsWritten);
        Assert.Equal(0, second.NonconformanceReportsWritten);
        Assert.Equal(facts.Count, await db.InspectionTasks.CountAsync());
        Assert.Equal(
            first.InspectionRecordsWritten + first.ReinspectionRecordsWritten,
            await db.InspectionRecords.CountAsync());
    }

    [Fact]
    public async Task Seeded_documents_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var documentIds = await db.InspectionTasks.Select(x => x.SourceDocumentId).ToArrayAsync();
        var ncrCodes = await db.NonconformanceReports.Select(x => x.NcrCode).ToArrayAsync();
        var planCodes = await db.InspectionPlans.Select(x => x.PlanCode).ToArrayAsync();

        Assert.NotEmpty(ncrCodes);
        foreach (var value in documentIds.Concat(ncrCodes).Concat(planCodes))
        {
            Assert.DoesNotContain("-DEMO-", value, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", value, StringComparison.Ordinal);
        }

        Assert.All(ncrCodes, code => Assert.StartsWith("NCR-2026-", code, StringComparison.Ordinal));
        Assert.All(planCodes, code => Assert.StartsWith("IP-WB-", code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seeded_ncrs_carry_the_closure_reference_their_disposition_requires()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var reports = await db.NonconformanceReports.Include(x => x.MrbReviews).ToArrayAsync();
        Assert.NotEmpty(reports);
        foreach (var report in reports)
        {
            Assert.Equal("closed", report.Status);
            Assert.NotEmpty(report.MrbReviews);
            Assert.All(report.MrbReviews, review => Assert.Equal("approved", review.Decision));
            switch (report.DispositionType)
            {
                case "rework":
                    Assert.False(string.IsNullOrWhiteSpace(report.ReworkWorkOrderId));
                    Assert.StartsWith("WO-2026-", report.ReworkWorkOrderId!, StringComparison.Ordinal);
                    break;
                case "scrap":
                    Assert.Equal(WorldHistoryQualitySpec.ScrapMovementId(report.NcrCode), report.ScrapMovementId);
                    break;
                case "conditional-release":
                    Assert.Null(report.ScrapMovementId);
                    break;
                default:
                    Assert.Fail($"未预期的历史处置类型 '{report.DispositionType}'。");
                    break;
            }

            // NCR 的持有痕迹落在不合格品隔离区。
            Assert.Equal(WorldHistoryPhase2Spec.QualityHoldLocationCode, report.LocationCode);
        }
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_completed_task_loses_its_record()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var record = await db.InspectionRecords.FirstAsync(x => x.AttemptNumber == 1);
        db.InspectionRecords.Remove(record);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.NotEmpty(exception.Failures);
        Assert.Contains(exception.Failures, failure => failure.Contains("没有检验记录", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seed_leaves_the_reserved_leader_demo_plan_untouched()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");
        db.ChangeTracker.Clear();

        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var demoPlan = await db.InspectionPlans.SingleAsync(x => x.PlanCode == LeaderDemoSeedService.PlanCode);
        Assert.Equal("SKU-DEMO-001", demoPlan.SkuCode);
        Assert.Equal(4, await db.InspectionPlans.CountAsync());
    }

    [Fact]
    public void Trigger_idempotency_keys_are_unique_across_the_fact_stream()
    {
        var facts = WorldHistoryQualitySpec.BuildInspectionFacts(AsOfDate, 1.0d);

        Assert.Equal(facts.Count, facts.Select(x => x.TriggerIdempotencyKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            facts.Count(x => x.HasNonconformance),
            facts.Where(x => x.HasNonconformance).Select(x => x.NcrCode).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            WorldHistoryPhase2Spec.NonconformanceReportNo(facts.Count(x => x.HasNonconformance)),
            facts.Last(x => x.HasNonconformance).NcrCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-world-history-{Guid.CreateVersion7():N}")
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
