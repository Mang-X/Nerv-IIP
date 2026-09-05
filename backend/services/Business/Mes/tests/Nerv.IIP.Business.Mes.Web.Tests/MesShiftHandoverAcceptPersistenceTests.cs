using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #2803：接班命令的定位谓词必须在真实关系 provider 上可翻译。
/// 原实现把 <c>x.Id.Id.ToString() == request.HandoverId</c> 写进谓词，整条查询翻译不了，
/// 于是任何一次 accept 都 500——与传入的是 HandoverNo 还是 Guid 无关。
/// 既有的接班用例跑在 InMemory 上（一律客户端求值）所以照绿，因此这里改用 SQLite 实跑；
/// 已实测：把谓词改回原样，本文件三个用例全部转红并报 could not be translated。
/// </summary>
public sealed class MesShiftHandoverAcceptPersistenceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-08-31T08:00:00Z");
    private static readonly DateTimeOffset AcceptedAtUtc = DateTimeOffset.Parse("2026-08-31T16:00:00Z");

    [Fact]
    public async Task Accept_by_handover_no_translates_and_marks_accepted_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedOpenHandover(dbContext, "SH-2803-01");
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var response = await new AcceptShiftHandoverCommandHandler(dbContext).Handle(
            new AcceptShiftHandoverCommand(Org, Env, "SH-2803-01", AcceptedAtUtc, "user-in", "接班人"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Equal("Accepted", response.Status);
        Assert.Equal("SH-2803-01", response.ReferenceId);
        var persisted = await dbContext.ShiftHandovers.SingleAsync();
        Assert.Equal(ShiftHandover.AcceptedStatus, persisted.HandoverStatus);
        Assert.Equal(AcceptedAtUtc, persisted.AcceptedAtUtc);
        Assert.Equal("user-in", persisted.IncomingUserId);
    }

    [Fact]
    public async Task Accept_by_guid_id_translates_and_marks_accepted_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var handover = SeedOpenHandover(dbContext, "SH-2803-02");
        await dbContext.SaveChangesAsync();
        var guidId = handover.Id.Id.ToString();
        dbContext.ChangeTracker.Clear();

        await new AcceptShiftHandoverCommandHandler(dbContext).Handle(
            new AcceptShiftHandoverCommand(Org, Env, guidId, AcceptedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.ShiftHandovers.SingleAsync();
        Assert.Equal(ShiftHandover.AcceptedStatus, persisted.HandoverStatus);
        Assert.Equal(AcceptedAtUtc, persisted.AcceptedAtUtc);
    }

    /// <summary>未命中一律走业务异常出口：Guid.TryParse 成功但库里没有的输入也不能落成 500。</summary>
    [Fact]
    public async Task Accept_unknown_handover_throws_known_exception()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var handler = new AcceptShiftHandoverCommandHandler(dbContext);

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() =>
            handler.Handle(
                new AcceptShiftHandoverCommand(Org, Env, "SH-MISSING", AcceptedAtUtc),
                CancellationToken.None));
        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() =>
            handler.Handle(
                new AcceptShiftHandoverCommand(Org, Env, "0199a3f0-2803-7000-8000-000000002803", AcceptedAtUtc),
                CancellationToken.None));
    }

    private static ShiftHandover SeedOpenHandover(ApplicationDbContext dbContext, string handoverNo)
    {
        var handover = ShiftHandover.Create(
            Org, Env, handoverNo, "SHIFT-A", "TEAM-01", 0, CreatedAtUtc, "甲班", "user-out", "交班人");
        dbContext.ShiftHandovers.Add(handover);
        return handover;
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
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>()
            .Options;
        return new ApplicationDbContext(options, NoopMediator.Instance);
    }

    // SQLite provider 翻译不了 DateTimeOffset 的比较（仓库已知坑：EF 测试 provider 翻译差异），
    // 与 MesDowntimeRecoveryPersistenceTests 同款：测试专用 ModelCustomizer 把 DateTimeOffset 列统一转成 long。
    private sealed class SqliteDateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        private static readonly DateTimeOffsetToBinaryConverter Converter = new();

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(Converter);
                }
            }
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public static NoopMediator Instance { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot create streams.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot create streams.");
    }
}
