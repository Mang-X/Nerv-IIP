using MediatR;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<OperationTask> OperationTasks => Set<OperationTask>();

    public DbSet<ProductionReport> ProductionReports => Set<ProductionReport>();

    public DbSet<ScheduleResult> ScheduleResults => Set<ScheduleResult>();

    public DbSet<WorkCenterUnavailability> WorkCenterUnavailabilities => Set<WorkCenterUnavailability>();

    public DbSet<DeviceAssetWorkCenterMapping> DeviceAssetWorkCenterMappings => Set<DeviceAssetWorkCenterMapping>();

    public DbSet<FinishedGoodsReceiptRequest> FinishedGoodsReceiptRequests => Set<FinishedGoodsReceiptRequest>();

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

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }
}
