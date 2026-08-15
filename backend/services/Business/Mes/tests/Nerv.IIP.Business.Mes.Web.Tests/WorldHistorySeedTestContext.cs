using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// L1 背景历史引擎测试的公共夹具：一次性的 InMemory <see cref="ApplicationDbContext"/>，
/// 以及「先把工单链铺好」这一步——追溯断点、规则排程等后置块都只挂在真实落库的工单上。
/// </summary>
internal static class WorldHistorySeedTestContext
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    public static async Task SeedWorkOrderChainAsync(ApplicationDbContext dbContext, DateOnly asOfDate, double scale) =>
        await new WorldHistorySeedService(dbContext, new StubProductionVersionResolver())
            .SeedAsync("org-001", "env-dev", asOfDate, scale);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

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

    private sealed class StubProductionVersionResolver : IWorldHistoryProductionVersionResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<string> skuCodes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                skuCodes.ToDictionary(sku => sku, sku => $"PV-{sku}", StringComparer.Ordinal));
    }
}
