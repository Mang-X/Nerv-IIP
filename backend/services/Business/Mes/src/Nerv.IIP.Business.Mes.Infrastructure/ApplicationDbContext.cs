using MediatR;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    private const string ProductionReportReversalUniqueIndexName = "ux_production_reports_scope_reversed_report_no";
    private const string QualityHoldTransitionIdempotencyIndexName = "ux_quality_hold_transitions_scope_idempotency_kind";

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<MesEngineeringChangeWorkOrderImpact> EngineeringChangeWorkOrderImpacts => Set<MesEngineeringChangeWorkOrderImpact>();

    public DbSet<OperationTask> OperationTasks => Set<OperationTask>();

    public DbSet<ProductionReport> ProductionReports => Set<ProductionReport>();

    public DbSet<TelemetryProductionReportCandidate> TelemetryProductionReportCandidates => Set<TelemetryProductionReportCandidate>();
    public DbSet<TelemetryProductionReportCandidateTransition> TelemetryProductionReportCandidateTransitions => Set<TelemetryProductionReportCandidateTransition>();

    public DbSet<ProductionReportMaterialConsumption> ProductionReportMaterialConsumptions => Set<ProductionReportMaterialConsumption>();

    public DbSet<OutputLotGenealogy> OutputLotGenealogies => Set<OutputLotGenealogy>();

    public DbSet<DefectRecord> DefectRecords => Set<DefectRecord>();

    public DbSet<QualityHoldContext> QualityHoldContexts => Set<QualityHoldContext>();
    public DbSet<QualityHoldTransition> QualityHoldTransitions => Set<QualityHoldTransition>();

    public DbSet<MaterialRequirement> MaterialRequirements => Set<MaterialRequirement>();

    public DbSet<MaterialIssueRequest> MaterialIssueRequests => Set<MaterialIssueRequest>();

    public DbSet<ScheduleResult> ScheduleResults => Set<ScheduleResult>();

    public DbSet<WorkCenterUnavailability> WorkCenterUnavailabilities => Set<WorkCenterUnavailability>();

    public DbSet<DeviceAssetWorkCenterMapping> DeviceAssetWorkCenterMappings => Set<DeviceAssetWorkCenterMapping>();

    public DbSet<FinishedGoodsReceiptRequest> FinishedGoodsReceiptRequests => Set<FinishedGoodsReceiptRequest>();

    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();

    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();

    public DbSet<CodeIdempotencyKey> CodeIdempotencyKeys => Set<CodeIdempotencyKey>();

    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    public DbSet<ScheduleReleaseWatermark> ScheduleReleaseWatermarks => Set<ScheduleReleaseWatermark>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder is null)
        {
            throw new ArgumentNullException(nameof(modelBuilder));
        }

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(MesFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ConfigureIntegrationEventDeadLetters();
        ConfigureCapStorage(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ConfigureStronglyTypedIdValueConverter(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<ProcessedIntegrationEvent>(
                this,
                token => base.SaveChangesAsync(acceptAllChangesOnSuccess, token),
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateQualityHoldTransition(exception))
        {
            return await RecoverQualityHoldTransitionReplayAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateProductionReportReversal(exception))
        {
            ChangeTracker.Clear();
            throw DuplicateProductionReportReversal(exception);
        }
    }

    private bool IsDuplicateQualityHoldTransition(DbUpdateException exception)
    {
        return ProcessedIntegrationEventInbox.IsUniqueConflict(exception, this, QualityHoldTransitionIdempotencyIndexName) ||
            ((Database.ProviderName ?? string.Empty).Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
             EnumerateExceptions(exception).Any(inner =>
                 inner.Message.Contains("quality_hold_transitions.idempotency_key", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<int> RecoverQualityHoldTransitionReplayAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        var pending = ChangeTracker.Entries<QualityHoldTransition>()
            .Where(x => x.State == EntityState.Added && !string.IsNullOrWhiteSpace(x.Entity.IdempotencyKey))
            .Select(x => x.Entity)
            .Single();
        var existing = await QualityHoldTransitions.AsNoTracking().SingleAsync(x =>
            x.OrganizationId == pending.OrganizationId &&
            x.EnvironmentId == pending.EnvironmentId &&
            x.SourceService == pending.SourceService &&
            x.SourceDocumentId == pending.SourceDocumentId &&
            x.HoldCycleId == pending.HoldCycleId &&
            x.IdempotencyKey == pending.IdempotencyKey &&
            x.EventKind == pending.EventKind,
            cancellationToken);

        if (!QualityHoldTransitionPayloadEquals(existing, pending))
        {
            throw new KnownException("Quality hold transition idempotency key was reused with a different payload.");
        }

        Entry(pending).State = EntityState.Detached;
        foreach (var staleProjection in ChangeTracker.Entries<QualityHoldContext>()
            .Where(entry => entry.State == EntityState.Modified &&
                entry.Entity.OrganizationId == pending.OrganizationId &&
                entry.Entity.EnvironmentId == pending.EnvironmentId &&
                entry.Entity.SourceService == pending.SourceService &&
                entry.Entity.SourceDocumentId == pending.SourceDocumentId)
            .ToList())
        {
            staleProjection.State = EntityState.Detached;
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static bool QualityHoldTransitionPayloadEquals(QualityHoldTransition left, QualityHoldTransition right)
    {
        return left.CorrelationId == right.CorrelationId &&
            left.Actor == right.Actor &&
            left.OccurredAtUtc == right.OccurredAtUtc &&
            left.Reason == right.Reason &&
            left.SourceInspectionRecordId == right.SourceInspectionRecordId &&
            left.SourceInspectionDocumentId == right.SourceInspectionDocumentId &&
            left.Origin == right.Origin;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicate<ProcessedIntegrationEvent>(
                this,
                () => base.SaveChanges(acceptAllChangesOnSuccess));
        }
        catch (DbUpdateException exception) when (IsDuplicateProductionReportReversal(exception))
        {
            ChangeTracker.Clear();
            throw DuplicateProductionReportReversal(exception);
        }
    }

    private bool IsDuplicateProductionReportReversal(DbUpdateException exception)
    {
        return ProcessedIntegrationEventInbox.IsUniqueConflict(exception, this, ProductionReportReversalUniqueIndexName) ||
            IsSqliteDuplicateProductionReportReversal(exception);
    }

    private static KnownException DuplicateProductionReportReversal(DbUpdateException exception)
    {
        return new KnownException("原报工已冲销，不能重复冲销。", exception);
    }

    private bool IsSqliteDuplicateProductionReportReversal(Exception exception)
    {
        var providerName = Database.ProviderName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return EnumerateExceptions(exception).Any(inner =>
            inner.Message.Contains("production_reports.organization_id", StringComparison.OrdinalIgnoreCase) &&
            inner.Message.Contains("production_reports.environment_id", StringComparison.OrdinalIgnoreCase) &&
            inner.Message.Contains("production_reports.reversed_report_no", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }
}
