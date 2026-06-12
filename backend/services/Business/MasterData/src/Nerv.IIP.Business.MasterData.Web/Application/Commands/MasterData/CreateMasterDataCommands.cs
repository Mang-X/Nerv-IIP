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
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateSkuCommandHandler : ICommandHandler<CreateSkuCommand, MasterDataResourceResult>
{
    private readonly ISkuRepository _repository;
    private readonly IReferenceDataCodeRepository? _referenceDataRepository;
    private readonly MasterDataNumberingService _numberingService;

    public CreateSkuCommandHandler(ISkuRepository repository, MasterDataNumberingService? numberingService = null)
    {
        _repository = repository;
        _referenceDataRepository = null;
        _numberingService = numberingService ?? new MasterDataNumberingService();
    }

    public CreateSkuCommandHandler(
        ISkuRepository repository,
        IReferenceDataCodeRepository referenceDataRepository,
        MasterDataNumberingService? numberingService = null)
    {
        _repository = repository;
        _referenceDataRepository = referenceDataRepository;
        _numberingService = numberingService ?? new MasterDataNumberingService();
    }

    public async Task<MasterDataResourceResult> Handle(CreateSkuCommand request, CancellationToken cancellationToken)
    {
        await ValidateControlledReferenceDataAsync(request, cancellationToken);

        var allocation = await _numberingService.AllocateSkuCodeAsync(
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
            request.ComplianceTags);
        await _repository.AddAsync(sku, cancellationToken);
        return new MasterDataResourceResult("sku", sku.Code, sku.Name);
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
            string.Join(',', request.ComplianceTags.Order(StringComparer.Ordinal)));
    }
}

public sealed record CreateUnitOfMeasureCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string DimensionType,
    int Precision,
    string RoundingMode) : ICommand<MasterDataResourceResult>;

public sealed class CreateUnitOfMeasureCommandHandler(IUnitOfMeasureRepository repository)
    : ICommandHandler<CreateUnitOfMeasureCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateUnitOfMeasureCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Unit of measure '{request.Code}' already exists.");
        }

        var uom = UnitOfMeasure.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
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
    DateOnly EffectiveFrom) : ICommand<MasterDataResourceResult>;

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
            request.EffectiveFrom);
        await repository.AddAsync(conversion, cancellationToken);
        return new MasterDataResourceResult("uom-conversion", $"{conversion.FromUomCode}->{conversion.ToUomCode}", $"{conversion.FromUomCode} to {conversion.ToUomCode}");
    }
}

public sealed record CreateBusinessPartnerCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string PartnerType,
    string Name,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? TaxId = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateBusinessPartnerCommandHandler(IBusinessPartnerRepository repository)
    : ICommandHandler<CreateBusinessPartnerCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateBusinessPartnerCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsCodeAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Business partner '{request.Code}' already exists.");
        }

        if (!string.IsNullOrWhiteSpace(request.TaxId) &&
            await repository.ExistsTaxIdAsync(request.OrganizationId, request.EnvironmentId, request.TaxId.Trim(), cancellationToken))
        {
            throw new KnownException($"Business partner tax id '{request.TaxId}' already exists.");
        }

        var partner = BusinessPartner.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.PartnerType,
            request.Name,
            request.PartnerRoles,
            request.TaxId);
        await repository.AddAsync(partner, cancellationToken);
        return new MasterDataResourceResult("business-partner", partner.Code, partner.Name);
    }
}

public sealed record CreateDepartmentCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string? ParentDepartmentCode) : ICommand<MasterDataResourceResult>;

public sealed class CreateDepartmentCommandHandler(IDepartmentRepository repository)
    : ICommandHandler<CreateDepartmentCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Department '{request.Code}' already exists.");
        }

        var department = Department.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.Name,
            request.ParentDepartmentCode);
        await repository.AddAsync(department, cancellationToken);
        return new MasterDataResourceResult("department", department.Code, department.Name);
    }
}

public sealed record CreateTeamCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string DepartmentCode,
    string ShiftCode) : ICommand<MasterDataResourceResult>;

public sealed class CreateTeamCommandHandler(ITeamRepository repository)
    : ICommandHandler<CreateTeamCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateTeamCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Team '{request.Code}' already exists.");
        }

        var team = Team.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
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

        await EnsureActiveReferenceDataAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "skill",
            request.SkillCode,
            "SkillCode",
            cancellationToken);
        await EnsureActiveReferenceDataAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "skill-level",
            request.Level,
            "Level",
            cancellationToken);
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
    string Code,
    string Name,
    string Timezone) : ICommand<MasterDataResourceResult>;

public sealed class CreateSiteCommandHandler(ISiteRepository repository)
    : ICommandHandler<CreateSiteCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Site '{request.Code}' already exists.");
        }

        var site = Site.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.Name,
            request.Timezone);
        await repository.AddAsync(site, cancellationToken);
        return new MasterDataResourceResult("site", site.Code, site.Name);
    }
}

public sealed record CreateProductionLineCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string SiteCode,
    string? WorkshopCode = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateProductionLineCommandHandler(IProductionLineRepository repository)
    : ICommandHandler<CreateProductionLineCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateProductionLineCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Production line '{request.Code}' already exists.");
        }

        var line = ProductionLine.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
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
    string Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int PaidMinutes) : ICommand<MasterDataResourceResult>;

public sealed class CreateShiftCommandHandler(IShiftRepository repository)
    : ICommandHandler<CreateShiftCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Shift '{request.Code}' already exists.");
        }

        var shift = Shift.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.Name,
            request.StartsAt,
            request.EndsAt,
            request.PaidMinutes);
        await repository.AddAsync(shift, cancellationToken);
        return new MasterDataResourceResult("shift", shift.Code, shift.Name);
    }
}

public sealed record CreateWorkCenterCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    int CapacityMinutesPerDay,
    string ResourceType,
    string PlantCode,
    string LineCode,
    string DefaultCalendarCode,
    string CapacityUnit,
    bool FiniteCapacity,
    string? WorkshopCode = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkCenterCommandHandler(IWorkCenterRepository repository)
    : ICommandHandler<CreateWorkCenterCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkCenterCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Work center '{request.Code}' already exists.");
        }

        var workCenter = WorkCenter.CreateResource(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.Name,
            request.CapacityMinutesPerDay,
            request.ResourceType,
            request.PlantCode,
            request.LineCode,
            request.WorkshopCode,
            request.DefaultCalendarCode,
            request.CapacityUnit,
            request.FiniteCapacity);
        await repository.AddAsync(workCenter, cancellationToken);
        return new MasterDataResourceResult("work-center", workCenter.Code, workCenter.Name);
    }
}

public sealed record CreateWorkCalendarCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkCalendarCommandHandler(IWorkCalendarRepository repository)
    : ICommandHandler<CreateWorkCalendarCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkCalendarCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Work calendar '{request.Code}' already exists.");
        }

        var calendar = WorkCalendar.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.Name);
        await repository.AddAsync(calendar, cancellationToken);
        return new MasterDataResourceResult("work-calendar", calendar.Code, calendar.Name);
    }
}

public sealed record RegisterDeviceAssetCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
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
    IReadOnlyDictionary<string, string> ExternalReferences) : ICommand<MasterDataResourceResult>;

public sealed class RegisterDeviceAssetCommandHandler(IDeviceAssetRepository repository)
    : ICommandHandler<RegisterDeviceAssetCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(RegisterDeviceAssetCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken))
        {
            throw new KnownException($"Device asset '{request.Code}' already exists.");
        }

        var asset = DeviceAsset.RegisterCapability(
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
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
