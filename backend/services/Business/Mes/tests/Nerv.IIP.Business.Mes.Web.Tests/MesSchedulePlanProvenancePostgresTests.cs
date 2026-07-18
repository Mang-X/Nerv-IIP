using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesSchedulePlanProvenancePostgresTests
{
    [MesRealPostgresFact]
    public async Task Schedule_provenance_and_DateTimeOffset_survive_release_and_revoke_on_postgres()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).Options;
        var scheduledAt = DateTimeOffset.Parse("2026-07-18T02:30:00+00:00");

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            await db.Database.MigrateAsync();
            db.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 1, scheduledAt.AddHours(4), "PCS"));
            var task = OperationTask.Queue("org-001", "env-dev", "WO-001", "OP-10", 10, "WC-OLD", [], scheduledAt, TimeSpan.FromHours(1));
            task.ApplyScheduleAssignment("WC-1", "DEV-1", scheduledAt, scheduledAt.AddHours(1), scheduledAt, schedulePlanId: "plan-1", scheduleReleaseRevision: 1);
            db.OperationTasks.Add(task);
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            var task = await db.OperationTasks.SingleAsync();
            Assert.Equal("plan-1", task.SchedulePlanId);
            Assert.Equal(1, task.ScheduleReleaseRevision);
            Assert.Equal(scheduledAt, task.ScheduledAtUtc);
            task.RevokeScheduleAssignment("plan-1", 1, "explicit");
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            var task = await db.OperationTasks.SingleAsync();
            Assert.Null(task.SchedulePlanId);
            Assert.Null(task.ScheduleReleaseRevision);
            Assert.Null(task.ScheduledAtUtc);
            Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
        }
    }

    private sealed class TemporaryDatabase(string adminConnectionString, string databaseName, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_mes_schedule_{Guid.CreateVersion7():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryDatabase(
                adminConnectionString,
                databaseName,
                new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class NoopMediator : IMediator
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
