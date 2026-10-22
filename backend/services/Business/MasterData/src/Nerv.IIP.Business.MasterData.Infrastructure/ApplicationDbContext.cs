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
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ScopeContextAuditAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.StationAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
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
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<PersonnelSkill> PersonnelSkills => Set<PersonnelSkill>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UomConversion> UomConversions => Set<UomConversion>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Workshop> Workshops => Set<Workshop>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ReferenceDataCode> ReferenceDataCodes => Set<ReferenceDataCode>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<WorkCalendar> WorkCalendars => Set<WorkCalendar>();
    public DbSet<DeviceAsset> DeviceAssets => Set<DeviceAsset>();
    public DbSet<ToolingAsset> ToolingAssets => Set<ToolingAsset>();
    public DbSet<ToolingAuditEntry> ToolingAuditEntries => Set<ToolingAuditEntry>();
    public DbSet<ChangeoverMatrixEntry> ChangeoverMatrixEntries => Set<ChangeoverMatrixEntry>();
    public DbSet<CodeRule> CodeRules => Set<CodeRule>();
    public DbSet<CodeRuleVersion> CodeRuleVersions => Set<CodeRuleVersion>();
    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
    public DbSet<CodeIdempotencyKey> CodeIdempotencyKeys => Set<CodeIdempotencyKey>();
    public DbSet<MasterDataLifecycleAuditEntry> LifecycleAuditEntries => Set<MasterDataLifecycleAuditEntry>();
    public DbSet<MasterDataScopeContextAuditEntry> ScopeContextAuditEntries => Set<MasterDataScopeContextAuditEntry>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder is null)
        {
            throw new ArgumentNullException(nameof(modelBuilder));
        }

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(MasterDataFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ConfigureCodingEntities();
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
        EnsureToolingAuditIsAppendOnly();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateLifecycleOperation(exception))
        {
            return await RecoverLifecycleOperationReplayAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsWorkerUserIdConflict(exception))
        {
            throw WorkerUserIdConflict(exception);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureToolingAuditIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void EnsureToolingAuditIsAppendOnly()
    {
        if (ChangeTracker.Entries<ToolingAuditEntry>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("工装审计事实只允许追加，禁止修改或删除。");
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
            throw new KnownException($"生命周期操作 '{pending.OperationId}' 与此前持久化的请求内容冲突。");
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

    // 员工 ↔ 登录账号一对一（#3924）：(organization_id, environment_id, user_id) 唯一索引是权威把关。
    // 命令里的预检挡住顺序提交；并发提交撞上唯一索引时在这里转成中文业务错误，不再以 500 漏出。
    internal const string WorkerUserIdIndexName = "IX_workers_organization_id_environment_id_user_id";

    private static readonly string[] WorkerUserIdSqliteColumns = ["workers.OrganizationId", "workers.EnvironmentId", "workers.UserId"];
    private static readonly string[] WorkerUserIdSqliteSnakeColumns = ["workers.organization_id", "workers.environment_id", "workers.user_id"];

    private bool IsWorkerUserIdConflict(DbUpdateException exception)
    {
        if (!ChangeTracker.Entries<Worker>().Any(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (IsPostgreSqlUniqueViolation(current, WorkerUserIdIndexName) || IsSqliteUniqueViolation(current))
            {
                return true;
            }
        }

        return false;
    }

    private KnownException WorkerUserIdConflict(DbUpdateException exception)
    {
        ChangeTracker.Clear();
        // 与 CreateWorkerCommand 预检同文案；不回显账号 ID。
        return new KnownException("所选登录账号已关联其他员工，一个账号只能关联一名员工。", exception);
    }

    private static bool IsPostgreSqlUniqueViolation(Exception exception, string indexName)
    {
        if (!string.Equals(exception.GetType().FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
        {
            return false;
        }

        var sqlState = exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string;
        var constraintName = exception.GetType().GetProperty("ConstraintName")?.GetValue(exception) as string;
        return sqlState == "23505" && string.Equals(constraintName, indexName, StringComparison.Ordinal);
    }

    private bool IsSqliteUniqueViolation(Exception exception)
    {
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            && !(Database.ProviderName ?? string.Empty).Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string marker = "UNIQUE constraint failed:";
        var markerIndex = exception.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var columns = exception.Message[(markerIndex + marker.Length)..]
            .Trim()
            .Trim('\'', '.')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return Matches(columns, WorkerUserIdSqliteColumns) || Matches(columns, WorkerUserIdSqliteSnakeColumns);

        static bool Matches(string[] actual, string[] expected) =>
            actual.Length == expected.Length
            && expected.All(column => actual.Contains(column, StringComparer.OrdinalIgnoreCase));
    }
}
