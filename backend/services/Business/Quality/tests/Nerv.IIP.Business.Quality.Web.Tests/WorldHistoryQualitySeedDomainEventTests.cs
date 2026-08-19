using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCorePal.Extensions.Domain;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// L1 背景历史（质量域侧）**不得驱动下游**的门禁。
///
/// <para>本块写的 <c>InspectionPlan</c> / <c>InspectionRecord</c> / <c>NonconformanceReport</c>
/// 都会 <c>AddDomainEvent</c>，且多数有跨服务转换器：
/// <c>InspectionPassed/Rejected/ConditionalReleased</c> → <c>InspectionResultIntegrationEvent</c>（MES 放行）、
/// <c>NonconformanceReportOpened/DispositionDecided/Closed</c>，以及
/// <c>NonconformanceReportInventoryDispositionRequested</c> → Inventory 的
/// <c>InventoryMovementRequestedIntegrationEvent</c>（会真扣库存）。
/// 全量规模约 7000 条检验任务，一旦外发就是启动瞬间的 CAP 风暴。</para>
///
/// <para>「本仓栈里裸 <c>SaveChangesAsync</c> 到底派不派发」的实测在 MES 侧
/// <c>WorldHistorySeedDomainEventTests</c>（含阳性对照）。这里不重复那条实测，
/// 只锁住种子自身的不变量：写盘那一刻实体上不许还挂着领域事件。</para>
/// </summary>
public sealed class WorldHistoryQualitySeedDomainEventTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>与 <c>WorldHistoryQualitySeedServiceTests</c> 同一规模：足够跑出 NCR 全链。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public async Task Seed_clears_domain_events_before_writing_history_facts()
    {
        var interceptor = new PendingDomainEventInterceptor();
        var mediator = new RecordingMediator();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-world-history-events-{Guid.CreateVersion7():N}")
            .AddInterceptors(interceptor)
            .Options;
        await using var dbContext = new ApplicationDbContext(options, mediator);

        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        output.WriteLine($"写盘时挂在实体上的领域事件（{interceptor.PendingOnSaving.Count}）：{Describe(interceptor.PendingOnSaving)}");
        output.WriteLine($"实际派发出去的通知（{mediator.Published.Count}）：{Describe(mediator.Published)}");
        output.WriteLine($"本次实测覆盖的写盘次数：{interceptor.SaveCount}");

        // 探针有效性：种子确实写过盘，否则「零事件」是空跑出来的假绿。
        Assert.True(interceptor.SaveCount > 0, "种子一次盘都没写，这条用例证明不了任何事。");
        Assert.True(
            interceptor.PendingOnSaving.Count == 0,
            $"种子写盘时仍有 {interceptor.PendingOnSaving.Count} 条领域事件挂在实体上：" +
            $"{Describe(interceptor.PendingOnSaving)}——历史事实会驱动下游（MES 放行 / 库存被真扣 / CAP 风暴）。" +
            "落盘前必须 ClearDomainEvents。");
        Assert.Empty(mediator.Published);
    }

    private static string Describe(IReadOnlyCollection<string> names) =>
        names.Count == 0
            ? "（空）"
            : string.Join("; ", names.GroupBy(x => x, StringComparer.Ordinal)
                .Select(g => $"{g.Key}×{g.Count()}")
                .Order(StringComparer.Ordinal));

    /// <summary>在 <c>SavingChanges</c> 时把变更跟踪器里所有实体上挂着的领域事件抄下来。</summary>
    private sealed class PendingDomainEventInterceptor : SaveChangesInterceptor
    {
        public List<string> PendingOnSaving { get; } = [];

        public int SaveCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (eventData.Context is { } context)
            {
                foreach (var entry in context.ChangeTracker.Entries<Entity>())
                {
                    PendingOnSaving.AddRange(entry.Entity.GetDomainEvents().Select(x => x.GetType().Name));
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class RecordingMediator : IMediator
    {
        public List<string> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification.GetType().Name);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification!.GetType().Name);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
