using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Infrastructure;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Seed;
using Nerv.IIP.Contracts.Approval;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Approval.Web.Tests;

/// <summary>
/// #1344 三方漂移契约（审批 / 种子侧）：审批模板码的唯一事实来源是
/// <see cref="ApprovalTemplateCodes"/>，种子模板、业务服务发起侧、界面侧共用。
///
/// 覆盖两例词表错配：ERP 硬编码 <c>erp-purchase-order-release</c> 而种子落库
/// <c>APT-WB-PO-001</c>（第六例，转单 / RFQ 必 400）；Inventory 盘点差异默认
/// <c>COUNT-VARIANCE</c> 而种子根本没有该模板（第八例，差异超阈值盘点确认必 400）。
/// </summary>
public sealed class ApprovalTemplateCodeContractTests
{
    /// <summary>任何一侧改动常量或种子字面量，本用例必红：权威码值 = 落库事实 APT-WB-PO-001。</summary>
    [Fact]
    public void Seed_spec_and_contract_pin_the_same_purchase_template_vocabulary()
    {
        Assert.Equal("APT-WB-PO-001", ApprovalTemplateCodes.PurchaseOrderRelease);
        Assert.Equal(ApprovalTemplateCodes.PurchaseOrderRelease, WorldHistoryApprovalSpec.PurchaseTemplateCode);
        Assert.Equal("purchase-order", WorldHistoryApprovalSpec.PurchaseDocumentType);

        Assert.Equal("erp-sales-credit-release", ApprovalTemplateCodes.SalesCreditRelease);
        Assert.Equal(ApprovalTemplateCodes.SalesCreditRelease, WorldHistoryApprovalSpec.SalesCreditReleaseTemplateCode);

        // #1344 扩修：盘点差异（Inventory 发起侧默认值 ↔ 种子模板）。
        Assert.Equal("APT-WB-CNT-001", ApprovalTemplateCodes.StockCountVariance);
        Assert.Equal(ApprovalTemplateCodes.StockCountVariance, WorldHistoryApprovalSpec.StockCountVarianceTemplateCode);
        Assert.Equal("inventory-count-variance", ApprovalDocumentTypes.StockCountVariance);
        Assert.Equal(ApprovalDocumentTypes.StockCountVariance, WorldHistoryApprovalSpec.StockCountVarianceDocumentType);
    }

    /// <summary>
    /// #1344 扩修（第八例）：Inventory 盘点差异发起元组（默认模板码 / inventory / 单据类型）
    /// 必须命中种子补齐的 <c>APT-WB-CNT-001</c> 模板并可由厂长核准——**不新建模板**。
    /// </summary>
    [Fact]
    public async Task Inventory_stock_count_variance_tuple_reaches_the_seeded_template()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            WorldHistoryApprovalSpec.StockCountVarianceTemplateCode,
            WorldHistoryApprovalSpec.StockCountVarianceDocumentType,
            version: 1,
            isActive: true,
            [
                new ApprovalTemplateStepDefinition(
                    StepNo: 1,
                    StepName: "盘点差异核准",
                    ParallelGroupKey: null,
                    ApproverType: WorldHistoryApprovalSpec.ActorTypeUser,
                    ApproverRef: WorldHistoryApprovalSpec.AdminUserId,
                    DueInHours: 24),
            ]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var chainId = await new StartApprovalChainCommandHandler(dbContext).Handle(
            new StartApprovalChainCommand(
                "org-001",
                "env-dev",
                ApprovalTemplateCodes.StockCountVariance,
                "inventory",
                ApprovalDocumentTypes.StockCountVariance,
                "CNT-20260731-000001",
                null,
                "system:inventory",
                Amount: 2400m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var chain = await dbContext.ApprovalChains.Include(x => x.Steps).SingleAsync(x => x.Id == chainId);
        chain.ResolveStep(1, WorldHistoryApprovalSpec.ActorTypeUser, WorldHistoryApprovalSpec.AdminUserId, "approve", "差异核准，允许调整账面");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(ApprovalChainStatuses.Approved, chain.Status);
    }

    /// <summary>旧默认值 <c>COUNT-VARIANCE</c> 从未有模板落库：谁改回去，种子态就是这条 400。</summary>
    [Fact]
    public async Task Legacy_count_variance_literal_still_finds_no_template_in_seed_state()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            WorldHistoryApprovalSpec.StockCountVarianceTemplateCode,
            WorldHistoryApprovalSpec.StockCountVarianceDocumentType,
            version: 1,
            isActive: true,
            [
                new ApprovalTemplateStepDefinition(1, "盘点差异核准", null, WorldHistoryApprovalSpec.ActorTypeUser, WorldHistoryApprovalSpec.AdminUserId, 24),
            ]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new StartApprovalChainCommandHandler(dbContext).Handle(
                new StartApprovalChainCommand(
                    "org-001",
                    "env-dev",
                    "COUNT-VARIANCE",
                    "inventory",
                    ApprovalDocumentTypes.StockCountVariance,
                    "CNT-20260731-000002",
                    null,
                    "system:inventory"),
                CancellationToken.None));

        // 文案在本分支已中文化（MAN-698 批次 A）：断言只换措辞，语义仍是「查无此模板」。
        Assert.Contains("审批模板不存在", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 种子态全链：种子形状的采购模板 + ERP 发起元组（模板码 / business-erp / purchase-order）
    /// 必须能开链，并由厂长（user-admin）一步审批通过——不新建任何模板。
    /// </summary>
    [Fact]
    public async Task Erp_start_tuple_reaches_the_seeded_template_and_can_be_approved()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(NewSeedShapedPurchaseTemplate());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StartApprovalChainCommandHandler(dbContext);

        var chainId = await handler.Handle(
            new StartApprovalChainCommand(
                "org-001",
                "env-dev",
                ApprovalTemplateCodes.PurchaseOrderRelease,
                "business-erp",
                WorldHistoryApprovalSpec.PurchaseDocumentType,
                "PO-20260731-000001",
                null,
                "system:erp",
                Amount: 84m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var chain = await dbContext.ApprovalChains.Include(x => x.Steps).SingleAsync(x => x.Id == chainId);
        chain.ResolveStep(1, WorldHistoryApprovalSpec.ActorTypeUser, WorldHistoryApprovalSpec.AdminUserId, "approve", "同意下达");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(ApprovalChainStatuses.Approved, chain.Status);
        Assert.Equal(ApprovalTemplateCodes.PurchaseOrderRelease, chain.TemplateCode);
    }

    /// <summary>旧字面量从未有模板落库：谁再把发起侧改回去，这里就是它在种子态得到的 400。</summary>
    [Fact]
    public async Task Legacy_erp_literal_still_finds_no_template_in_seed_state()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(NewSeedShapedPurchaseTemplate());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StartApprovalChainCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new StartApprovalChainCommand(
                "org-001",
                "env-dev",
                "erp-purchase-order-release",
                "business-erp",
                WorldHistoryApprovalSpec.PurchaseDocumentType,
                "PO-20260731-000002",
                null,
                "system:erp"),
            CancellationToken.None));

        // 文案在本分支已中文化（MAN-698 批次 A）：断言只换措辞，语义仍是「查无此模板」。
        Assert.Contains("审批模板不存在", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>与 <c>WorldHistoryApprovalSeedService.SeedTemplatesAsync</c> 同形状的采购模板（不落任何演示专属字段）。</summary>
    private static ApprovalTemplate NewSeedShapedPurchaseTemplate()
    {
        return ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            WorldHistoryApprovalSpec.PurchaseTemplateCode,
            WorldHistoryApprovalSpec.PurchaseDocumentType,
            version: 1,
            isActive: true,
            [
                new ApprovalTemplateStepDefinition(
                    StepNo: 1,
                    StepName: "总经理审批",
                    ParallelGroupKey: null,
                    ApproverType: WorldHistoryApprovalSpec.ActorTypeUser,
                    ApproverRef: WorldHistoryApprovalSpec.AdminUserId,
                    DueInHours: 24),
            ]);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"approval-po-template-{Guid.CreateVersion7():N}";
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName, databaseRoot));
        return services.BuildServiceProvider();
    }
}
