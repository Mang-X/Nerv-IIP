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

    public async Task<WmsWorkPoolMembershipSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var pool = await dbContext.WarehouseWorkPools
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.PoolCode == PoolCode,
                cancellationToken);

        var existingMemberships = await dbContext.WarehouseWorkPoolMemberships
            .AsNoTracking()
            .Where(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.PoolCode == PoolCode
                && candidate.Active
                && candidate.EffectiveFromUtc <= nowUtc
                && (candidate.EffectiveToUtc == null || nowUtc < candidate.EffectiveToUtc))
            .ToArrayAsync(cancellationToken);

        var unapprovedPrincipalIds = existingMemberships
            .Select(membership => membership.PrincipalId)
            .Where(principalId => !string.Equals(principalId, PrincipalId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unapprovedPrincipalIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"WMS demo work pool '{PoolCode}' has unapproved effective members: " +
                string.Join(", ", unapprovedPrincipalIds));
        }

        if (pool is not null
            && (!pool.Active || !string.Equals(pool.SiteCode, SiteCode, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"WMS demo work pool '{PoolCode}' must be active at site '{SiteCode}'.");
        }

        var existingInboundOrder = await dbContext.InboundOrders
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .SingleOrDefaultAsync(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.InboundOrderNo == InboundOrderNo,
                cancellationToken);

        if (existingInboundOrder is not null
            && !IsCanonicalInboundOrder(existingInboundOrder, organizationId, environmentId))
        {
            throw new InvalidOperationException(
                $"WMS demo inbound order '{InboundOrderNo}' exists with non-canonical facts.");
        }

        // All conflict checks complete before the first Add. A failing opt-in cannot leave
        // a newly-created pool or membership behind an unsafe existing fact.
        var poolsWritten = 0;
        if (pool is null)
        {
            dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
                organizationId,
                environmentId,
                PoolCode,
                PoolDisplayName,
                SiteCode));
            poolsWritten++;
        }

        var membershipsWritten = 0;
        if (!existingMemberships.Any())
        {
            dbContext.WarehouseWorkPoolMemberships.Add(WarehouseWorkPoolMembership.Create(
                organizationId,
                environmentId,
                PoolCode,
                PrincipalId,
                nowUtc));
            membershipsWritten++;
        }

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

    private static bool IsCanonicalInboundOrder(
        InboundOrder order,
        string organizationId,
        string environmentId)
    {
        if (!string.Equals(order.OrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(order.EnvironmentId, environmentId, StringComparison.Ordinal)
            || !string.Equals(order.InboundOrderNo, InboundOrderNo, StringComparison.Ordinal)
            || !string.Equals(order.SourceDocumentType, SeedSourceDocumentType, StringComparison.Ordinal)
            || !string.Equals(order.SourceDocumentId, SeedSourceDocumentId, StringComparison.Ordinal)
            || !string.Equals(order.SiteCode, SiteCode, StringComparison.Ordinal)
            || !string.Equals(order.AssignedOperatorUserId, PrincipalId, StringComparison.Ordinal)
            || !string.Equals(order.AssignedPoolCode, PoolCode, StringComparison.Ordinal)
            || order.Status != InboundOrderStatus.Open
            || order.Version != 1
            || order.CompletedAtUtc is not null
            || order.CancelledAtUtc is not null
            || order.CancellationReason is not null
            || order.Lines.Count != 1)
        {
            return false;
        }

        var line = order.Lines.Single();
        return string.Equals(line.LineNo, WorldHistoryWmsSpec.LineNo, StringComparison.Ordinal)
            && string.Equals(line.SkuCode, SkuCode, StringComparison.Ordinal)
            && string.Equals(line.UomCode, UomCode, StringComparison.Ordinal)
            && line.ReceivedQuantity == 1m
            && string.Equals(line.StagingLocationCode, StagingLocationCode, StringComparison.Ordinal)
            && string.Equals(line.LotNo, LotNo, StringComparison.Ordinal)
            && line.SerialNo is null
            && string.Equals(line.QualityStatus, WorldHistoryWmsSpec.Unrestricted, StringComparison.Ordinal)
            && string.Equals(line.QualityGateStatus, InboundQualityGateStatuses.NotRequired, StringComparison.Ordinal)
            && line.InspectionRecordId is null
            && line.QualityDispositionReason is null
            && string.Equals(line.OwnerType, WorldHistoryWmsSpec.OwnerType, StringComparison.Ordinal)
            && line.OwnerId is null
            && line.ProductionDate is null
            && line.ExpiryDate is null;
    }
}

public sealed record WmsWorkPoolMembershipSeedReport(
    int WorkPoolsWritten,
    int WorkPoolMembershipsWritten,
    int InboundOrdersWritten);
