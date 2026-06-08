using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public static class MasterDataPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddMasterDataPostgreSqlPersistence(
        this IServiceCollection services,
        string? connectionString,
        bool enableSensitiveDataLogging = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("PostgreSQL persistence requires a connection string.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataFacts.Schema));

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            options.EnableDetailedErrors();
        });
        services.AddRepositories(typeof(ApplicationDbContext).Assembly);
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<ISkuRepository, SkuRepository>();
        services.AddScoped<IUnitOfMeasureRepository, UnitOfMeasureRepository>();
        services.AddScoped<IUomConversionRepository, UomConversionRepository>();
        services.AddScoped<IBusinessPartnerRepository, BusinessPartnerRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITeamMemberRepository, TeamMemberRepository>();
        services.AddScoped<IPersonnelSkillRepository, PersonnelSkillRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IWorkshopRepository, WorkshopRepository>();
        services.AddScoped<IProductionLineRepository, ProductionLineRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IWorkCenterRepository, WorkCenterRepository>();
        services.AddScoped<IWorkCalendarRepository, WorkCalendarRepository>();
        services.AddScoped<IDeviceAssetRepository, DeviceAssetRepository>();
        services.AddScoped<IReferenceDataCodeRepository, ReferenceDataCodeRepository>();
        return services;
    }
}
