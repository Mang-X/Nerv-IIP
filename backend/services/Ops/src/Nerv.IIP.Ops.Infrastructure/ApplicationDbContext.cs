using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure;

public sealed partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), ICapDataStorage
{
    public DbSet<OperationTask> OperationTasks => Set<OperationTask>();
    public DbSet<OperationAttempt> OperationAttempts => Set<OperationAttempt>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<PublishedMessage> PublishedMessages => Set<PublishedMessage>();
    public DbSet<ReceivedMessage> ReceivedMessages => Set<ReceivedMessage>();
    public DbSet<CapLock> CapLocks => Set<CapLock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ops");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ConfigureCapStorage(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }
}
