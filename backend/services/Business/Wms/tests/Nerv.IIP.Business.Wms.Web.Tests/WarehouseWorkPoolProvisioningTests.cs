using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 现场作业池写面：让「派工夹具」不再只能由 LeaderDemo 世界观种子提供（#1910 / NERV-1125）。
/// 判据不是「有没有写进表」，而是**同一套公开写面造出来的池 + 成员能否让派工授权通过**。
/// </summary>
public sealed class WarehouseWorkPoolProvisioningTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Public_write_face_provisions_a_pool_and_member_that_authorizes_assignment()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = new StaticTimeProvider(Now);

        var pool = await new ProvisionWarehouseWorkPoolCommandHandler(dbContext).Handle(
            ProvisionCommand(),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var member = await new AddWarehouseWorkPoolMemberCommandHandler(dbContext, timeProvider)
            .Handle(MemberCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(pool.Created);
        Assert.True(pool.Active);
        Assert.Equal("POOL-WMS-SHIPPING", pool.PoolCode);
        Assert.Equal("SITE-001", pool.SiteCode);
        Assert.True(member.Created);
        Assert.Equal(Now, member.EffectiveFromUtc);
        Assert.Null(member.EffectiveToUtc);

        // 真正的验收面：派工授权此刻应当成立，且被指派人就是刚加进去的那名操作员。
        var authorization = await new WarehouseWorkScopeAuthorizer(dbContext, timeProvider)
            .AuthorizeAssignmentAsync(
                new WarehouseAssignmentAuthorizationRequest(
                    "org-001",
                    "env-dev",
                    "user-emp-049",
                    ["SITE-001"],
                    "SITE-001",
                    "POOL-WMS-SHIPPING",
                    "user-emp-049"),
                CancellationToken.None);

        Assert.Equal("POOL-WMS-SHIPPING", authorization.PoolCode);
        Assert.Equal("user-emp-049", authorization.OperatorPrincipalId);
    }

    [Fact]
    public async Task Repeated_provisioning_is_idempotent_and_never_duplicates_rows()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = new StaticTimeProvider(Now);
        var poolHandler = new ProvisionWarehouseWorkPoolCommandHandler(dbContext);
        var memberHandler = new AddWarehouseWorkPoolMemberCommandHandler(dbContext, timeProvider);

        await poolHandler.Handle(ProvisionCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await memberHandler.Handle(MemberCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var poolReplay = await poolHandler.Handle(ProvisionCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var memberReplay = await memberHandler.Handle(MemberCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.False(poolReplay.Created);
        Assert.False(memberReplay.Created);
        Assert.Equal(Now, memberReplay.EffectiveFromUtc);
        Assert.Single(dbContext.WarehouseWorkPools);
        Assert.Single(dbContext.WarehouseWorkPoolMemberships);
    }

    [Fact]
    public async Task Provisioning_fails_closed_outside_the_exact_site_grant()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new ProvisionWarehouseWorkPoolCommandHandler(dbContext);

        var missingGrant = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                ProvisionCommand() with { AuthorizedSiteCodes = [] },
                CancellationToken.None));
        var crossSite = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                ProvisionCommand() with { AuthorizedSiteCodes = ["SITE-002"] },
                CancellationToken.None));

        Assert.Equal("missing-exact-site-grant", missingGrant.Reason);
        Assert.Equal("site-outside-exact-grant", crossSite.Reason);
        Assert.Empty(dbContext.WarehouseWorkPools);
    }

    [Fact]
    public async Task Reusing_a_pool_code_on_another_site_is_rejected_instead_of_silently_moved()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new ProvisionWarehouseWorkPoolCommandHandler(dbContext);
        await handler.Handle(ProvisionCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var failure = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                ProvisionCommand() with
                {
                    SiteCode = "SITE-002",
                    AuthorizedSiteCodes = ["SITE-001", "SITE-002"],
                },
                CancellationToken.None));

        Assert.Equal("work-pool-site-mismatch", failure.Reason);
        Assert.Equal("SITE-001", dbContext.WarehouseWorkPools.Single().SiteCode);
    }

    [Fact]
    public async Task Membership_requires_an_existing_pool_and_a_forward_window()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new AddWarehouseWorkPoolMemberCommandHandler(
            dbContext,
            new StaticTimeProvider(Now));

        var unknownPool = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(MemberCommand(), CancellationToken.None));

        await new ProvisionWarehouseWorkPoolCommandHandler(dbContext)
            .Handle(ProvisionCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var backwardWindow = await Assert.ThrowsAsync<WmsUnprocessableException>(() =>
            handler.Handle(
                MemberCommand() with
                {
                    EffectiveFromUtc = Now,
                    EffectiveToUtc = Now.AddHours(-1),
                },
                CancellationToken.None));
        var crossSiteMember = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                MemberCommand() with { AuthorizedSiteCodes = ["SITE-002"] },
                CancellationToken.None));

        Assert.Equal("inactive-or-unknown-work-pool", unknownPool.Reason);
        Assert.Equal("membership-window-not-forward", backwardWindow.ReasonCode);
        Assert.Equal("site-outside-exact-grant", crossSiteMember.Reason);
        Assert.Empty(dbContext.WarehouseWorkPoolMemberships);
    }

    private static ProvisionWarehouseWorkPoolCommand ProvisionCommand() =>
        new(
            "org-001",
            "env-dev",
            "user-emp-048",
            ["SITE-001"],
            "POOL-WMS-SHIPPING",
            "拣货与发运",
            "SITE-001");

    private static AddWarehouseWorkPoolMemberCommand MemberCommand() =>
        new(
            "org-001",
            "env-dev",
            "user-emp-048",
            ["SITE-001"],
            "POOL-WMS-SHIPPING",
            "user-emp-049",
            EffectiveFromUtc: null,
            EffectiveToUtc: null);

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
