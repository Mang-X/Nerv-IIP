using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;

namespace Nerv.IIP.Business.Wms.Web.Tests;

internal static class TrustedWmsCompletionTestCommands
{
    private const string ActorPrincipalId = "completion-test-operator";

    public static CompleteInboundOrderCommand TrustedFor(
        this CompleteInboundOrderCommand command,
        ApplicationDbContext dbContext,
        InboundOrder inbound)
    {
        TrustFixture(dbContext, inbound);
        return command with
        {
            OrganizationId = command.OrganizationId ?? inbound.OrganizationId,
            EnvironmentId = command.EnvironmentId ?? inbound.EnvironmentId,
            ActorPrincipalId = ActorPrincipalId,
            AuthorizedSiteCodes = [inbound.SiteCode],
            ScopeKind = "self",
            ScopeId = ActorPrincipalId,
            ExpectedVersion = command.ExpectedVersion > 0
                ? command.ExpectedVersion
                : inbound.Version,
        };
    }

    public static CompleteOutboundOrderCommand TrustedFor(
        this CompleteOutboundOrderCommand command,
        ApplicationDbContext dbContext,
        OutboundOrder outbound)
    {
        TrustFixture(dbContext, outbound);
        return command with
        {
            OrganizationId = command.OrganizationId ?? outbound.OrganizationId,
            EnvironmentId = command.EnvironmentId ?? outbound.EnvironmentId,
            ActorPrincipalId = ActorPrincipalId,
            AuthorizedSiteCodes = [outbound.SiteCode],
            ScopeKind = "self",
            ScopeId = ActorPrincipalId,
            ExpectedVersion = command.ExpectedVersion > 0
                ? command.ExpectedVersion
                : outbound.Version,
        };
    }

    public static CompleteCountExecutionCommand TrustedFor(
        this CompleteCountExecutionCommand command,
        ApplicationDbContext dbContext,
        CountExecution count)
    {
        TrustFixture(dbContext, count);
        return command with
        {
            OrganizationId = command.OrganizationId ?? count.OrganizationId,
            EnvironmentId = command.EnvironmentId ?? count.EnvironmentId,
            ActorPrincipalId = ActorPrincipalId,
            AuthorizedSiteCodes = [count.SiteCode],
            ScopeKind = "self",
            ScopeId = ActorPrincipalId,
            ExpectedVersion = command.ExpectedVersion > 0
                ? command.ExpectedVersion
                : count.Version,
        };
    }

    public static void TrustFixture(
        ApplicationDbContext dbContext,
        InboundOrder inbound) =>
        AssignAndSeed(
            dbContext,
            inbound,
            inbound.OrganizationId,
            inbound.EnvironmentId,
            inbound.SiteCode);

    public static void TrustFixture(
        ApplicationDbContext dbContext,
        OutboundOrder outbound) =>
        AssignAndSeed(
            dbContext,
            outbound,
            outbound.OrganizationId,
            outbound.EnvironmentId,
            outbound.SiteCode);

    public static void TrustFixture(
        ApplicationDbContext dbContext,
        CountExecution count) =>
        AssignAndSeed(
            dbContext,
            count,
            count.OrganizationId,
            count.EnvironmentId,
            count.SiteCode);

    private static void AssignAndSeed(
        ApplicationDbContext dbContext,
        object resource,
        string organizationId,
        string environmentId,
        string siteCode)
    {
        var poolCode = $"POOL-COMPLETE-{siteCode}";
        Set(resource, "AssignedPoolCode", poolCode);
        Set(resource, "AssignedOperatorUserId", ActorPrincipalId);
        var poolExists = dbContext.WarehouseWorkPools.Local.Any(pool =>
                pool.OrganizationId == organizationId
                && pool.EnvironmentId == environmentId
                && pool.PoolCode == poolCode)
            || dbContext.WarehouseWorkPools.AsNoTracking().Any(pool =>
                pool.OrganizationId == organizationId
                && pool.EnvironmentId == environmentId
                && pool.PoolCode == poolCode);
        if (!poolExists)
        {
            dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
                organizationId,
                environmentId,
                poolCode,
                "完工测试作业池",
                siteCode));
        }

        var membershipExists = dbContext.WarehouseWorkPoolMemberships.Local.Any(
                membership => membership.OrganizationId == organizationId
                    && membership.EnvironmentId == environmentId
                    && membership.PoolCode == poolCode
                    && membership.PrincipalId == ActorPrincipalId)
            || dbContext.WarehouseWorkPoolMemberships.AsNoTracking().Any(
                membership => membership.OrganizationId == organizationId
                    && membership.EnvironmentId == environmentId
                    && membership.PoolCode == poolCode
                    && membership.PrincipalId == ActorPrincipalId);
        if (!membershipExists)
        {
            dbContext.WarehouseWorkPoolMemberships.Add(
                WarehouseWorkPoolMembership.Create(
                    organizationId,
                    environmentId,
                    poolCode,
                    ActorPrincipalId,
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(1)));
        }

        dbContext.SaveChanges();
    }

    private static void Set(object target, string propertyName, string value) =>
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);
}
