using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Infrastructure;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Approval.Web.Application.Seed;

/// <summary>
/// Approval 产品基线 seed：为全新环境补齐跨业务域开链所需的六张模板。
/// 本 seed 不依赖 LeaderDemo/WorldHistory；按 org/env + templateCode 幂等只补缺，
/// 已存在的模板（包括被租户停用或改写的定义）一律保留。
/// </summary>
public sealed class ApprovalSeedService(ApplicationDbContext dbContext)
{
    private const string ActorTypeUser = "user";
    private const string AdminUserId = "user-admin";
    private const int StepDueInHours = 24;

    private sealed record TemplateSeed(string TemplateCode, string DocumentType, string StepName);

    private static readonly TemplateSeed[] Templates =
    [
        new(ApprovalTemplateCodes.PurchaseOrderRelease, ApprovalDocumentTypes.PurchaseOrder, "总经理审批"),
        new(ApprovalTemplateCodes.PurchaseOrderChange, ApprovalDocumentTypes.PurchaseOrder, "采购变更审批"),
        new(ApprovalTemplateCodes.NcrDisposition, ApprovalDocumentTypes.NcrDisposition, "NCR 处置评审"),
        new(ApprovalTemplateCodes.SalesCreditRelease, ApprovalDocumentTypes.SalesOrderCreditRelease, "信用解冻复核"),
        new(ApprovalTemplateCodes.StockCountVariance, ApprovalDocumentTypes.StockCountVariance, "盘点差异核准"),
        new(ApprovalTemplateCodes.EngineeringChangeOrder, ApprovalDocumentTypes.EngineeringChangeOrder, "工程变更评审"),
    ];

    public async Task<int> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var templateCodes = Templates.Select(x => x.TemplateCode).ToArray();
        var existing = (await dbContext.ApprovalTemplates
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                    && templateCodes.Contains(x.TemplateCode))
                .Select(x => x.TemplateCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var seed in Templates)
        {
            if (existing.Contains(seed.TemplateCode))
            {
                continue;
            }

            dbContext.ApprovalTemplates.Add(ApprovalTemplate.Create(
                organizationId,
                environmentId,
                seed.TemplateCode,
                seed.DocumentType,
                version: 1,
                isActive: true,
                [
                    new ApprovalTemplateStepDefinition(
                        StepNo: 1,
                        StepName: seed.StepName,
                        ParallelGroupKey: null,
                        ApproverType: ActorTypeUser,
                        ApproverRef: AdminUserId,
                        DueInHours: StepDueInHours),
                ]));
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return written;
    }
}
