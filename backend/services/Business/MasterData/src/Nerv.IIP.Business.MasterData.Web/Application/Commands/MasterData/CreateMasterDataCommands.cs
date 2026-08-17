using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataResourceResult(string ResourceType, string Code, string DisplayName);

public sealed record CreateSkuCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string BaseUomCode,
    string Category,
    string MaterialType,
    string BatchTrackingPolicy,
    string SerialTrackingPolicy,
    string ShelfLifePolicyCode,
    string StorageConditionCode,
    string DefaultBarcodeRuleCode,
    bool QualityRequired,
    IReadOnlyCollection<string> ComplianceTags,
    string? IdempotencyKey = null,
    string? InventoryUomCode = null,
    string? PurchaseUomCode = null,
    string? SalesUomCode = null,
    string? ManufacturingUomCode = null,
    string? ProcurementType = null,
    string? MrpType = null,
    string? LotSizingPolicy = null,
    decimal? MinimumLotSize = null,
    decimal? MaximumLotSize = null,
    decimal? LotSizeMultiple = null,
    decimal? SafetyStockQuantity = null,
    decimal? ReorderPointQuantity = null,
    int? PlannedDeliveryTimeDays = null,
    int? InHouseProductionTimeDays = null,
    int? GoodsReceiptProcessingTimeDays = null,
    int? ShelfLifeDays = null,
    int? NearExpiryThresholdDays = null,
    string? AbcClass = null,
    string? LifecycleStatus = "active",
    bool PurchasingEnabled = true,
    bool ManufacturingEnabled = true,
    bool SalesEnabled = true) : ICommand<MasterDataResourceResult>;

public sealed class CreateSkuCommandHandler : ICommandHandler<CreateSkuCommand, MasterDataResourceResult>
{
    private readonly ISkuRepository _repository;
    private readonly IReferenceDataCodeRepository? _referenceDataRepository;
    private readonly ApplicationDbContext? _dbContext;
    private readonly MasterDataCodingService _codingService;

    public CreateSkuCommandHandler(ISkuRepository repository, MasterDataCodingService? codingService = null)
    {
        _repository = repository;
        _referenceDataRepository = null;
        _dbContext = null;
        _codingService = codingService ?? new MasterDataCodingService();
    }

    public CreateSkuCommandHandler(
        ISkuRepository repository,
        IReferenceDataCodeRepository referenceDataRepository,
        MasterDataCodingService? codingService = null)
    {
        _repository = repository;
        _referenceDataRepository = referenceDataRepository;
        _dbContext = null;
        _codingService = codingService ?? new MasterDataCodingService();
    }

    public CreateSkuCommandHandler(
        ISkuRepository repository,
        IReferenceDataCodeRepository referenceDataRepository,
        ApplicationDbContext dbContext,
        MasterDataCodingService? codingService = null)
    {
        _repository = repository;
        _referenceDataRepository = referenceDataRepository;
        _dbContext = dbContext;
        _codingService = codingService ?? new MasterDataCodingService();
    }

    public async Task<MasterDataResourceResult> Handle(CreateSkuCommand request, CancellationToken cancellationToken)
    {
        await ValidateControlledReferenceDataAsync(request, cancellationToken);
        await ValidateChannelUomsAsync(request, cancellationToken);

        var allocation = await _codingService.AllocateSkuCodeAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            SkuPayloadFingerprint(request),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var persisted = await _repository.FindByBusinessKeyAsync(
                request.OrganizationId,
                request.EnvironmentId,
                allocation.Code,
                cancellationToken);
            if (persisted is null)
            {
                throw new KnownException($"SKU '{allocation.Code}' 的幂等记录已存在，但未找到对应资源。");
            }

            return new MasterDataResourceResult("sku", persisted.Code, persisted.Name);
        }

        if (await _repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, cancellationToken))
        {
            throw new KnownException($"SKU '{allocation.Code}' 已存在。");
        }

        var sku = Sku.CreateIndustrial(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.Name,
            request.BaseUomCode,
            request.Category,
            request.MaterialType,
            request.BatchTrackingPolicy,
            request.SerialTrackingPolicy,
            request.ShelfLifePolicyCode,
            request.StorageConditionCode,
            request.DefaultBarcodeRuleCode,
            request.QualityRequired,
            request.ComplianceTags,
            request.InventoryUomCode,
            request.PurchaseUomCode,
            request.SalesUomCode,
            request.ManufacturingUomCode,
            request.ProcurementType,
            request.MrpType,
            request.LotSizingPolicy,
            request.MinimumLotSize,
            request.MaximumLotSize,
            request.LotSizeMultiple,
            request.SafetyStockQuantity,
            request.ReorderPointQuantity,
            request.PlannedDeliveryTimeDays,
            request.InHouseProductionTimeDays,
            request.GoodsReceiptProcessingTimeDays,
            request.AbcClass,
            request.LifecycleStatus,
            request.PurchasingEnabled,
            request.ManufacturingEnabled,
            request.SalesEnabled,
            request.ShelfLifeDays,
            request.NearExpiryThresholdDays);
        await _repository.AddAsync(sku, cancellationToken);
        return new MasterDataResourceResult("sku", sku.Code, sku.Name);
    }

    private async Task ValidateChannelUomsAsync(CreateSkuCommand request, CancellationToken cancellationToken)
    {
        await SkuChannelUomValidator.ValidateAsync(
            _dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.BaseUomCode,
            [request.InventoryUomCode, request.PurchaseUomCode, request.SalesUomCode, request.ManufacturingUomCode],
            cancellationToken);
    }

    private async Task ValidateControlledReferenceDataAsync(CreateSkuCommand request, CancellationToken cancellationToken)
    {
        // 分类的权威值域是产品分类目录实体，不在受控字典循环里（#1596）。
        await SkuCategoryValidator.ValidateAsync(_dbContext, _referenceDataRepository, request.OrganizationId, request.EnvironmentId, request.Category, allowLegacyFallback: false, cancellationToken);

        if (_referenceDataRepository is null)
        {
            return;
        }

        foreach (var reference in MasterDataDictionaryRules.GetCreateSkuReferences(
            request.MaterialType,
            request.BatchTrackingPolicy,
            request.SerialTrackingPolicy,
            request.ShelfLifePolicyCode,
            request.StorageConditionCode,
            request.DefaultBarcodeRuleCode,
            request.ComplianceTags))
        {
            if (string.IsNullOrWhiteSpace(reference.Code))
            {
                throw new KnownException($"SKU 字段 '{reference.Field}' 必须引用已启用的 '{reference.CodeSet}' 代码。");
            }

            var exists = await _referenceDataRepository.ExistsActiveAsync(
                request.OrganizationId,
                request.EnvironmentId,
                reference.CodeSet,
                reference.Code.Trim(),
                cancellationToken);
            if (!exists)
            {
                throw new KnownException($"SKU 字段 '{reference.Field}' 引用的参考数据 '{reference.CodeSet}:{reference.Code}' 不存在或未启用。");
            }
        }
    }

    private static string SkuPayloadFingerprint(CreateSkuCommand request)
    {
        return string.Join('|',
            request.OrganizationId,
            request.EnvironmentId,
            request.Name,
            request.BaseUomCode,
            request.Category,
            request.MaterialType,
            request.BatchTrackingPolicy,
            request.SerialTrackingPolicy,
            request.ShelfLifePolicyCode,
            request.StorageConditionCode,
            request.DefaultBarcodeRuleCode,
            request.QualityRequired,
            string.Join(',', request.ComplianceTags.Order(StringComparer.Ordinal)),
            request.InventoryUomCode,
            request.PurchaseUomCode,
            request.SalesUomCode,
            request.ManufacturingUomCode,
            request.ProcurementType,
            request.MrpType,
            request.LotSizingPolicy,
            request.MinimumLotSize,
            request.MaximumLotSize,
            request.LotSizeMultiple,
            request.SafetyStockQuantity,
            request.ReorderPointQuantity,
            request.PlannedDeliveryTimeDays,
            request.InHouseProductionTimeDays,
            request.GoodsReceiptProcessingTimeDays,
            request.ShelfLifeDays,
            request.NearExpiryThresholdDays,
            request.AbcClass,
            request.LifecycleStatus,
            request.PurchasingEnabled,
            request.ManufacturingEnabled,
            request.SalesEnabled);
    }
}

/// <summary>
/// SKU 的「产品分类」值域校验（#1596，口径裁决 A）。
///
/// 权威值域是**产品分类目录实体**（ProductCategory，`PCAT-*` 层级树），不是 reference-data
/// 的 `product-category` CodeSet——后者已按 <c>master-data-dictionary-rules.md</c> §1
/// 「独立目录兼容」降级为 legacy。此前后端只认 CodeSet，而界面下拉给的是实体编码，
/// 两个值空间不相交，新建物料表单提交必 400。
///
/// legacy 兼容**只给更新路径**（<c>allowLegacyFallback</c>）：文档要求「完全切换前
/// 不得破坏 SKU <c>category</c> 对 CodeSet 的兼容读取」，而这条理由只覆盖「编辑一条老物料不该被
/// 它自己的历史分类挡住」。**新建一律只认实体**，否则新数据会持续流入待退役的值空间。
/// 旧码迁移与 CodeSet 正式退役另行处理。
///
/// 实体一旦按 code 命中即为**权威判定**：命中且已停用就直接拒，不再落到 legacy 支路——
/// 否则 CodeSet 里放一条同码启用条目，就能把「停用的分类不可再用」这条不变量整个绕过去。
/// </summary>
public static class SkuCategoryValidator
{
    public const string LegacyCodeSet = "product-category";

    public static async Task ValidateAsync(
        ApplicationDbContext? dbContext,
        IReferenceDataCodeRepository? referenceDataRepository,
        string organizationId,
        string environmentId,
        string? category,
        bool allowLegacyFallback,
        CancellationToken cancellationToken)
    {
        // 更新命令未提交该字段：不是「填了空」，是「没改」，不校验。
        if (category is null)
        {
            return;
        }

        var code = category.Trim();
        if (code.Length == 0)
        {
            throw new KnownException("SKU 字段 'Category' 必须引用已启用的产品分类。");
        }

        // 两条数据源哪条在就用哪条：handler 有三个构造重载，只认 dbContext 会让不带它的那条
        // 路径静默失去校验——「依赖缺失就放行」正是最容易积成静默缺口的写法。
        if (dbContext is null && referenceDataRepository is null)
        {
            return;
        }

        if (dbContext is not null)
        {
            // 按 code 取实体的停用标记：命中即权威，不带 Disabled 过滤——带了的话「停用实体」
            // 会退化成「查无此条」，再被同码 legacy 兜底放行。
            var entityDisabled = await dbContext.ProductCategories
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.CategoryCode == code)
                .Select(x => (bool?)x.Disabled)
                .FirstOrDefaultAsync(cancellationToken);
            if (entityDisabled is not null)
            {
                if (entityDisabled == false)
                {
                    return;
                }

                throw new KnownException(
                    $"SKU 字段 'Category' 引用的产品分类 '{code}' 已停用。");
            }

            // 实体里完全没有这个 code，才轮到 legacy 兼容（且仅更新路径）。
            if (allowLegacyFallback)
            {
                var isActiveLegacyCode = await dbContext.ReferenceDataCodes.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    !x.Disabled &&
                    x.CodeSet == LegacyCodeSet &&
                    x.Code == code,
                    cancellationToken);
                if (isActiveLegacyCode)
                {
                    return;
                }
            }
        }
        else if (await referenceDataRepository!.ExistsActiveAsync(organizationId, environmentId, LegacyCodeSet, code, cancellationToken))
        {
            // 降级路径：只有字典仓储、没有持久化上下文时无从查实体，只能退回 legacy 校验。
            // 该重载只被测试使用（运行时 DI 走带 dbContext 的版本），故保留而不是一律拒绝；
            // 「新建只认实体」的口径由带 dbContext 的 handler 级测试钉住。
            return;
        }

        throw new KnownException(
            $"SKU 字段 'Category' 引用的产品分类 '{code}' 不存在或未启用。");
    }
}

internal static class SkuChannelUomValidator
{
    public static async Task ValidateAsync(
        ApplicationDbContext? dbContext,
        string organizationId,
        string environmentId,
        string baseUomCode,
        IEnumerable<string?> channelUomCodes,
        CancellationToken cancellationToken)
    {
        var baseUom = baseUomCode.Trim();
        var channelUoms = channelUomCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(x => !string.Equals(x, baseUom, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (channelUoms.Length == 0)
        {
            return;
        }

        if (dbContext is null)
        {
            throw new KnownException("校验 SKU 渠道计量单位需要主数据持久化上下文。");
        }

        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var channelUom in channelUoms)
        {
            // MVP rule: require an active direct conversion from each channel UOM to the SKU base UOM.
            // Reverse and transitive conversion paths are intentionally left to a future conversion graph.
            var hasConversion = await dbContext.UomConversions.AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                !x.Disabled &&
                x.FromUomCode == channelUom &&
                x.ToUomCode == baseUom &&
                x.EffectiveFrom <= businessDate &&
                (x.EffectiveTo == null || x.EffectiveTo >= businessDate),
                cancellationToken);
            if (!hasConversion)
            {
                throw new KnownException($"SKU 渠道计量单位 '{channelUom}' 需要一条到基本计量单位 '{baseUom}' 的启用直接换算关系。");
            }
        }
    }
}

public sealed record CreateUnitOfMeasureCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string DimensionType,
    int Precision,
    string RoundingMode,
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateUnitOfMeasureCommandHandler(IUnitOfMeasureRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateUnitOfMeasureCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateUnitOfMeasureCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "unit-of-measure",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.DimensionType, request.Precision, request.RoundingMode),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("unit-of-measure", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"计量单位 '{code}' 已存在。");
        }

        var uom = UnitOfMeasure.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.DimensionType,
            request.Precision,
            request.RoundingMode);
        await repository.AddAsync(uom, cancellationToken);
        return new MasterDataResourceResult("unit-of-measure", uom.Code, uom.Name);
    }
}

public sealed record CreateUomConversionCommand(
    string OrganizationId,
    string EnvironmentId,
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateUomConversionCommandHandler(IUomConversionRepository repository, ApplicationDbContext dbContext)
    : ICommandHandler<CreateUomConversionCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateUomConversionCommand request, CancellationToken cancellationToken)
    {
        await UomConversionValidator.ValidateUnitsAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.FromUomCode,
            request.ToUomCode,
            requireActiveUnits: true,
            cancellationToken);

        if (await repository.ExistsAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.FromUomCode,
            request.ToUomCode,
            request.EffectiveFrom,
            cancellationToken))
        {
            throw new KnownException($"计量单位换算关系 '{request.FromUomCode}->{request.ToUomCode}' 已存在。");
        }

        var conversion = UomConversion.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.FromUomCode,
            request.ToUomCode,
            request.Factor,
            request.Offset,
            request.Precision,
            request.RoundingMode,
            request.EffectiveFrom,
            request.EffectiveTo);
        await repository.AddAsync(conversion, cancellationToken);
        return new MasterDataResourceResult("uom-conversion", $"{conversion.FromUomCode}->{conversion.ToUomCode}", $"{conversion.FromUomCode} to {conversion.ToUomCode}");
    }
}

public sealed record CreateBusinessPartnerCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string PartnerType,
    string Name,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? TaxId = null,
    string? IdempotencyKey = null,
    string? TaxRegionCode = null,
    string? DefaultCurrencyCode = null,
    string? PaymentTermsCode = null,
    string? PrimaryAddress = null,
    string? PrimaryContactName = null,
    string? PrimaryContactEmail = null,
    string? PrimaryContactPhone = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateBusinessPartnerCommandHandler(IBusinessPartnerRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateBusinessPartnerCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateBusinessPartnerCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "business-partner",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.PartnerType, request.Name, request.PartnerRoles ?? [], request.TaxId, request.CreditLimit, request.CreditCurrencyCode),
            cancellationToken,
            new Dictionary<string, string> { ["partnerType"] = request.PartnerType });
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("business-partner", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsCodeAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"业务伙伴 '{code}' 已存在。");
        }

        if (!string.IsNullOrWhiteSpace(request.TaxId) &&
            await repository.ExistsTaxIdAsync(request.OrganizationId, request.EnvironmentId, request.TaxId.Trim(), cancellationToken))
        {
            throw new KnownException($"业务伙伴税号 '{request.TaxId}' 已存在。");
        }

        var partner = BusinessPartner.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.PartnerType,
            request.Name,
            request.PartnerRoles,
            request.TaxId,
            request.TaxRegionCode,
            request.DefaultCurrencyCode,
            request.PaymentTermsCode,
            request.PrimaryAddress,
            request.PrimaryContactName,
            request.PrimaryContactEmail,
            request.PrimaryContactPhone,
            request.CreditLimit,
            request.CreditCurrencyCode);
        await repository.AddAsync(partner, cancellationToken);
        return new MasterDataResourceResult("business-partner", partner.Code, partner.Name);
    }
}

public sealed record CreateDepartmentCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? ParentDepartmentCode,
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateDepartmentCommandHandler(IDepartmentRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateDepartmentCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "department",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.ParentDepartmentCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("department", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"部门 '{code}' 已存在。");
        }

        var department = Department.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.ParentDepartmentCode);
        await repository.AddAsync(department, cancellationToken);
        return new MasterDataResourceResult("department", department.Code, department.Name);
    }
}

public sealed record CreateTeamCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string DepartmentCode,
    string ShiftCode,
    string? WorkshopCode = null,
    string? IdempotencyKey = null,
    MasterDataIntegrationEventContext? AuditContext = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateTeamCommandHandler(
    ITeamRepository repository,
    MasterDataCodingService? codingService = null,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateTeamCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateTeamCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "team",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.DepartmentCode, request.ShiftCode, request.WorkshopCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("team", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"班组 '{code}' 已存在。");
        }

        var team = Team.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.DepartmentCode,
            request.ShiftCode,
            request.WorkshopCode);
        await repository.AddAsync(team, cancellationToken);
        MasterDataScopeContextAudit.AddCreated(
            dbContext ?? throw new KnownException("创建班组需要范围审计存储。"),
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "team",
            team.Id.ToString(),
            team.Code,
            new
            {
                workshopCode = team.WorkshopCode,
                shiftCode = team.ShiftCode,
                disabled = team.Disabled,
            });
        return new MasterDataResourceResult("team", team.Code, team.Name);
    }
}

public sealed record AssignPersonnelSkillCommand(
    string OrganizationId,
    string EnvironmentId,
    string UserId,
    string SkillCode,
    string Level,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo) : ICommand<MasterDataResourceResult>;

public sealed class AssignPersonnelSkillCommandHandler : ICommandHandler<AssignPersonnelSkillCommand, MasterDataResourceResult>
{
    private readonly IPersonnelSkillRepository _repository;
    private readonly IReferenceDataCodeRepository? _referenceDataRepository;

    public AssignPersonnelSkillCommandHandler(IPersonnelSkillRepository repository)
    {
        _repository = repository;
    }

    public AssignPersonnelSkillCommandHandler(
        IPersonnelSkillRepository repository,
        IReferenceDataCodeRepository referenceDataRepository)
    {
        _repository = repository;
        _referenceDataRepository = referenceDataRepository;
    }

    public async Task<MasterDataResourceResult> Handle(AssignPersonnelSkillCommand request, CancellationToken cancellationToken)
    {
        await ValidateControlledReferenceDataAsync(request, cancellationToken);

        if (await _repository.ExistsAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.UserId,
            request.SkillCode,
            request.EffectiveFrom,
            cancellationToken))
        {
            throw new KnownException($"人员技能 '{request.UserId}:{request.SkillCode}' 已存在。");
        }

        var skill = PersonnelSkill.Assign(
            request.OrganizationId,
            request.EnvironmentId,
            request.UserId,
            request.SkillCode,
            request.Level,
            request.EffectiveFrom,
            request.EffectiveTo);
        await _repository.AddAsync(skill, cancellationToken);
        return new MasterDataResourceResult("personnel-skill", $"{skill.UserId}:{skill.SkillCode}", skill.Level);
    }

    private async Task ValidateControlledReferenceDataAsync(AssignPersonnelSkillCommand request, CancellationToken cancellationToken)
    {
        if (_referenceDataRepository is null)
        {
            return;
        }

        foreach (var reference in MasterDataDictionaryRules.GetPersonnelSkillReferences(request.SkillCode, request.Level))
        {
            await EnsureActiveReferenceDataAsync(
                request.OrganizationId,
                request.EnvironmentId,
                reference.CodeSet,
                reference.Code,
                reference.Field,
                cancellationToken);
        }
    }

    private async Task EnsureActiveReferenceDataAsync(
        string organizationId,
        string environmentId,
        string codeSet,
        string code,
        string field,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new KnownException($"人员技能字段 '{field}' 必须引用已启用的 '{codeSet}' 代码。");
        }

        var trimmedCode = code.Trim();
        var exists = await _referenceDataRepository!.ExistsActiveAsync(
            organizationId,
            environmentId,
            codeSet,
            trimmedCode,
            cancellationToken);
        if (!exists)
        {
            throw new KnownException($"人员技能字段 '{field}' 引用的参考数据 '{codeSet}:{trimmedCode}' 不存在或未启用。");
        }
    }
}

public sealed record CreateSiteCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string Timezone,
    string? IdempotencyKey = null,
    MasterDataIntegrationEventContext? AuditContext = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateSiteCommandHandler(
    ISiteRepository repository,
    MasterDataCodingService? codingService = null,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateSiteCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "site",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.Timezone),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("site", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"站点 '{code}' 已存在。");
        }

        var site = Site.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.Timezone);
        await repository.AddAsync(site, cancellationToken);
        MasterDataScopeContextAudit.AddCreated(
            dbContext ?? throw new KnownException("创建站点需要范围审计存储。"),
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "site",
            site.Id.ToString(),
            site.Code,
            new { timezone = site.Timezone, disabled = site.Disabled });
        return new MasterDataResourceResult("site", site.Code, site.Name);
    }
}

public sealed record CreateProductionLineCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? WorkshopCode = null,
    string? IdempotencyKey = null,
    MasterDataIntegrationEventContext? AuditContext = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateProductionLineCommandHandler(
    IProductionLineRepository repository,
    MasterDataCodingService? codingService = null,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateProductionLineCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateProductionLineCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "production-line",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.SiteCode, request.WorkshopCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("production-line", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"产线 '{code}' 已存在。");
        }

        var line = ProductionLine.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.SiteCode,
            request.WorkshopCode);
        await repository.AddAsync(line, cancellationToken);
        MasterDataScopeContextAudit.AddCreated(
            dbContext ?? throw new KnownException("创建产线需要范围审计存储。"),
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "production-line",
            line.Id.ToString(),
            line.Code,
            new
            {
                siteCode = line.SiteCode,
                workshopCode = line.WorkshopCode,
                disabled = line.Disabled,
            });
        return new MasterDataResourceResult("production-line", line.Code, line.Name);
    }
}

public sealed record CreateShiftCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int PaidMinutes,
    string? IdempotencyKey = null,
    int BreakMinutes = 0) : ICommand<MasterDataResourceResult>;

public sealed class CreateShiftCommandHandler(IShiftRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateShiftCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "shift",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.StartsAt, request.EndsAt, request.PaidMinutes, request.BreakMinutes),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("shift", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"班次 '{code}' 已存在。");
        }

        var shift = Shift.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.StartsAt,
            request.EndsAt,
            request.PaidMinutes,
            request.BreakMinutes);
        await repository.AddAsync(shift, cancellationToken);
        return new MasterDataResourceResult("shift", shift.Code, shift.Name);
    }
}

public sealed record CreateWorkCenterCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    int CapacityMinutesPerDay,
    string ResourceType,
    string PlantCode,
    string LineCode,
    string DefaultCalendarCode,
    string CapacityUnit,
    bool FiniteCapacity,
    string? WorkshopCode = null,
    string? IdempotencyKey = null,
    decimal UtilizationRate = 1m,
    decimal EfficiencyRate = 1m,
    int NumberOfCapacities = 1,
    string? CostCenterCode = null,
    bool Bottleneck = false,
    MasterDataIntegrationEventContext? AuditContext = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkCenterCommandHandler(
    IWorkCenterRepository repository,
    MasterDataCodingService? codingService = null,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateWorkCenterCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkCenterCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "work-center",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.CapacityMinutesPerDay, request.ResourceType, request.PlantCode, request.LineCode, request.DefaultCalendarCode, request.CapacityUnit, request.FiniteCapacity, request.WorkshopCode, request.UtilizationRate, request.EfficiencyRate, request.NumberOfCapacities, request.CostCenterCode, request.Bottleneck),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("work-center", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"工作中心 '{code}' 已存在。");
        }

        var workCenter = WorkCenter.CreateResource(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.CapacityMinutesPerDay,
            request.ResourceType,
            request.PlantCode,
            request.LineCode,
            request.WorkshopCode,
            request.DefaultCalendarCode,
            request.CapacityUnit,
            request.FiniteCapacity,
            request.UtilizationRate,
            request.EfficiencyRate,
            request.NumberOfCapacities,
            request.CostCenterCode,
            request.Bottleneck);
        await repository.AddAsync(workCenter, cancellationToken);
        MasterDataScopeContextAudit.AddCreated(
            dbContext ?? throw new KnownException("创建工作中心需要范围审计存储。"),
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "work-center",
            workCenter.Id.ToString(),
            workCenter.Code,
            new
            {
                plantCode = workCenter.PlantCode,
                lineCode = workCenter.LineCode,
                workshopCode = workCenter.WorkshopCode,
                disabled = workCenter.Disabled,
            });
        return new MasterDataResourceResult("work-center", workCenter.Code, workCenter.Name);
    }
}

public sealed record CreateWorkCalendarCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? IdempotencyKey = null,
    string Timezone = "UTC",
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? HolidayCalendarCode = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkCalendarCommandHandler(IWorkCalendarRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateWorkCalendarCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkCalendarCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "work-calendar",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.Timezone, request.EffectiveFrom, request.EffectiveTo, request.HolidayCalendarCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("work-calendar", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"工作日历 '{code}' 已存在。");
        }

        var calendar = WorkCalendar.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.Timezone,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.HolidayCalendarCode);
        await repository.AddAsync(calendar, cancellationToken);
        return new MasterDataResourceResult("work-calendar", calendar.Code, calendar.Name);
    }
}

public sealed record RegisterDeviceAssetCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Model,
    string LineCode,
    string WorkCenterCode,
    string AssetClassCode,
    string Manufacturer,
    string SerialNo,
    decimal? MinimumCapacity,
    decimal? MaximumCapacity,
    string CapacityUomCode,
    string Criticality,
    bool Maintainable,
    bool TelemetryEnabled,
    IReadOnlyDictionary<string, string> ExternalReferences,
    string? IdempotencyKey = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchaseCost = null,
    string? PurchaseCurrencyCode = null,
    DateOnly? WarrantyExpiresOn = null,
    string? SupplierPartnerCode = null,
    string? SiteCode = null,
    string? WorkshopCode = null,
    string? StationCode = null,
    string? ParentDeviceId = null,
    DateOnly? RetiredOn = null,
    IReadOnlyCollection<DeviceAssetComponentDraft>? Components = null) : ICommand<MasterDataResourceResult>;

public sealed class RegisterDeviceAssetCommandHandler
    : ICommandHandler<RegisterDeviceAssetCommand, MasterDataResourceResult>
{
    private readonly IDeviceAssetRepository repository;
    private readonly IDeviceAssetReferenceValidator? referenceValidator;
    private readonly IMasterDataReferenceScopeCoordinator? referenceScopeCoordinator;
    private readonly MasterDataCodingService? codingService;

    public RegisterDeviceAssetCommandHandler(
        IDeviceAssetRepository repository,
        MasterDataCodingService? codingService = null)
    {
        this.repository = repository;
        this.codingService = codingService;
    }

    public RegisterDeviceAssetCommandHandler(
        IDeviceAssetRepository repository,
        IDeviceAssetReferenceValidator referenceValidator,
        MasterDataCodingService? codingService = null)
    {
        this.repository = repository;
        this.referenceValidator = referenceValidator;
        this.codingService = codingService;
    }

    public RegisterDeviceAssetCommandHandler(
        IDeviceAssetRepository repository,
        IDeviceAssetReferenceValidator referenceValidator,
        IMasterDataReferenceScopeCoordinator referenceScopeCoordinator,
        MasterDataCodingService? codingService = null)
    {
        this.repository = repository;
        this.referenceValidator = referenceValidator;
        this.referenceScopeCoordinator = referenceScopeCoordinator;
        this.codingService = codingService;
    }

    public Task<MasterDataResourceResult> Handle(RegisterDeviceAssetCommand request, CancellationToken cancellationToken)
    {
        return referenceScopeCoordinator is null
            ? HandleCoreAsync(request, cancellationToken)
            : referenceScopeCoordinator.ExecuteAsync(
                request.OrganizationId,
                request.EnvironmentId,
                token => HandleCoreAsync(request, token),
                cancellationToken);
    }

    private async Task<MasterDataResourceResult> HandleCoreAsync(
        RegisterDeviceAssetCommand request,
        CancellationToken cancellationToken)
    {
        var purchaseCurrencyCode = DeviceAssetCommandValidator.NormalizeCurrencyCode(request.PurchaseCurrencyCode);
        DeviceAssetCommandValidator.EnsureValidComponents(request.Components);
        var references = await ValidateReferencesAsync(request, cancellationToken);

        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "device-asset",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(
                request.Model,
                request.LineCode,
                request.WorkCenterCode,
                request.AssetClassCode,
                request.Manufacturer,
                request.SerialNo,
                request.MinimumCapacity,
                request.MaximumCapacity,
                request.CapacityUomCode,
                request.Criticality,
                request.Maintainable,
                request.TelemetryEnabled,
                request.ExternalReferences.Select(x => $"{x.Key}:{x.Value}"),
                request.PurchaseDate,
                request.PurchaseCost,
                purchaseCurrencyCode,
                request.WarrantyExpiresOn,
                references.SupplierPartnerCode,
                request.SiteCode,
                request.WorkshopCode,
                request.StationCode,
                references.ParentDeviceId,
                request.RetiredOn,
                request.Components?.Select(x => $"{x.ComponentCode}:{x.Quantity}:{x.Critical}") ?? []),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("device-asset", allocation.Code, request.Model);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"设备资产 '{code}' 已存在。");
        }

        var asset = DeviceAsset.RegisterCapability(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Model,
            request.LineCode,
            request.WorkCenterCode,
            request.AssetClassCode,
            request.Manufacturer,
            request.SerialNo,
            request.MinimumCapacity,
            request.MaximumCapacity,
            request.CapacityUomCode,
            request.Criticality,
            request.Maintainable,
            request.TelemetryEnabled,
            request.ExternalReferences)
            .WithLedger(
                request.PurchaseDate,
                request.PurchaseCost,
                purchaseCurrencyCode,
                request.WarrantyExpiresOn,
                references.SupplierPartnerCode,
                request.SiteCode ?? string.Empty,
                request.WorkshopCode ?? string.Empty,
                request.LineCode,
                request.StationCode ?? string.Empty,
                references.ParentDeviceId,
                request.RetiredOn)
            .ReplaceComponents(request.Components ?? []);
        await repository.AddAsync(asset, cancellationToken);
        return new MasterDataResourceResult("device-asset", asset.Code, asset.Model);
    }

    private Task<DeviceAssetReferenceValidationResult> ValidateReferencesAsync(
        RegisterDeviceAssetCommand request,
        CancellationToken cancellationToken)
    {
        if (referenceValidator is not null)
        {
            return referenceValidator.ValidateForCreateAsync(
                request.OrganizationId,
                request.EnvironmentId,
                request.SupplierPartnerCode,
                request.ParentDeviceId,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.SupplierPartnerCode) ||
            !string.IsNullOrWhiteSpace(request.ParentDeviceId))
        {
            throw new KnownException("校验设备资产引用需要 MasterData 持久化上下文。");
        }

        return Task.FromResult(new DeviceAssetReferenceValidationResult(string.Empty, string.Empty));
    }
}

internal static class DeviceAssetCommandValidator
{
    public static string NormalizeCurrencyCode(string? value)
    {
        var code = value?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        if (code.Length != 3 || code.Any(x => !char.IsAsciiLetter(x)))
        {
            throw new KnownException("设备资产采购币种代码必须是 3 位 ISO 4217 字母代码。");
        }

        return code.ToUpperInvariant();
    }

    public static string NormalizeCurrencyCode(string? value, string fallback)
    {
        return value is null ? fallback : NormalizeCurrencyCode(value);
    }

    public static void EnsureValidComponents(IReadOnlyCollection<DeviceAssetComponentDraft>? components)
    {
        if (components is null)
        {
            return;
        }

        var invalid = components.FirstOrDefault(x => x.Quantity <= 0m);
        if (invalid is not null)
        {
            throw new KnownException($"设备资产组件 '{invalid.ComponentCode}' 的数量必须大于零。");
        }
    }
}

internal static class MasterDataCodeGenerator
{
    public static async Task<MasterDataCodeAllocation> AllocateAsync(
        MasterDataCodingService? codingService,
        string ruleKey,
        string organizationId,
        string environmentId,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var allocation = await (codingService ?? new MasterDataCodingService()).AllocateAsync(
            organizationId,
            environmentId,
            ruleKey,
            requestedCode,
            idempotencyKey,
            payloadFingerprint,
            cancellationToken,
            fields);
        return allocation;
    }
}

public sealed record CreateReferenceDataCodeCommand(
    string OrganizationId,
    string EnvironmentId,
    string CodeSet,
    string Code,
    string Name) : ICommand<MasterDataResourceResult>;

public sealed class CreateReferenceDataCodeCommandHandler(IReferenceDataCodeRepository repository)
    : ICommandHandler<CreateReferenceDataCodeCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateReferenceDataCodeCommand request, CancellationToken cancellationToken)
    {
        if (!MasterDataDictionaryRules.IsStandardCodeSet(request.CodeSet))
        {
            throw new KnownException($"参考数据代码集 '{request.CodeSet}' 未在主数据字典规则中登记。");
        }

        if (MasterDataDictionaryRules.IsSystemEnumCodeSet(request.CodeSet) &&
            !MasterDataDictionaryRules.IsSystemManagedReferenceData(request.CodeSet, request.Code))
        {
            throw new KnownException($"参考数据代码 '{request.CodeSet}:{request.Code}' 不允许加入系统枚举代码集。");
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.CodeSet, request.Code, cancellationToken))
        {
            throw new KnownException($"参考数据代码 '{request.CodeSet}:{request.Code}' 已存在。");
        }

        var code = ReferenceDataCode.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.CodeSet,
            request.Code,
            request.Name);
        await repository.AddAsync(code, cancellationToken);
        return new MasterDataResourceResult("reference-data-code", code.Code, code.Name);
    }
}
