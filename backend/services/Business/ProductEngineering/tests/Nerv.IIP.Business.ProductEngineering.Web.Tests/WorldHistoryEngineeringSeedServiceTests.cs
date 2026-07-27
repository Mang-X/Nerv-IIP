using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;
using System.Globalization;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

/// <summary>
/// L1 背景历史（工程域侧）的门禁测试：形状、确定性、幂等、号段、状态分布、受影响版本与 fail-closed。
///
/// 库写入类用例一律先铺 L0（<see cref="WorldBibleSeedService"/>）再铺 L1，
/// 这样校验器的「受影响版本必须指得上 L0 真实版本」那一条才真正跑起来。
/// </summary>
public sealed class WorldHistoryEngineeringSeedServiceTests(ITestOutputHelper output)
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";

    /// <summary>设定集基准演示日（上线日 + 约 29 周）。</summary>
    private static readonly DateOnly ReferenceDate = new(2026, 7, 26);

    [Fact]
    public void Full_scale_fact_stream_matches_the_world_bible_shape()
    {
        var changes = WorldHistoryEngineeringSpec.BuildChangeFacts(ReferenceDate, 1.0d);
        var documents = WorldHistoryEngineeringSpec.BuildDocumentFacts(ReferenceDate, 1.0d);

        output.WriteLine($"engineering-world-history-changes={changes.Count}");
        output.WriteLine($"engineering-world-history-documents={documents.Count}");
        foreach (var group in changes.GroupBy(x => x.State).OrderBy(x => x.Key))
        {
            output.WriteLine(FormattableString.Invariant(
                $"engineering-world-history-change-state-{group.Key}={group.Count()} ({(double)group.Count() / changes.Count:P1})"));
        }

        foreach (var group in documents.GroupBy(x => x.DocumentType, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"engineering-world-history-document-type-{group.Key}={group.Count()}");
        }

        output.WriteLine($"engineering-world-history-documents-archived={documents.Count(x => x.IsArchived)}");
        output.WriteLine($"engineering-world-history-affected-versions={changes.Sum(x => x.AffectedVersions.Count)}");

        // 设定集 §7 的历史节奏：周均 2 张变更 / 4 份文档，跨上线日至演示日的 29 周。
        Assert.InRange(changes.Count, 40, 80);
        Assert.InRange(documents.Count, 80, 150);

        // 四档状态都必须在场——独立抽签会在几十张的样本上抽空「已取消」，配额分层不会。
        Assert.Equal(4, changes.Select(x => x.State).Distinct().Count());

        // 状态分布落在设定集目标的容差内。
        AssertStateShare(changes, WorldHistoryEngineeringChangeState.Published, WorldHistoryEngineeringSpec.PublishedShare);
        AssertStateShare(changes, WorldHistoryEngineeringChangeState.Scheduled, WorldHistoryEngineeringSpec.ScheduledShare);
        AssertStateShare(changes, WorldHistoryEngineeringChangeState.Draft, WorldHistoryEngineeringSpec.DraftShare);
        AssertStateShare(changes, WorldHistoryEngineeringChangeState.Cancelled, WorldHistoryEngineeringSpec.CancelledShare);

        // 每张变更都挂着受影响版本，且相当一部分落在真实的 rev1 → rev2 演进链上。
        Assert.All(changes, change => Assert.InRange(change.AffectedVersions.Count, 1, 3));
        Assert.True(changes.Count(x => x.AffectedVersions.Any(v => v.SupersededByVersionId is not null)) >= changes.Count / 3);

        // 五类文档全覆盖，SOP 与图纸都在。
        Assert.Equal(
            WorldHistoryEngineeringSpec.DocumentTypes.Order(StringComparer.Ordinal),
            documents.Select(x => x.DocumentType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Contains(documents, x => x.IsSop);
        Assert.Contains(documents, x => x.IsArchived);
    }

    [Fact]
    public void Fact_stream_is_deterministic_and_scale_independent_per_document()
    {
        var first = WorldHistoryEngineeringSpec.BuildChangeFacts(ReferenceDate, 1.0d);
        var second = WorldHistoryEngineeringSpec.BuildChangeFacts(ReferenceDate, 1.0d);
        Assert.Equal(first.Select(Describe), second.Select(Describe));

        // 号段连续、无重复，文件名与 fileId 一一对应。
        Assert.Equal(
            Enumerable.Range(1, first.Count).Select(WorldHistoryEngineeringSpec.ChangeNumber),
            first.Select(x => x.ChangeNumber));

        var documents = WorldHistoryEngineeringSpec.BuildDocumentFacts(ReferenceDate, 1.0d);
        Assert.Equal(
            Enumerable.Range(1, documents.Count).Select(WorldHistoryEngineeringSpec.DocumentNumber),
            documents.Select(x => x.DocumentNumber));
        Assert.Equal(documents.Count, documents.Select(x => x.FileId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(documents, document => Assert.DoesNotContain("-DEMO-", document.DocumentNumber, StringComparison.Ordinal));
        Assert.All(first, change => Assert.DoesNotContain("-SCALE-", change.ChangeNumber, StringComparison.Ordinal));
    }

    /// <summary>
    /// asOfDate 边界：上线日、上线日 +1、年中、演示当天、未来日。
    /// 每个日期都跑两遍 seed，断言两张表的行数、号段、状态分布与受影响版本都稳定。
    /// </summary>
    [Theory]
    [InlineData(2026, 1, 5)]
    [InlineData(2026, 1, 6)]
    [InlineData(2026, 6, 15)]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 12, 31)]
    public async Task Seed_is_idempotent_and_consistent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await new WorldBibleSeedService(db).SeedAsync(OrganizationId, EnvironmentId);

        var seed = new WorldHistorySeedService(db);
        var first = await seed.SeedAsync(OrganizationId, EnvironmentId, asOfDate, 1.0d);
        var second = await seed.SeedAsync(OrganizationId, EnvironmentId, asOfDate, 1.0d);

        var expectedChanges = WorldHistoryEngineeringSpec.BuildChangeFacts(asOfDate, 1.0d);
        var expectedDocuments = WorldHistoryEngineeringSpec.BuildDocumentFacts(asOfDate, 1.0d);
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} changes={expectedChanges.Count} documents={expectedDocuments.Count}");

        // 行数区间：上限不越过周均节奏的两倍；上线当周可能一条都还没发生（0 是合法的历史）。
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        Assert.InRange(expectedChanges.Count, 0, 2 * WorldHistoryEngineeringSpec.WeeklyChangeBase * weeks);
        Assert.InRange(expectedDocuments.Count, 0, 2 * WorldHistoryEngineeringSpec.WeeklyDocumentBase * weeks);
        if (weeks >= 8)
        {
            // 纵深够长时，周均节奏必须真的兑现（否则说明周次切片被 asOfDate 意外裁空）。
            Assert.True(expectedChanges.Count >= weeks - 1);
            Assert.True(expectedDocuments.Count >= 2 * (weeks - 1));
        }

        // 幂等：第二遍一条都不写，库内行数与第一遍一致。
        Assert.Equal(expectedChanges.Count, first.EngineeringChangesWritten);
        Assert.Equal(expectedDocuments.Count, first.EngineeringDocumentsWritten);
        Assert.Equal(0, second.EngineeringChangesWritten);
        Assert.Equal(0, second.EngineeringDocumentsWritten);

        var changes = await db.EngineeringChanges.AsNoTracking().Include(x => x.AffectedVersions).ToArrayAsync();
        var documents = await db.EngineeringDocuments.AsNoTracking().ToArrayAsync();
        Assert.Equal(expectedChanges.Count, changes.Length);
        Assert.Equal(expectedDocuments.Count, documents.Length);
        Assert.Equal(first.AffectedVersionsWritten, changes.Sum(x => x.AffectedVersions.Count));

        // 号段格式。
        Assert.All(changes, change => Assert.Matches(@"^ECO-2026-\d{4}$", change.ChangeNumber));
        Assert.All(documents, document => Assert.Matches(@"^DOC-2026-\d{4}$", document.DocumentNumber));

        // 每张变更至少一条受影响版本，且时间戳已回填到历史窗口内。
        Assert.All(changes, change =>
        {
            Assert.NotEmpty(change.AffectedVersions);
            Assert.InRange(
                DateOnly.FromDateTime(change.CreatedAtUtc.Add(WorldHistoryCalendar.SiteUtcOffset)),
                WorldHistoryCalendar.GoLiveDate,
                asOfDate);
        });
        Assert.All(documents, document => Assert.InRange(
            DateOnly.FromDateTime(document.RegisteredAtUtc.Add(WorldHistoryCalendar.SiteUtcOffset)),
            WorldHistoryCalendar.GoLiveDate,
            asOfDate));

        // 状态分布：校验器已经 fail-closed 过一次，这里再按同一容差断言一遍库内事实。
        AssertStatusShare(changes, EngineeringVersionStatus.Published, WorldHistoryEngineeringSpec.PublishedShare);
        AssertStatusShare(changes, EngineeringVersionStatus.Scheduled, WorldHistoryEngineeringSpec.ScheduledShare);
        AssertStatusShare(changes, EngineeringVersionStatus.Draft, WorldHistoryEngineeringSpec.DraftShare);
        AssertStatusShare(changes, EngineeringVersionStatus.Cancelled, WorldHistoryEngineeringSpec.CancelledShare);

        // 已排期的生效日一定在 asOfDate 之后，否则定时发布任务一启动就把它推成已发布。
        Assert.All(
            changes.Where(x => x.Status == EngineeringVersionStatus.Scheduled),
            change => Assert.True(change.EffectiveDate > asOfDate));

        // L0 已铺开，因此受影响版本的引用完整性校验真的跑过了。
        Assert.True(first.Validation.AffectedVersionReferencesChecked);
        Assert.Equal(changes.Length, first.Validation.EngineeringChangesChecked);
        Assert.Equal(documents.Length, first.Validation.EngineeringDocumentsChecked);
    }

    [Fact]
    public async Task Seeded_history_is_written_in_chinese_and_lands_on_real_world_bible_anchors()
    {
        await using var db = CreateDbContext();
        await new WorldBibleSeedService(db).SeedAsync(OrganizationId, EnvironmentId);
        await new WorldHistorySeedService(db).SeedAsync(OrganizationId, EnvironmentId, ReferenceDate, 1.0d);

        var changes = await db.EngineeringChanges.AsNoTracking().Include(x => x.AffectedVersions).ToArrayAsync();
        Assert.All(changes, change => Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", change.Reason));

        var bomCodes = (await db.EngineeringBoms.AsNoTracking().Select(x => x.BomCode + ":" + x.Revision).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var referenced = changes
            .SelectMany(x => x.AffectedVersions)
            .Where(x => x.VersionKind == WorldHistoryEngineeringSpec.VersionKindEngineeringBom)
            .ToArray();
        Assert.NotEmpty(referenced);
        Assert.All(referenced, version => Assert.Contains(version.VersionId, bomCodes));
        Assert.Contains(referenced, version => version.SupersededByVersionId is not null);

        var documents = await db.EngineeringDocuments.AsNoTracking().ToArrayAsync();
        Assert.All(documents, document => Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", document.FileName));
        Assert.All(documents, document => Assert.StartsWith("file-edoc-", document.FileId, StringComparison.Ordinal));

        var operationCodes = WorldBibleSpec.StandardOperations.Select(x => x.OperationCode).ToHashSet(StringComparer.Ordinal);
        var sopDocuments = documents.Where(x => x.OperationCode is not null).ToArray();
        Assert.NotEmpty(sopDocuments);
        Assert.All(sopDocuments, document =>
        {
            Assert.Contains(document.OperationCode!, operationCodes);
            Assert.NotNull(document.EffectiveDate);
            Assert.Equal(WorldHistoryEngineeringSpec.DocumentTypeSop, document.DocumentType);
        });
    }

    [Fact]
    public async Task Validator_fails_closed_when_the_number_segment_is_squatted()
    {
        await using var db = CreateDbContext();
        await new WorldBibleSeedService(db).SeedAsync(OrganizationId, EnvironmentId);
        await new WorldHistorySeedService(db).SeedAsync(OrganizationId, EnvironmentId, ReferenceDate, 1.0d);

        var squatter = EngineeringChange.Open(OrganizationId, EnvironmentId, "ECO-2026-9999", "号段被占用");
        db.EngineeringChanges.Add(squatter);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync(OrganizationId, EnvironmentId, ReferenceDate, 1.0d));

        Assert.Contains("ECO-2026-9999", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_planned_document_is_missing()
    {
        await using var db = CreateDbContext();
        await new WorldBibleSeedService(db).SeedAsync(OrganizationId, EnvironmentId);
        await new WorldHistorySeedService(db).SeedAsync(OrganizationId, EnvironmentId, ReferenceDate, 1.0d);

        var victim = await db.EngineeringDocuments.OrderBy(x => x.DocumentNumber).FirstAsync();
        db.EngineeringDocuments.Remove(victim);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync(OrganizationId, EnvironmentId, ReferenceDate, 1.0d));

        Assert.Contains("工程文档缺失", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>把一条变更事实压成可比对的字符串（record 里含集合，默认相等性不是结构相等）。</summary>
    private static string Describe(WorldHistoryEngineeringChangeFact fact)
    {
        var versions = string.Join(";", fact.AffectedVersions.Select(x => $"{x.VersionKind}>{x.VersionId}>{x.SupersededByVersionId}"));
        var effectiveDate = fact.EffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
        var openedAt = fact.OpenedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        var decidedAt = fact.DecidedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        return string.Join(
            '|',
            fact.ChangeNumber,
            fact.ReasonCategory,
            fact.Reason,
            fact.ApprovalReferenceId ?? "-",
            fact.State.ToString(),
            effectiveDate,
            openedAt,
            decidedAt,
            versions);
    }

    private static void AssertStateShare(
        IReadOnlyList<WorldHistoryEngineeringChangeFact> changes,
        WorldHistoryEngineeringChangeState state,
        double expectedShare) =>
        Assert.True(
            WorldHistoryConsistencyValidator.WithinTolerance(changes.Count(x => x.State == state), expectedShare, changes.Count),
            FormattableString.Invariant($"{state} 占比偏离目标 {expectedShare:P0}。"));

    private static void AssertStatusShare(
        IReadOnlyList<EngineeringChange> changes,
        EngineeringVersionStatus status,
        double expectedShare) =>
        Assert.True(
            WorldHistoryConsistencyValidator.WithinTolerance(changes.Count(x => x.Status == status), expectedShare, changes.Count),
            FormattableString.Invariant($"{status} 占比偏离目标 {expectedShare:P0}。"));

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"product-engineering-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistorySeedTestMediator());
    }

    private sealed class WorldHistorySeedTestMediator : IMediator
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
