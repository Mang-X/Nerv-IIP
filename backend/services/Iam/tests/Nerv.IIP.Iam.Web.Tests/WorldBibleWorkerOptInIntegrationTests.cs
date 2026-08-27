using Microsoft.AspNetCore.Http;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
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

    [Fact]
    public async Task Worker_membership_authorizes_emp049_sku_read_without_manage_with_managed_scopes()
    {
        const string demoWorkerPassword = "worker-password-for-authorization-test";
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        await dbContext.Database.EnsureCreatedAsync();
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
        var tokenService = new IamTokenService(
            new ConfigurationBuilder().Build(),
            new TestWebHostEnvironment());
        var authorization = new PostgreSqlIamAuthService(
            new UserRepository(dbContext),
            new UserSessionRepository(dbContext),
            new MembershipRepository(dbContext),
            new ConnectorHostCredentialRepository(dbContext),
            new ExternalClientRepository(dbContext),
            passwordService,
            tokenService,
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var login = await authorization.LoginAsync(
            "emp049",
            demoWorkerPassword,
            clientInfo: null,
            ipAddress: null,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var principal = await authorization.GetCurrentPrincipalAsync(
            CreateHttpContext(login.AccessToken),
            CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal("user-emp-049", principal!.UserId);
        Assert.Equal("org-001", principal.OrganizationId);
        Assert.Equal("env-dev", principal.EnvironmentId);
        Assert.Equal(
            [WorldBiblePdaDemoAccountSeedService.WarehouseRoleId],
            principal.RoleIds);
        Assert.Contains("business.masterdata.products.read", principal.PermissionCodes);
        Assert.DoesNotContain("business.masterdata.products.manage", principal.PermissionCodes);

        var skuRead = await authorization.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.masterdata.products.read",
            resourceType: "master-data-sku",
            resourceId: null,
            CancellationToken.None);

        Assert.True(skuRead.Allowed);
        Assert.NotNull(skuRead.DataScope);
        Assert.False(skuRead.DataScope!.DenyAll);
        Assert.Equal(["SITE-001"], skuRead.DataScope.SiteCodes);
        Assert.Equal(["user-emp-049"], skuRead.DataScope.SelfIds);
        Assert.Empty(skuRead.DataScope.WorkshopCodes);
        Assert.Empty(skuRead.DataScope.ProductionLineCodes);
        Assert.Empty(skuRead.DataScope.TeamCodes!);
        Assert.Empty(skuRead.DataScope.WorkCenterCodes!);
        Assert.Empty(skuRead.DataScope.OrganizationIds!);
        Assert.NotNull(skuRead.ScopeGrants);
        Assert.Collection(
            skuRead.ScopeGrants,
            membershipGrant =>
            {
                Assert.Equal("membership", membershipGrant.SourceKind);
                Assert.Equal("user-emp-049:org-001:env-dev", membershipGrant.SourceId);
                Assert.Equal(DataScopeBinding.Self, membershipGrant.ScopeKind);
                Assert.Equal("user-emp-049", membershipGrant.ScopeId);
                Assert.Equal(
                    ["business.masterdata.products.read"],
                    membershipGrant.ApplicablePermissionCodes);
                Assert.False(membershipGrant.OrganizationWide);
            },
            roleGrant =>
            {
                Assert.Equal("role", roleGrant.SourceKind);
                Assert.Equal(
                    WorldBiblePdaDemoAccountSeedService.WarehouseRoleId,
                    roleGrant.SourceId);
                Assert.Equal(DataScopeBinding.Site, roleGrant.ScopeKind);
                Assert.Equal("SITE-001", roleGrant.ScopeId);
                Assert.Equal(
                    ["business.masterdata.products.read"],
                    roleGrant.ApplicablePermissionCodes);
                Assert.False(roleGrant.OrganizationWide);
            });

        var skuManage = await authorization.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.masterdata.products.manage",
            resourceType: "master-data-sku",
            resourceId: null,
            CancellationToken.None);

        Assert.False(skuManage.Allowed);
        Assert.Null(skuManage.DataScope);
        Assert.Null(skuManage.ScopeGrants);
    }

    private static HttpContext CreateHttpContext(string accessToken)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {accessToken}";
        return httpContext;
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
