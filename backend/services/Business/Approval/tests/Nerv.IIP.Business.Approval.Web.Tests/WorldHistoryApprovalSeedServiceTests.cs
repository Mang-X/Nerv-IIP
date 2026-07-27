using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Infrastructure;
using Nerv.IIP.Business.Approval.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Approval.Web.Tests;

/// <summary>
/// L1 背景历史（审批域侧）的常规门禁测试：形状、确定性、幂等、隔离、待办归属与 fail-closed。
/// asOfDate 覆盖 5 个日期（防单日期盲区）：周一 / 周日次日 / 未来周日 / 春节段 / 月末冲量段。
/// </summary>
public sealed class WorldHistoryApprovalSeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库写入类用例的规模：足够覆盖「通过 / 驳回 / 待办 / NCR 引用」全部落点，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_fact_stream_matches_the_world_bible_shape()
    {
        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(AsOfDate, 1.0d);

        var purchase = facts.Where(x => x.TemplateCode == WorldHistoryApprovalSpec.PurchaseTemplateCode).ToArray();
        var ncr = facts.Where(x => x.TemplateCode == WorldHistoryApprovalSpec.NcrTemplateCode).ToArray();
        var pending = facts.Count(x => x.Outcome == WorldHistoryApprovalOutcome.Pending);
        var rejected = facts.Count(x => x.Outcome == WorldHistoryApprovalOutcome.Rejected);
        var completed = facts.Count(x => x.IsCompleted);

        output.WriteLine($"approval-world-history-chains-total={facts.Count}");
        output.WriteLine($"approval-world-history-purchase={purchase.Length}");
        output.WriteLine($"approval-world-history-ncr={ncr.Length}");
        output.WriteLine($"approval-world-history-pending={pending}");
        output.WriteLine(FormattableString.Invariant(
            $"approval-world-history-rejected={rejected} ({(double)rejected / completed:P2} of completed)"));

        // 设定集 §7：约 480 张采购订单（截至 7 月末约一半年度量走完 → 约 490 张）。
        Assert.Equal(WorldHistoryProcurementSpec.TotalPurchaseOrders(AsOfDate, 1.0d), purchase.Length);
        Assert.InRange(purchase.Length, 400, 600);
        Assert.Equal(WorldHistoryApprovalSpec.NcrReferenceCount(AsOfDate, 1.0d), ncr.Length);
        Assert.True(ncr.Length > 50, "全量规模下 NCR 处置审批不应只有零星几条。");

        // 待办 5–8 条口径，全部挂在 admin 名下的采购订单审批上。
        Assert.Equal(WorldHistoryApprovalSpec.PendingTargetCount, pending);
        Assert.All(
            facts.Where(x => x.Outcome == WorldHistoryApprovalOutcome.Pending),
            fact =>
            {
                Assert.Equal(WorldHistoryApprovalSpec.AdminUserId, fact.ApproverUserId);
                Assert.Equal(WorldHistoryApprovalSpec.PurchaseDocumentType, fact.DocumentType);
            });

        // 驳回约 5%（只在已完成的采购审批上出现；NCR 处置审批与源域关单事实保持一致，全部通过）。
        Assert.InRange((double)rejected / completed, 0.02, 0.10);
        Assert.All(ncr, fact => Assert.Equal(WorldHistoryApprovalOutcome.Approved, fact.Outcome));
    }

    [Fact]
    public void Fact_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistoryApprovalSpec.BuildApprovalFacts(AsOfDate, 0.2d);
        var second = WorldHistoryApprovalSpec.BuildApprovalFacts(AsOfDate, 0.2d);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first, second);
    }

    [Fact]
    public void All_fact_timestamps_stay_inside_the_history_window_and_off_sunday()
    {
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var fact in WorldHistoryApprovalSpec.BuildApprovalFacts(AsOfDate, 0.2d))
        {
            Assert.InRange(fact.StartedAtUtc, lowerBound, upperBound);
            Assert.True(
                WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(fact.StartedAtUtc.UtcDateTime)),
                $"{fact.DocumentId} 的发起时间落在周日。");
            if (fact.DecidedAtUtc is { } decidedAtUtc)
            {
                Assert.InRange(decidedAtUtc, lowerBound, upperBound);
                Assert.True(decidedAtUtc >= fact.StartedAtUtc, $"{fact.DocumentId} 时间链非单调。");
                Assert.True(
                    WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(decidedAtUtc.UtcDateTime)),
                    $"{fact.DocumentId} 的决策时间落在周日。");
            }
            else
            {
                Assert.Equal(WorldHistoryApprovalOutcome.Pending, fact.Outcome);
            }
        }
    }

    [Fact]
    public void Actors_are_world_bible_people()
    {
        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(AsOfDate, 0.2d);
        var purchaserIds = WorldHistoryApprovalSpec.Purchasers.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);
        var engineerIds = WorldHistoryApprovalSpec.QualityEngineers.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);

        foreach (var fact in facts)
        {
            if (fact.TemplateCode == WorldHistoryApprovalSpec.PurchaseTemplateCode)
            {
                Assert.Contains(fact.StartedByUserId, purchaserIds);
                Assert.Equal(WorldHistoryApprovalSpec.AdminUserId, fact.ApproverUserId);
            }
            else
            {
                Assert.Contains(fact.StartedByUserId, engineerIds);
                Assert.Equal(WorldHistoryApprovalSpec.QualitySupervisorUserId, fact.ApproverUserId);
            }

            Assert.StartsWith("user:user-", fact.StartedByActorRef, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 防 #1151 单日期盲区：5 个 asOfDate 全量走「seed → 重跑零写入 → 待办归属」。
    /// 春节段（2026-02-16）小规模下 NCR 引用条数为 0 属于 spec 期望值，断言按 spec 对齐、不硬 NotEmpty。
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
        var seed = new WorldHistoryApprovalSeedService(db);
        var asOfDate = new DateOnly(year, month, day);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(asOfDate, SmallScale);
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-chains={first.ChainsWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-purchase={first.PurchaseChainsWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-ncr={first.NcrChainsWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-pending={first.PendingChainsWritten}");
        output.WriteLine($"small-scale-{asOfDate:yyyy-MM-dd}-rejected={first.RejectedChainsWritten}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"small-scale-sample: {line}");
        }

        Assert.Equal(2, first.TemplatesWritten);
        Assert.Equal(facts.Count, first.ChainsWritten);
        Assert.Equal(
            facts.Count(x => x.TemplateCode == WorldHistoryApprovalSpec.PurchaseTemplateCode),
            first.PurchaseChainsWritten);
        Assert.Equal(
            WorldHistoryApprovalSpec.NcrReferenceCount(asOfDate, SmallScale),
            first.NcrChainsWritten);
        Assert.Equal(facts.Count(x => x.Outcome == WorldHistoryApprovalOutcome.Pending), first.PendingChainsWritten);
        Assert.Equal(facts.Count(x => x.Outcome == WorldHistoryApprovalOutcome.Rejected), first.RejectedChainsWritten);

        Assert.Equal(0, second.TemplatesWritten);
        Assert.Equal(0, second.ChainsWritten);
        Assert.Equal(facts.Count, await db.ApprovalChains.CountAsync());

        // 待办挂在 admin 名下：与工作台 ListPendingApprovalTasksQuery 的过滤口径（步骤 pending + approver）一致。
        var pendingChains = await db.ApprovalChains
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.Status == ApprovalChainStatuses.Pending)
            .ToArrayAsync();
        Assert.Equal(first.PendingChainsWritten, pendingChains.Length);
        Assert.All(pendingChains, chain =>
            Assert.Contains(chain.Steps, step =>
                step.Status == ApprovalStepStatuses.Pending
                && step.ApproverType == WorldHistoryApprovalSpec.ActorTypeUser
                && step.ApproverRef == WorldHistoryApprovalSpec.AdminUserId));
    }

    [Fact]
    public async Task Completed_chains_carry_decision_actor_and_backdated_times()
    {
        await using var db = CreateDbContext();
        await new WorldHistoryApprovalSeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var chains = await db.ApprovalChains
            .AsNoTracking()
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .Where(x => x.Status != ApprovalChainStatuses.Pending)
            .ToArrayAsync();
        Assert.NotEmpty(chains);

        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        foreach (var chain in chains)
        {
            Assert.NotNull(chain.CompletedAtUtc);
            Assert.True(chain.CompletedAtUtc <= upperBound, $"{chain.DocumentReference.DocumentId} 完成时间未回填到历史窗内。");
            Assert.True(chain.StartedAtUtc <= chain.CompletedAtUtc, $"{chain.DocumentReference.DocumentId} 时间链非单调。");

            var decision = Assert.Single(chain.Decisions);
            Assert.Equal(chain.CompletedAtUtc, decision.DecidedAtUtc);
            Assert.Equal(WorldHistoryApprovalSpec.ActorTypeUser, decision.ActorType);
            Assert.False(string.IsNullOrWhiteSpace(decision.ActorRef));

            var step = Assert.Single(chain.Steps);
            Assert.Equal(chain.CompletedAtUtc, step.ResolvedAtUtc);

            if (chain.Status == ApprovalChainStatuses.Rejected)
            {
                // 驳回原因是界面必填展示，且必须是中文而不是内部英文字面量。
                Assert.False(string.IsNullOrWhiteSpace(decision.Comment));
                Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", decision.Comment!);
            }
        }
    }

    [Fact]
    public async Task Seeded_documents_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistoryApprovalSeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var documentIds = await db.ApprovalChains.Select(x => x.DocumentReference.DocumentId).ToArrayAsync();
        var templateCodes = await db.ApprovalTemplates.Select(x => x.TemplateCode).ToArrayAsync();

        Assert.NotEmpty(documentIds);
        foreach (var value in documentIds.Concat(templateCodes))
        {
            Assert.DoesNotContain("-DEMO-", value, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", value, StringComparison.Ordinal);
        }

        Assert.All(documentIds, id => Assert.True(
            id.StartsWith("PO-2026-", StringComparison.Ordinal) || id.StartsWith("NCR-2026-", StringComparison.Ordinal),
            $"{id} 不在世界观号段内。"));
        Assert.All(templateCodes, code => Assert.StartsWith("APT-WB-", code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ncr_references_stay_inside_the_calibrated_lower_bound_segment()
    {
        // 用 2026-07-27（NCR 引用条数 > 0 的日期）避免用例空转。
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await new WorldHistoryApprovalSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        // NCR-2026-#### 在质量域从 1 连续编号；只要引用序号 ≤ 标定下界 K，引用就全部真实存在。
        var bound = WorldHistoryApprovalSpec.NcrReferenceCount(asOfDate, SmallScale);
        Assert.True(bound > 0, "所选 asOfDate 的 NCR 引用下界不应为 0，否则本用例没有覆盖到 NCR 审批。");
        var ncrIds = await db.ApprovalChains
            .Where(x => x.TemplateCode == WorldHistoryApprovalSpec.NcrTemplateCode)
            .Select(x => x.DocumentReference.DocumentId)
            .ToArrayAsync();

        Assert.Equal(bound, ncrIds.Length);
        Assert.NotEmpty(ncrIds);
        Assert.Equal(
            Enumerable.Range(1, bound).Select(WorldHistoryApprovalSpec.NonconformanceReportNo).ToHashSet(StringComparer.Ordinal),
            ncrIds.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_planned_chain_disappears()
    {
        await using var db = CreateDbContext();
        await new WorldHistoryApprovalSeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var victim = await db.ApprovalChains.FirstAsync();
        db.ApprovalChains.Remove(victim);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryApprovalConsistencyException>(() =>
            new WorldHistoryApprovalConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.NotEmpty(exception.Failures);
        Assert.Contains(exception.Failures, failure => failure.Contains("未落库", StringComparison.Ordinal));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"approval-world-history-{Guid.CreateVersion7():N}")
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
