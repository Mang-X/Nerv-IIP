using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 为开发环境的 PDA 演示补齐最小 WMS 现场资格边界。
///
/// 该种子只写一个收货作业池、emp049 的有效成员资格和一张保持 Open 的演示入库单，
/// 不生成世界历史的任务、库存移动、WCS 或退货事实，也不修改 IAM 权限。
/// </summary>
public sealed class WmsWorkPoolMembershipSeedService(
    ApplicationDbContext dbContext,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private const string PoolCode = WorldHistoryWarehouseOpsSpec.ReceivingPoolCode;
    private const string PoolDisplayName = "收货与上架";
    private const string SiteCode = WorldHistorySpec.SiteCode;
    private const string PrincipalId = WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId;
    private const string InboundOrderNo = "IB-WMS-SEED-001";
    private const string SeedSourceDocumentType = "wms-walkthrough-seed";
    private const string SeedSourceDocumentId = "WMS-WALKTHROUGH-SEED-001";
    private const string SkuCode = "RM-TUB-01";
    private const string UomCode = "kg";
    private const string StagingLocationCode = "loc-raw-01";
    private const string LotNo = "LOT-WMS-SEED-001";

    private static readonly DateTime EffectiveFromUtc = WorldHistoryCalendar.GoLiveDate.ToDateTime(
        TimeOnly.MinValue,
        DateTimeKind.Utc);

    public async Task<WmsWorkPoolMembershipSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var pool = await dbContext.WarehouseWorkPools
            .SingleOrDefaultAsync(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.PoolCode == PoolCode,
                cancellationToken);

        var poolsWritten = 0;
        if (pool is null)
        {
            pool = WarehouseWorkPool.Create(
                organizationId,
                environmentId,
                PoolCode,
                PoolDisplayName,
                SiteCode);
            dbContext.WarehouseWorkPools.Add(pool);
            poolsWritten++;
        }
        else if (!pool.Active || !string.Equals(pool.SiteCode, SiteCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"WMS demo work pool '{PoolCode}' must be active at site '{SiteCode}'.");
        }

        var existingMemberships = await dbContext.WarehouseWorkPoolMemberships
            .AsNoTracking()
            .Where(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.PoolCode == PoolCode
                && candidate.PrincipalId == PrincipalId)
            .ToArrayAsync(cancellationToken);

        var membershipsWritten = 0;
        if (existingMemberships.Length == 0)
        {
            dbContext.WarehouseWorkPoolMemberships.Add(WarehouseWorkPoolMembership.Create(
                organizationId,
                environmentId,
                PoolCode,
                PrincipalId,
                EffectiveFromUtc));
            membershipsWritten++;
        }
        else if (!existingMemberships.Any(membership => membership.IsEffectiveAt(clock.GetUtcNow().UtcDateTime)))
        {
            throw new InvalidOperationException(
                $"WMS demo work-pool membership '{PoolCode}/{PrincipalId}' is not currently effective.");
        }

        var existingInboundOrder = await dbContext.InboundOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.InboundOrderNo == InboundOrderNo,
                cancellationToken);

        var inboundOrdersWritten = 0;
        if (existingInboundOrder is null)
        {
            var inboundOrder = InboundOrder.Create(
                organizationId,
                environmentId,
                InboundOrderNo,
                SeedSourceDocumentType,
                SeedSourceDocumentId,
                SiteCode,
                [
                    new InboundOrderLineDraft(
                        WorldHistoryWmsSpec.LineNo,
                        SkuCode,
                        UomCode,
                        ReceivedQuantity: 1m,
                        StagingLocationCode,
                        LotNo,
                        SerialNo: null,
                        WorldHistoryWmsSpec.Unrestricted,
                        WorldHistoryWmsSpec.OwnerType,
                        OwnerId: null),
                ],
                assignedOperatorUserId: PrincipalId,
                assignedPoolCode: PoolCode);
            dbContext.InboundOrders.Add(inboundOrder);
            inboundOrdersWritten++;
        }

        if (poolsWritten + membershipsWritten + inboundOrdersWritten > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return new WmsWorkPoolMembershipSeedReport(
            poolsWritten,
            membershipsWritten,
            inboundOrdersWritten);
    }
}

public sealed record WmsWorkPoolMembershipSeedReport(
    int WorkPoolsWritten,
    int WorkPoolMembershipsWritten,
    int InboundOrdersWritten);
