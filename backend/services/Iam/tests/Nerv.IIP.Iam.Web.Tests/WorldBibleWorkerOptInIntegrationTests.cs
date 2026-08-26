using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.DataScopes;
using Nerv.IIP.Iam.Web.Application.Seed;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// 以同一组 IAM seed 服务复现 NERV-1360 的最小开通链：先登记工人，再只开通
/// 既有 PDA 演示账号，确认 WMS 工人能登录且未扩展其它人员授权。
/// </summary>
public sealed class WorldBibleWorkerOptInIntegrationTests
{
    [Fact]
    public async Task Worker_opt_in_makes_emp049_login_ready_without_granting_other_workers_memberships()
    {
        const string demoWorkerPassword = "worker-password-for-test";
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"world-bible-worker-opt-in-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var passwordService = new IamPasswordService();

        await new WorldBibleWorkerSeedService(services, passwordService).SeedAsync();
        await new WorldBiblePdaDemoAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = demoWorkerPassword,
            }),
            passwordService).SeedAsync();

        dbContext.ChangeTracker.Clear();
        var emp049 = await dbContext.Users.SingleAsync(x => x.Id.Id == "user-emp-049");
        Assert.True(passwordService.Verify(emp049, demoWorkerPassword));
        Assert.False(emp049.PasswordChangeRequired);

        var emp049Membership = await dbContext.Memberships
            .Include(x => x.Roles)
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id == new MembershipId("user-emp-049:org-001:env-dev"));
        Assert.Equal(
            WorldBiblePdaDemoAccountSeedService.WarehouseRoleId,
            Assert.Single(emp049Membership.Roles).RoleId.Id);
        var emp049Scope = Assert.Single(emp049Membership.DataScopes);
        Assert.Equal(DataScopeBinding.Self, emp049Scope.ScopeType);
        Assert.Equal("user-emp-049", emp049Scope.ScopeCode);

        var warehouseRole = await dbContext.Roles
            .Include(x => x.Permissions)
            .SingleAsync(x => x.Id.Id == WorldBiblePdaDemoAccountSeedService.WarehouseRoleId);
        Assert.Contains(
            warehouseRole.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.read");
        Assert.DoesNotContain(
            warehouseRole.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.manage");

        Assert.Empty(await dbContext.Memberships
            .Where(x => x.UserId.Id == "user-emp-001")
            .ToArrayAsync());
        var unselectedWorker = await dbContext.Users.SingleAsync(x => x.Id.Id == "user-emp-001");
        Assert.True(unselectedWorker.PasswordChangeRequired);
        Assert.False(passwordService.Verify(unselectedWorker, demoWorkerPassword));
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
