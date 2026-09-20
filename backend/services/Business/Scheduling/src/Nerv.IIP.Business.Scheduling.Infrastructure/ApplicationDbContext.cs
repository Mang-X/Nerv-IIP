using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Scheduling.Infrastructure.Urgency;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;

namespace Nerv.IIP.Business.Scheduling.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator)
    , IPostgreSqlCapDataStorage
{
    public DbSet<ScheduleProblemSnapshot> ScheduleProblems => Set<ScheduleProblemSnapshot>();
    public DbSet<SchedulePlan> SchedulePlans => Set<SchedulePlan>();
    public DbSet<SchedulePlanInvalidation> SchedulePlanInvalidations => Set<SchedulePlanInvalidation>();
    public DbSet<ScheduleOperationOverride> ScheduleOperationOverrides => Set<ScheduleOperationOverride>();
    public DbSet<OrderUrgencyBusinessPriority> OrderUrgencyBusinessPriorities => Set<OrderUrgencyBusinessPriority>();
    public DbSet<OrderUrgencyBusinessPriorityChange> OrderUrgencyBusinessPriorityChanges => Set<OrderUrgencyBusinessPriorityChange>();
    public DbSet<OrderUrgencySnapshot> OrderUrgencySnapshots => Set<OrderUrgencySnapshot>();
    public DbSet<OrderUrgencyArchiveBatch> OrderUrgencyArchiveBatches => Set<OrderUrgencyArchiveBatch>();
    public DbSet<OrderUrgencyArchiveBatchSnapshot> OrderUrgencyArchiveBatchSnapshots => Set<OrderUrgencyArchiveBatchSnapshot>();
    public DbSet<OrderUrgencyRetentionLease> OrderUrgencyRetentionLeases => Set<OrderUrgencyRetentionLease>();
    public DbSet<OrderUrgencyRestoreAudit> OrderUrgencyRestoreAudits => Set<OrderUrgencyRestoreAudit>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchedulingFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ConfigureIntegrationEventDeadLetters();
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

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        return SaveInboxChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try { return base.SaveChanges(acceptAllChangesOnSuccess); }
        catch (DbUpdateException exception) when (IsInboxIdentityConflict(exception))
        {
            ChangeTracker.Clear();
            return 0;
        }
    }

    private async Task<int> SaveInboxChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateException exception) when (IsInboxIdentityConflict(exception))
        {
            ChangeTracker.Clear();
            return 0;
        }
    }

    private bool IsInboxIdentityConflict(DbUpdateException exception) =>
        ChangeTracker.Entries<ProcessedIntegrationEvent>().Any(x => x.State == EntityState.Added) &&
        (ProcessedIntegrationEventInbox.IsUniqueConflict(exception, this, "ux_processed_integration_events_consumer_idempotency_key") ||
         ProcessedIntegrationEventInbox.IsUniqueConflict(exception, this, "ux_processed_integration_events_consumer_event_id"));
}
