using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Coding;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator)
    , IPostgreSqlCapDataStorage
{
    private const string LifecycleAuditOperationIndexName = "ux_master_data_lifecycle_audit_operation";
    public DbSet<Sku> Skus => Set<Sku>();
    public DbSet<BusinessPartner> BusinessPartners => Set<BusinessPartner>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<PersonnelSkill> PersonnelSkills => Set<PersonnelSkill>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UomConversion> UomConversions => Set<UomConversion>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Workshop> Workshops => Set<Workshop>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ReferenceDataCode> ReferenceDataCodes => Set<ReferenceDataCode>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<WorkCalendar> WorkCalendars => Set<WorkCalendar>();
    public DbSet<DeviceAsset> DeviceAssets => Set<DeviceAsset>();
    public DbSet<ToolingAsset> ToolingAssets => Set<ToolingAsset>();
    public DbSet<ChangeoverMatrixEntry> ChangeoverMatrixEntries => Set<ChangeoverMatrixEntry>();
    public DbSet<CodeRule> CodeRules => Set<CodeRule>();
    public DbSet<CodeRuleVersion> CodeRuleVersions => Set<CodeRuleVersion>();
    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
    public DbSet<CodeIdempotencyKey> CodeIdempotencyKeys => Set<CodeIdempotencyKey>();
    public DbSet<MasterDataLifecycleAuditEntry> LifecycleAuditEntries => Set<MasterDataLifecycleAuditEntry>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder is null)
        {
            throw new ArgumentNullException(nameof(modelBuilder));
        }

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(MasterDataFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ConfigureCapStorage(modelBuilder);
    }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ConfigureStronglyTypedIdValueConverter(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateLifecycleOperation(exception))
        {
            return await RecoverLifecycleOperationReplayAsync(cancellationToken);
        }
    }

    private bool IsDuplicateLifecycleOperation(Exception exception)
    {
        var provider = Database.ProviderName ?? string.Empty;
        return exception.ToString().Contains(LifecycleAuditOperationIndexName, StringComparison.OrdinalIgnoreCase) ||
            (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
             exception.ToString().Contains("master_data_lifecycle_audit.OrganizationId", StringComparison.OrdinalIgnoreCase) &&
             exception.ToString().Contains("master_data_lifecycle_audit.OperationId", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> RecoverLifecycleOperationReplayAsync(CancellationToken cancellationToken)
    {
        var pending = ChangeTracker.Entries<MasterDataLifecycleAuditEntry>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Single();
        var existing = await LifecycleAuditEntries.AsNoTracking().SingleAsync(x =>
            x.OrganizationId == pending.OrganizationId &&
            x.EnvironmentId == pending.EnvironmentId &&
            x.OperationId == pending.OperationId, cancellationToken);
        if (!LifecycleAuditPayloadEquals(existing, pending))
        {
            ChangeTracker.Clear();
            throw new KnownException($"Lifecycle operation '{pending.OperationId}' conflicts with its previously persisted payload.");
        }

        // The failed transaction included the resource mutation and any outbox rows. The winner already
        // persisted the canonical operation, so discarding the complete loser graph is the idempotent result.
        ChangeTracker.Clear();
        return 0;
    }

    private static bool LifecycleAuditPayloadEquals(MasterDataLifecycleAuditEntry left, MasterDataLifecycleAuditEntry right) =>
        left.ResourceType == right.ResourceType &&
        left.ResourceId == right.ResourceId &&
        left.ResourceCode == right.ResourceCode &&
        left.ResourceIdentity == right.ResourceIdentity &&
        left.TargetEnabled == right.TargetEnabled &&
        left.ActorId == right.ActorId &&
        left.Reason == right.Reason;
}
