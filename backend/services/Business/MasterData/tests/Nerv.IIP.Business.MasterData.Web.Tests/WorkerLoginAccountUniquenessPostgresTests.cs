using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Testing.PostgreSql;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 员工 ↔ 登录账号一对一（#3924）的 PostgreSQL 证明：绕开命令预检直接落两条同账号员工，
/// 真实 23505 + 约束名 <c>IX_workers_organization_id_environment_id_user_id</c> 必须转成中文业务错误。
/// SQLite 一侧见 <see cref="WorkerLoginAccountUniquenessTests"/>。
/// </summary>
public sealed class WorkerLoginAccountUniquenessPostgresTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private const string ConflictMessage = "所选登录账号已关联其他员工，一个账号只能关联一名员工。";

    [PostgresFact]
    public async Task Second_worker_with_same_login_account_is_rejected_as_known_exception_on_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_masterdata_worker_account");
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.Workers.Add(NewWorker("EMP-A", "user-linked"));
            await setup.SaveChangesAsync();
        }

        await using var loser = CreateContext(database.ConnectionString);
        loser.Workers.Add(NewWorker("EMP-B", "user-linked"));

        var error = await Assert.ThrowsAsync<KnownException>(() => loser.SaveChangesAsync());
        Assert.Equal(ConflictMessage, error.Message);
    }

    private static Worker NewWorker(string code, string userId) =>
        Worker.Create(OrganizationId, EnvironmentId, code, $"员工{code}", userId, null, null, Worker.StatusActive, null);

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
