using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamRoleResolutionTests
{
    [Fact]
    public async Task Role_lookup_returns_only_requested_non_deleted_roles()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
            new NoopMediator());
        await db.Database.EnsureCreatedAsync();
        db.Roles.AddRange(
            new Role(new RoleId("role-requested"), "请求角色", ["business.mes.work-orders.read"]),
            new Role(new RoleId("role-other"), "无关角色", ["business.mes.work-orders.read"]),
            new Role(new RoleId("role-deleted"), "已删除角色", ["business.mes.work-orders.read"]));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE roles SET Deleted = 1 WHERE Id = 'role-deleted'");
        db.ChangeTracker.Clear();

        var roles = await new RoleRepository(db).ListByIdsAsync(
            [new RoleId("role-requested"), new RoleId("role-deleted")],
            CancellationToken.None);

        var role = Assert.Single(roles);
        Assert.Equal("role-requested", role.Id.Id);
        Assert.Equal("请求角色", role.RoleName);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
