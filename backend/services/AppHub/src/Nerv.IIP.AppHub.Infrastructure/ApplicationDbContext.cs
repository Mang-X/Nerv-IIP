using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using Nerv.IIP.AppHub.Infrastructure.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), ICapDataStorage
{
    public DbSet<AppHubApplication> Applications => Set<AppHubApplication>();
    public DbSet<ApplicationVersion> ApplicationVersions => Set<ApplicationVersion>();
    public DbSet<ManagedNode> ManagedNodes => Set<ManagedNode>();
    public DbSet<ApplicationInstance> ApplicationInstances => Set<ApplicationInstance>();
    public DbSet<InstanceHeartbeat> InstanceHeartbeats => Set<InstanceHeartbeat>();
    public DbSet<ConnectorCollectionHealthProjection> ConnectorCollectionHealth => Set<ConnectorCollectionHealthProjection>();
    public DbSet<InstanceStateHistory> InstanceStateHistory => Set<InstanceStateHistory>();
    public DbSet<InstanceStatusChange> InstanceStatusChanges => Set<InstanceStatusChange>();
    public DbSet<RegistrationIdempotency> RegistrationIdempotency => Set<RegistrationIdempotency>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();
    public DbSet<PublishedMessage> PublishedMessages => Set<PublishedMessage>();
    public DbSet<ReceivedMessage> ReceivedMessages => Set<ReceivedMessage>();
    public DbSet<CapLock> CapLocks => Set<CapLock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("apphub");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ConfigureIntegrationEventDeadLetters();
        ConfigureCapStorage(modelBuilder);
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

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }
}
