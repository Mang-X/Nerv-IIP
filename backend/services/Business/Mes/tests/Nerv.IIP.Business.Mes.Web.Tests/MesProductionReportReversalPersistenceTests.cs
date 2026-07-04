using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesProductionReportReversalPersistenceTests
{
    [Fact]
    public async Task Duplicate_reversal_unique_conflict_returns_known_exception()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-REV-SQL", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-REV-SQL",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-10",
            [],
            now,
            TimeSpan.FromMinutes(30),
            now,
            null));
        var original = ProductionReport.Record(
            "org-001",
            "env-dev",
            "RPT-ORIGINAL",
            "WO-REV-SQL",
            "OP-10",
            1m,
            0m,
            false,
            now.AddMinutes(10));
        dbContext.ProductionReports.Add(original);
        dbContext.ProductionReports.Add(ProductionReport.Reverse(original, "RPT-REV-1", now.AddMinutes(20), "first correction"));
        dbContext.ProductionReports.Add(ProductionReport.Reverse(original, "RPT-REV-2", now.AddMinutes(21), "concurrent correction"));

        var exception = await Assert.ThrowsAsync<KnownException>(() => dbContext.SaveChangesAsync());

        Assert.Contains("已冲销", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
