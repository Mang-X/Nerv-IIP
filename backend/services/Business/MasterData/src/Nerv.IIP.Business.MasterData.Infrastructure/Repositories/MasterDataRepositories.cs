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
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Infrastructure.Repositories;

public interface ISkuRepository : IRepository<Sku, SkuId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);

    Task<Sku?> FindByBusinessKeyAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class SkuRepository(ApplicationDbContext context)
    : RepositoryBase<Sku, SkuId, ApplicationDbContext>(context), ISkuRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.Skus.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }

    public async Task<Sku?> FindByBusinessKeyAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return DbContext.Skus.Local.FirstOrDefault(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == code)
            ?? await DbContext.Skus.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == code,
                cancellationToken);
    }
}

public interface IUnitOfMeasureRepository : IRepository<UnitOfMeasure, UnitOfMeasureId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class UnitOfMeasureRepository(ApplicationDbContext context)
    : RepositoryBase<UnitOfMeasure, UnitOfMeasureId, ApplicationDbContext>(context), IUnitOfMeasureRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.UnitsOfMeasure.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IUomConversionRepository : IRepository<UomConversion, UomConversionId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string fromUomCode, string toUomCode, DateOnly effectiveFrom, CancellationToken cancellationToken = default);
}

public sealed class UomConversionRepository(ApplicationDbContext context)
    : RepositoryBase<UomConversion, UomConversionId, ApplicationDbContext>(context), IUomConversionRepository
{
    public async Task<bool> ExistsAsync(
        string organizationId,
        string environmentId,
        string fromUomCode,
        string toUomCode,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.UomConversions.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.FromUomCode == fromUomCode &&
            x.ToUomCode == toUomCode &&
            x.EffectiveFrom == effectiveFrom,
            cancellationToken);
    }
}

public interface IBusinessPartnerRepository : IRepository<BusinessPartner, BusinessPartnerId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string partnerType, string code, CancellationToken cancellationToken = default);
}

public sealed class BusinessPartnerRepository(ApplicationDbContext context)
    : RepositoryBase<BusinessPartner, BusinessPartnerId, ApplicationDbContext>(context), IBusinessPartnerRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string partnerType, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.BusinessPartners.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.PartnerType == partnerType &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IDepartmentRepository : IRepository<Department, DepartmentId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class DepartmentRepository(ApplicationDbContext context)
    : RepositoryBase<Department, DepartmentId, ApplicationDbContext>(context), IDepartmentRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.Departments.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface ITeamRepository : IRepository<Team, TeamId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class TeamRepository(ApplicationDbContext context)
    : RepositoryBase<Team, TeamId, ApplicationDbContext>(context), ITeamRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.Teams.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IPersonnelSkillRepository : IRepository<PersonnelSkill, PersonnelSkillId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string userId, string skillCode, DateOnly effectiveFrom, CancellationToken cancellationToken = default);
}

public sealed class PersonnelSkillRepository(ApplicationDbContext context)
    : RepositoryBase<PersonnelSkill, PersonnelSkillId, ApplicationDbContext>(context), IPersonnelSkillRepository
{
    public async Task<bool> ExistsAsync(
        string organizationId,
        string environmentId,
        string userId,
        string skillCode,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.PersonnelSkills.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.UserId == userId &&
            x.SkillCode == skillCode &&
            x.EffectiveFrom == effectiveFrom,
            cancellationToken);
    }
}

public interface ISiteRepository : IRepository<Site, SiteId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class SiteRepository(ApplicationDbContext context)
    : RepositoryBase<Site, SiteId, ApplicationDbContext>(context), ISiteRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.Sites.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IProductionLineRepository : IRepository<ProductionLine, ProductionLineId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class ProductionLineRepository(ApplicationDbContext context)
    : RepositoryBase<ProductionLine, ProductionLineId, ApplicationDbContext>(context), IProductionLineRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.ProductionLines.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IShiftRepository : IRepository<Shift, ShiftId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class ShiftRepository(ApplicationDbContext context)
    : RepositoryBase<Shift, ShiftId, ApplicationDbContext>(context), IShiftRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.Shifts.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IWorkCenterRepository : IRepository<WorkCenter, WorkCenterId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class WorkCenterRepository(ApplicationDbContext context)
    : RepositoryBase<WorkCenter, WorkCenterId, ApplicationDbContext>(context), IWorkCenterRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.WorkCenters.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IWorkCalendarRepository : IRepository<WorkCalendar, WorkCalendarId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class WorkCalendarRepository(ApplicationDbContext context)
    : RepositoryBase<WorkCalendar, WorkCalendarId, ApplicationDbContext>(context), IWorkCalendarRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.WorkCalendars.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IDeviceAssetRepository : IRepository<DeviceAsset, DeviceAssetId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default);
}

public sealed class DeviceAssetRepository(ApplicationDbContext context)
    : RepositoryBase<DeviceAsset, DeviceAssetId, ApplicationDbContext>(context), IDeviceAssetRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.DeviceAssets.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.Code == code,
            cancellationToken);
    }
}

public interface IReferenceDataCodeRepository : IRepository<ReferenceDataCode, ReferenceDataCodeId>
{
    Task<bool> ExistsAsync(string organizationId, string environmentId, string codeSet, string code, CancellationToken cancellationToken = default);
}

public sealed class ReferenceDataCodeRepository(ApplicationDbContext context)
    : RepositoryBase<ReferenceDataCode, ReferenceDataCodeId, ApplicationDbContext>(context), IReferenceDataCodeRepository
{
    public async Task<bool> ExistsAsync(string organizationId, string environmentId, string codeSet, string code, CancellationToken cancellationToken = default)
    {
        return await DbContext.ReferenceDataCodes.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.CodeSet == codeSet &&
            x.Code == code,
            cancellationToken);
    }
}
