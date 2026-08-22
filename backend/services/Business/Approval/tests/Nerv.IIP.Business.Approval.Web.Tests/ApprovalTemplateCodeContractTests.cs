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
/// <see cref="ApprovalTemplateCodes"/>，产品基线种子、业务服务发起侧、界面侧共用。
/// WorldHistory 的 <c>APT-WB-*</c> 旧码只用于已持久历史事实，不得再作为产品发起契约。
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

    /// <summary>
    /// #1702 三方漂移契约（族 2：NCR 处置审批来源服务）：种子侧此前是裸字面量 <c>quality</c>，
    /// 现收敛到 <see cref="ApprovalSourceServices.Quality"/>。该值逐字参与
    /// <c>ApprovalChain.BuildPendingIdentityKey</c> 的 SHA256（键上有唯一索引），
    /// 谁把种子常量改回字面量或改值，本用例必红。
    /// </summary>
    [Fact]
    public void Seed_spec_and_contract_pin_the_same_ncr_source_service()
    {
        Assert.Equal("quality", ApprovalSourceServices.Quality);
        Assert.Equal(ApprovalSourceServices.Quality, WorldHistoryApprovalSpec.NcrSourceService);
    }

    /// <summary>
    /// #1702：常量对上还不够——真正落库的是 <c>BuildApprovalFacts</c> 产出的事实流。
    /// 每条 NCR 处置审批事实的来源服务 / 单据类型都必须逐字等于契约常量
    /// （它们一起进 <c>PendingIdentityKey</c>，也是未来质量侧回写消费者的分流依据）。
    /// </summary>
    [Fact]
    public void Ncr_approval_facts_carry_the_contract_source_service()
    {
        var facts = WorldHistoryApprovalSpec.BuildApprovalFacts(new DateOnly(2026, 7, 26), 0.2d);
        var ncrFacts = facts
            .Where(x => string.Equals(x.TemplateCode, WorldHistoryApprovalSpec.NcrTemplateCode, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(ncrFacts);
        Assert.All(ncrFacts, fact =>
        {
            Assert.Equal(ApprovalSourceServices.Quality, fact.SourceService);
            Assert.Equal(ApprovalDocumentTypes.NcrDisposition, fact.DocumentType);
        });
    }

    /// <summary>
    /// #1702 三方漂移契约（族 3：信用解冻单据类型）：ERP 发起侧 / 审批种子模板 / ERP 回写消费侧
    /// 此前三处各写各的字面量，现共用 <see cref="ApprovalDocumentTypes.SalesOrderCreditRelease"/>。
    /// 种子漂移即发起 400（模板按 <c>(templateCode, documentType)</c> 双条件命中），
    /// 消费侧漂移即回写静默丢事件（订单永停 credit-held）。
    /// </summary>
    [Fact]
    public void Seed_spec_and_contract_pin_the_same_sales_credit_release_document_type()
    {
        Assert.Equal("sales-order-credit-release", ApprovalDocumentTypes.SalesOrderCreditRelease);
        Assert.Equal(ApprovalDocumentTypes.SalesOrderCreditRelease, WorldHistoryApprovalSpec.SalesCreditReleaseDocumentType);
    }

    /// <summary>
    /// #1702 三方漂移契约（族 1：盘点差异来源服务）：Inventory 发起侧与 Inventory 回写消费侧共用
    /// <see cref="ApprovalSourceServices.Inventory"/>（#1344 只收敛了同一个 <c>if</c> 里的单据类型）。
    /// </summary>
    [Fact]
    public void Contract_pins_the_inventory_approval_source_service()
    {
        Assert.Equal("inventory", ApprovalSourceServices.Inventory);
    }

    /// <summary>
    /// #1702：三族改动的 <c>sourceService</c> / <c>documentType</c> 都逐字进
    /// <c>ApprovalChain.BuildPendingIdentityKey</c> 的 SHA256，而该键在 <c>approval_chains</c> 上有唯一索引
    /// （同一 (org, env, templateCode, 单据引用) 只允许一条在跑的链）。
    /// 因此词表漂移不只是回写丢事件：它同时把 pending 唯一键换成另一把——旧的 pending 链拦不住新链，
    /// 已落库的历史链也再算不出同一个键。本用例把这条因果钉成可执行断言。
    /// </summary>
    [Fact]
    public void Vocabulary_drift_changes_the_pending_identity_key()
    {
        var authoritative = ApprovalChain.BuildPendingIdentityKey(
            "org-001",
            "env-dev",
            ApprovalTemplateCodes.SalesCreditRelease,
            new ApprovalDocumentReference(
                ApprovalSourceServices.BusinessErp,
                ApprovalDocumentTypes.SalesOrderCreditRelease,
                "SO-001",
                documentLineId: null));

        var driftedDocumentType = ApprovalChain.BuildPendingIdentityKey(
            "org-001",
            "env-dev",
            ApprovalTemplateCodes.SalesCreditRelease,
            new ApprovalDocumentReference(
                ApprovalSourceServices.BusinessErp,
                "sales-credit-release",
                "SO-001",
                documentLineId: null));

        var driftedSourceService = ApprovalChain.BuildPendingIdentityKey(
            "org-001",
            "env-dev",
            ApprovalTemplateCodes.StockCountVariance,
            new ApprovalDocumentReference(
                "business-inventory",
                ApprovalDocumentTypes.StockCountVariance,
                "CNT-20260731-000001",
                documentLineId: null));

        var authoritativeStockCount = ApprovalChain.BuildPendingIdentityKey(
            "org-001",
            "env-dev",
            ApprovalTemplateCodes.StockCountVariance,
            new ApprovalDocumentReference(
                ApprovalSourceServices.Inventory,
                ApprovalDocumentTypes.StockCountVariance,
                "CNT-20260731-000001",
                documentLineId: null));

        Assert.NotEqual(authoritative, driftedDocumentType);
        Assert.NotEqual(authoritativeStockCount, driftedSourceService);
    }

    /// <summary>
    /// #1702 同值不同义护栏：<c>quality</c> / <c>inventory</c> 在库存契约里另有其义
    /// （<c>InventoryMovementSourceServices.Quality</c> 是库存流水来源，<c>inventory</c> 还是 schema 名 /
    /// 库存流水 sourceService），两族取值恰好同字面量但**不得互相引用**。
    /// 用「审批契约程序集不引用库存契约程序集」把这条边界钉成可执行断言：
    /// 谁哪天图省事把审批词表指向库存词表，本用例必红。
    /// </summary>
    [Fact]
    public void Approval_vocabulary_assembly_does_not_borrow_inventory_vocabulary()
    {
        var referenced = typeof(ApprovalSourceServices).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .Where(x => x is not null)
            .ToArray();

        Assert.DoesNotContain("Nerv.IIP.Contracts.Inventory", referenced);
    }

    /// <summary>产品码与 WorldHistory 旧码必须分离；旧码仍保持逐字冻结。</summary>
    [Fact]
    public void Seed_spec_and_contract_pin_the_same_purchase_template_vocabulary()
    {
        Assert.Equal("purchase-order-release", ApprovalTemplateCodes.PurchaseOrderRelease);
        Assert.Equal("APT-WB-PO-001", WorldHistoryApprovalSpec.PurchaseTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.PurchaseOrderRelease, WorldHistoryApprovalSpec.PurchaseTemplateCode);
        Assert.Equal("purchase-order", WorldHistoryApprovalSpec.PurchaseDocumentType);

        Assert.Equal("purchase-order-change", ApprovalTemplateCodes.PurchaseOrderChange);
        Assert.Equal("APT-WB-PO-002", WorldHistoryApprovalSpec.PurchaseChangeTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.PurchaseOrderChange, WorldHistoryApprovalSpec.PurchaseChangeTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.PurchaseOrderRelease, ApprovalTemplateCodes.PurchaseOrderChange);

        // 单据类型刻意与下达相同：换新值就必须同步 ERP 回写消费侧 / 委托单据范围 / 界面词表三处。
        Assert.Equal(ApprovalDocumentTypes.PurchaseOrder, WorldHistoryApprovalSpec.PurchaseChangeDocumentType);
        Assert.Equal(ApprovalSourceServices.BusinessErp, WorldHistoryApprovalSpec.PurchaseChangeSourceService);

        // 收件箱待办表没有「模板」列，能分辨两类待办的是步骤名，因此两者必须不同。
        Assert.Equal("总经理审批", WorldHistoryApprovalSpec.PurchaseReleaseStepName);
        Assert.Equal("采购变更审批", WorldHistoryApprovalSpec.PurchaseChangeStepName);
        Assert.NotEqual(WorldHistoryApprovalSpec.PurchaseReleaseStepName, WorldHistoryApprovalSpec.PurchaseChangeStepName);

        Assert.Equal("ncr-disposition", ApprovalTemplateCodes.NcrDisposition);
        Assert.Equal("APT-WB-NCR-001", WorldHistoryApprovalSpec.NcrTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.NcrDisposition, WorldHistoryApprovalSpec.NcrTemplateCode);

        Assert.Equal("erp-sales-credit-release", ApprovalTemplateCodes.SalesCreditRelease);
        Assert.Equal(ApprovalTemplateCodes.SalesCreditRelease, WorldHistoryApprovalSpec.SalesCreditReleaseTemplateCode);

        // #1344 扩修：盘点差异（Inventory 发起侧默认值 ↔ 种子模板）。
        Assert.Equal("stock-count-variance", ApprovalTemplateCodes.StockCountVariance);
        Assert.Equal("APT-WB-CNT-001", WorldHistoryApprovalSpec.StockCountVarianceTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.StockCountVariance, WorldHistoryApprovalSpec.StockCountVarianceTemplateCode);
        Assert.Equal("inventory-count-variance", ApprovalDocumentTypes.StockCountVariance);
        Assert.Equal(ApprovalDocumentTypes.StockCountVariance, WorldHistoryApprovalSpec.StockCountVarianceDocumentType);

        Assert.Equal("engineering-change-order", ApprovalTemplateCodes.EngineeringChangeOrder);
        Assert.Equal("APT-WB-ECO-001", WorldHistoryApprovalSpec.EngineeringChangeTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.EngineeringChangeOrder, WorldHistoryApprovalSpec.EngineeringChangeTemplateCode);
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
            ApprovalTemplateCodes.EngineeringChangeOrder,
            ApprovalDocumentTypes.EngineeringChangeOrder,
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
    /// 必须命中产品种子补齐的中性码模板并可由管理员核准。
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
            ApprovalTemplateCodes.StockCountVariance,
            ApprovalDocumentTypes.StockCountVariance,
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
                ApprovalSourceServices.Inventory,
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
            ApprovalTemplateCodes.StockCountVariance,
            ApprovalDocumentTypes.StockCountVariance,
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
                    ApprovalSourceServices.Inventory,
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
    public async Task Erp_change_start_tuple_reaches_the_template_written_by_the_product_seed()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new ApprovalSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);
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
        await new ApprovalSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);
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

    /// <summary>与 Approval 产品基线 seed 同形状的采购模板。</summary>
    private static ApprovalTemplate NewSeedShapedPurchaseTemplate()
    {
        return ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            ApprovalTemplateCodes.PurchaseOrderRelease,
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
