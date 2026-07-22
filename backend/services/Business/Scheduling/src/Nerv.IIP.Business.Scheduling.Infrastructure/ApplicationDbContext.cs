using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure.IntegrationEvents;
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
        return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<ProcessedIntegrationEvent>(
            this,
            token => base.SaveChangesAsync(acceptAllChangesOnSuccess, token),
            cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicate<ProcessedIntegrationEvent>(
            this,
            () => base.SaveChanges(acceptAllChangesOnSuccess));
    }
}
