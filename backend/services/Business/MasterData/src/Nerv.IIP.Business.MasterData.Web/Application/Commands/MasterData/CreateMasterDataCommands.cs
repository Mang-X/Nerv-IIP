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
                throw new KnownException($"SKU '{allocation.Code}' idempotency record exists but resource was not found.");
            }

            return new MasterDataResourceResult("sku", persisted.Code, persisted.Name);
        }

        if (await _repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, cancellationToken))
        {
            throw new KnownException($"SKU '{allocation.Code}' already exists.");
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
        if (_referenceDataRepository is null)
        {
            return;
        }

        foreach (var reference in MasterDataDictionaryRules.GetCreateSkuReferences(
            request.Category,
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
                throw new KnownException($"SKU field '{reference.Field}' must reference an active '{reference.CodeSet}' code.");
            }

            var exists = await _referenceDataRepository.ExistsActiveAsync(
                request.OrganizationId,
                request.EnvironmentId,
                reference.CodeSet,
                reference.Code.Trim(),
                cancellationToken);
            if (!exists)
            {
                throw new KnownException($"SKU field '{reference.Field}' references inactive or missing reference data '{reference.CodeSet}:{reference.Code}'.");
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
            throw new KnownException("SKU channel UOM validation requires master data persistence context.");
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
                throw new KnownException($"SKU channel UOM '{channelUom}' requires an active direct conversion to base UOM '{baseUom}'.");
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
            throw new KnownException($"Unit of measure '{code}' already exists.");
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
            throw new KnownException($"UOM conversion '{request.FromUomCode}->{request.ToUomCode}' already exists.");
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
            throw new KnownException($"Business partner '{code}' already exists.");
        }

        if (!string.IsNullOrWhiteSpace(request.TaxId) &&
            await repository.ExistsTaxIdAsync(request.OrganizationId, request.EnvironmentId, request.TaxId.Trim(), cancellationToken))
        {
            throw new KnownException($"Business partner tax id '{request.TaxId}' already exists.");
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
            throw new KnownException($"Department '{code}' already exists.");
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
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateTeamCommandHandler(ITeamRepository repository, MasterDataCodingService? codingService = null)
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
            MasterDataCodingService.Fingerprint(request.Name, request.DepartmentCode, request.ShiftCode),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("team", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"Team '{code}' already exists.");
        }

        var team = Team.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.DepartmentCode,
            request.ShiftCode);
        await repository.AddAsync(team, cancellationToken);
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
            throw new KnownException($"Personnel skill '{request.UserId}:{request.SkillCode}' already exists.");
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
            throw new KnownException($"Personnel skill field '{field}' must reference an active '{codeSet}' code.");
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
            throw new KnownException($"Personnel skill field '{field}' references inactive or missing reference data '{codeSet}:{trimmedCode}'.");
        }
    }
}

public sealed record CreateSiteCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string Timezone,
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateSiteCommandHandler(ISiteRepository repository, MasterDataCodingService? codingService = null)
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
            throw new KnownException($"Site '{code}' already exists.");
        }

        var site = Site.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.Timezone);
        await repository.AddAsync(site, cancellationToken);
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
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateProductionLineCommandHandler(IProductionLineRepository repository, MasterDataCodingService? codingService = null)
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
            throw new KnownException($"Production line '{code}' already exists.");
        }

        var line = ProductionLine.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.SiteCode,
            request.WorkshopCode);
        await repository.AddAsync(line, cancellationToken);
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
            throw new KnownException($"Shift '{code}' already exists.");
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
    bool Bottleneck = false) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkCenterCommandHandler(IWorkCenterRepository repository, MasterDataCodingService? codingService = null)
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
            throw new KnownException($"Work center '{code}' already exists.");
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
            throw new KnownException($"Work calendar '{code}' already exists.");
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
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class RegisterDeviceAssetCommandHandler(IDeviceAssetRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<RegisterDeviceAssetCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(RegisterDeviceAssetCommand request, CancellationToken cancellationToken)
    {
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
                request.ExternalReferences.Select(x => $"{x.Key}:{x.Value}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("device-asset", allocation.Code, request.Model);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"Device asset '{code}' already exists.");
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
            request.ExternalReferences);
        await repository.AddAsync(asset, cancellationToken);
        return new MasterDataResourceResult("device-asset", asset.Code, asset.Model);
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
            throw new KnownException($"Reference data code set '{request.CodeSet}' is not reserved by the master-data dictionary rules.");
        }

        if (MasterDataDictionaryRules.IsSystemEnumCodeSet(request.CodeSet) &&
            !MasterDataDictionaryRules.IsSystemManagedReferenceData(request.CodeSet, request.Code))
        {
            throw new KnownException($"Reference data code '{request.CodeSet}:{request.Code}' is not allowed in a system enum reference data code set.");
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.CodeSet, request.Code, cancellationToken))
        {
            throw new KnownException($"Reference data code '{request.CodeSet}:{request.Code}' already exists.");
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
