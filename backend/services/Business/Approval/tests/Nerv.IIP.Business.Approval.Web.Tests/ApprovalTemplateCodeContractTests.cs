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
    /// <summary>
    /// #1683 三方漂移契约（来源服务）：ERP 发起侧 / 审批种子侧 / ERP 回写消费侧共用
    /// <see cref="ApprovalSourceServices.BusinessErp"/>。种子此前写 <c>erp</c>，回写消费侧只认
    /// <c>business-erp</c>，不匹配即静默 <c>return</c>——采购审批通过后订单永停 pending 且无任何报错。
    /// 谁把种子常量改回去，本用例必红。
    /// </summary>
    [Fact]
    public void Seed_spec_and_contract_pin_the_same_purchase_source_service()
    {
        Assert.Equal("business-erp", ApprovalSourceServices.BusinessErp);
        Assert.Equal(ApprovalSourceServices.BusinessErp, WorldHistoryApprovalSpec.PurchaseSourceService);
    }

    /// <summary>
    /// #1683：常量对上还不够——真正落库的是 <c>BuildApprovalFacts</c> 产出的事实流。
    /// 每条采购审批事实的来源服务 / 单据类型都必须逐字等于契约常量（回写消费侧的分流依据）。
    /// </summary>
    [Fact]
    public void Purchase_approval_facts_carry_the_contract_source_service()
    {
        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(new DateOnly(2026, 7, 26), 0.2d);
        var purchaseFacts = facts
            .Where(x => string.Equals(x.TemplateCode, WorldHistoryApprovalSpec.PurchaseTemplateCode, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(purchaseFacts);
        Assert.All(purchaseFacts, fact =>
        {
            Assert.Equal(ApprovalSourceServices.BusinessErp, fact.SourceService);
            Assert.Equal(ApprovalDocumentTypes.PurchaseOrder, fact.DocumentType);
        });
    }

    /// <summary>任何一侧改动常量或种子字面量，本用例必红：权威码值 = 落库事实 APT-WB-PO-001。</summary>
    [Fact]
    public void Seed_spec_and_contract_pin_the_same_purchase_template_vocabulary()
    {
        Assert.Equal("APT-WB-PO-001", ApprovalTemplateCodes.PurchaseOrderRelease);
        Assert.Equal(ApprovalTemplateCodes.PurchaseOrderRelease, WorldHistoryApprovalSpec.PurchaseTemplateCode);
        Assert.Equal("purchase-order", WorldHistoryApprovalSpec.PurchaseDocumentType);

        // #1685：采购**变更**再审批拆出独立模板码（沿用 APT-WB- 号段），发起侧 / 种子侧共用。
        Assert.Equal("APT-WB-PO-002", ApprovalTemplateCodes.PurchaseOrderChange);
        Assert.Equal(ApprovalTemplateCodes.PurchaseOrderChange, WorldHistoryApprovalSpec.PurchaseChangeTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.PurchaseOrderRelease, ApprovalTemplateCodes.PurchaseOrderChange);

        // 单据类型刻意与下达相同：换新值就必须同步 ERP 回写消费侧 / 委托单据范围 / 界面词表三处。
        Assert.Equal(ApprovalDocumentTypes.PurchaseOrder, WorldHistoryApprovalSpec.PurchaseChangeDocumentType);
        Assert.Equal(ApprovalSourceServices.BusinessErp, WorldHistoryApprovalSpec.PurchaseChangeSourceService);

        // 收件箱待办表没有「模板」列，能分辨两类待办的是步骤名，因此两者必须不同。
        Assert.Equal("总经理审批", WorldHistoryApprovalSpec.PurchaseReleaseStepName);
        Assert.Equal("采购变更审批", WorldHistoryApprovalSpec.PurchaseChangeStepName);
        Assert.NotEqual(WorldHistoryApprovalSpec.PurchaseReleaseStepName, WorldHistoryApprovalSpec.PurchaseChangeStepName);

        // #1684：NCR 处置模板码收敛进契约（参与跨服务确定性回链盐串），权威码值 = 落库事实 APT-WB-NCR-001。
        Assert.Equal("APT-WB-NCR-001", ApprovalTemplateCodes.NcrDisposition);
        Assert.Equal(ApprovalTemplateCodes.NcrDisposition, WorldHistoryApprovalSpec.NcrTemplateCode);

        Assert.Equal("erp-sales-credit-release", ApprovalTemplateCodes.SalesCreditRelease);
        Assert.Equal(ApprovalTemplateCodes.SalesCreditRelease, WorldHistoryApprovalSpec.SalesCreditReleaseTemplateCode);

        // #1344 扩修：盘点差异（Inventory 发起侧默认值 ↔ 种子模板）。
        Assert.Equal("APT-WB-CNT-001", ApprovalTemplateCodes.StockCountVariance);
        Assert.Equal(ApprovalTemplateCodes.StockCountVariance, WorldHistoryApprovalSpec.StockCountVarianceTemplateCode);
        Assert.Equal("inventory-count-variance", ApprovalDocumentTypes.StockCountVariance);
        Assert.Equal(ApprovalDocumentTypes.StockCountVariance, WorldHistoryApprovalSpec.StockCountVarianceDocumentType);

        Assert.Equal("APT-WB-ECO-001", ApprovalTemplateCodes.EngineeringChangeOrder);
        Assert.Equal(ApprovalTemplateCodes.EngineeringChangeOrder, WorldHistoryApprovalSpec.EngineeringChangeTemplateCode);
        Assert.Equal("engineering-change-order", ApprovalDocumentTypes.EngineeringChangeOrder);
        Assert.Equal(ApprovalDocumentTypes.EngineeringChangeOrder, WorldHistoryApprovalSpec.EngineeringChangeDocumentType);
        Assert.Equal("product-engineering", WorldHistoryApprovalSpec.EngineeringChangeSourceService);
    }

    [Fact]
    public async Task Product_engineering_start_tuple_reaches_the_seeded_engineering_template()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            WorldHistoryApprovalSpec.EngineeringChangeTemplateCode,
            WorldHistoryApprovalSpec.EngineeringChangeDocumentType,
            version: 1,
            isActive: true,
            [
                new ApprovalTemplateStepDefinition(
                    1,
                    "工程变更评审",
                    null,
                    WorldHistoryApprovalSpec.ActorTypeUser,
                    WorldHistoryApprovalSpec.AdminUserId,
                    24),
            ]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var chainId = await new StartApprovalChainCommandHandler(dbContext).Handle(
            new StartApprovalChainCommand(
                "org-001",
                "env-dev",
                ApprovalTemplateCodes.EngineeringChangeOrder,
                WorldHistoryApprovalSpec.EngineeringChangeSourceService,
                ApprovalDocumentTypes.EngineeringChangeOrder,
                "ECO-20260801-000001",
                null,
                "user:user-engineer"),
            CancellationToken.None);

        var chain = await dbContext.ApprovalChains.SingleAsync(x => x.Id == chainId, CancellationToken.None);
        Assert.Equal(ApprovalTemplateCodes.EngineeringChangeOrder, chain.TemplateCode);
        Assert.Equal(ApprovalDocumentTypes.EngineeringChangeOrder, chain.DocumentReference.DocumentType);
        Assert.Equal(WorldHistoryApprovalSpec.EngineeringChangeSourceService, chain.DocumentReference.SourceService);
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

    /// <summary>
    /// #1685 的**核心不变量**：加一个模板码 = 种子里必须有一张同码同单据类型的模板，否则种子态必 400。
    ///
    /// 本用例不自建模板，而是跑**真实的种子服务**写库，再拿 ERP 变更发起元组
    /// （<see cref="ApprovalTemplateCodes.PurchaseOrderChange"/> / business-erp / purchase-order）开链、
    /// 由厂长一步审批通过。谁删掉种子里的变更模板块、把种子模板的单据类型改成别的值，
    /// 或让契约常量与种子常量漂移，本用例都必红。
    /// </summary>
    [Fact]
    public async Task Erp_change_start_tuple_reaches_the_template_written_by_the_world_history_seed()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WorldHistoryApprovalSeedService(dbContext).SeedAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 26),
            0.05d,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var seededChangeTemplate = await dbContext.ApprovalTemplates
            .AsNoTracking()
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == "org-001"
                && x.EnvironmentId == "env-dev"
                && x.TemplateCode == ApprovalTemplateCodes.PurchaseOrderChange);
        Assert.NotNull(seededChangeTemplate);
        Assert.Equal(ApprovalDocumentTypes.PurchaseOrder, seededChangeTemplate!.DocumentType);
        Assert.True(seededChangeTemplate.IsActive, "种子写入的采购变更模板必须是启用状态，否则发起侧仍然开不了链。");

        var chainId = await new StartApprovalChainCommandHandler(dbContext).Handle(
            new StartApprovalChainCommand(
                "org-001",
                "env-dev",
                ApprovalTemplateCodes.PurchaseOrderChange,
                ApprovalSourceServices.BusinessErp,
                ApprovalDocumentTypes.PurchaseOrder,
                "PO-20260731-000010",
                null,
                "user:user-emp-057",
                Amount: 1200m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var chain = await dbContext.ApprovalChains.Include(x => x.Steps).SingleAsync(x => x.Id == chainId);
        chain.ResolveStep(1, WorldHistoryApprovalSpec.ActorTypeUser, WorldHistoryApprovalSpec.AdminUserId, "approve", "同意变更");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(ApprovalChainStatuses.Approved, chain.Status);
        Assert.Equal(ApprovalTemplateCodes.PurchaseOrderChange, chain.TemplateCode);
        Assert.Equal(ApprovalDocumentTypes.PurchaseOrder, chain.DocumentReference.DocumentType);
    }

    /// <summary>
    /// #1685 的**体验目标**：同一张采购订单的下达链与变更链在收件箱里必须能分辨。
    ///
    /// 收件箱待办表只有「单据 / 当前步骤 / 单据类型 / 到期时间」四列，单据号与单据类型两类链完全一致，
    /// 因此判据落在**步骤名**上；顺带钉住两条链的待办唯一键不再相同（共用模板码时它们完全相同）。
    /// </summary>
    [Fact]
    public async Task Seeded_release_and_change_chains_on_the_same_order_are_distinguishable_in_the_inbox()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WorldHistoryApprovalSeedService(dbContext).SeedAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 26),
            0.05d,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var handler = new StartApprovalChainCommandHandler(dbContext);
        const string purchaseOrderNo = "PO-20260731-000011";
        var releaseChainId = await handler.Handle(
            new StartApprovalChainCommand(
                "org-001", "env-dev", ApprovalTemplateCodes.PurchaseOrderRelease, ApprovalSourceServices.BusinessErp,
                ApprovalDocumentTypes.PurchaseOrder, purchaseOrderNo, null, "user:user-emp-057"),
            CancellationToken.None);
        var changeChainId = await handler.Handle(
            new StartApprovalChainCommand(
                "org-001", "env-dev", ApprovalTemplateCodes.PurchaseOrderChange, ApprovalSourceServices.BusinessErp,
                ApprovalDocumentTypes.PurchaseOrder, purchaseOrderNo, null, "user:user-emp-057"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.NotEqual(releaseChainId, changeChainId);
        var releaseChain = await dbContext.ApprovalChains.AsNoTracking().Include(x => x.Steps).SingleAsync(x => x.Id == releaseChainId);
        var changeChain = await dbContext.ApprovalChains.AsNoTracking().Include(x => x.Steps).SingleAsync(x => x.Id == changeChainId);

        // 单据号与单据类型两条链一致——这正是收件箱此前分辨不出来的原因。
        Assert.Equal(releaseChain.DocumentReference.DocumentId, changeChain.DocumentReference.DocumentId);
        Assert.Equal(releaseChain.DocumentReference.DocumentType, changeChain.DocumentReference.DocumentType);

        // 待办列表展示的「当前步骤」必须不同。
        Assert.Equal(WorldHistoryApprovalSpec.PurchaseReleaseStepName, releaseChain.Steps.Single().StepName);
        Assert.Equal(WorldHistoryApprovalSpec.PurchaseChangeStepName, changeChain.Steps.Single().StepName);
        Assert.NotEqual(releaseChain.Steps.Single().StepName, changeChain.Steps.Single().StepName);

        // 待办唯一键含模板码：共用模板码时两条链的唯一键完全相同（同一张订单只能有一条待办链）。
        Assert.NotEqual(releaseChain.PendingIdentityKey, changeChain.PendingIdentityKey);
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
                    StepName: WorldHistoryApprovalSpec.PurchaseReleaseStepName,
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
